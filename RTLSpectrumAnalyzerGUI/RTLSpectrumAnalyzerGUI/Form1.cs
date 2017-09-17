/*
* Author: Clint Mclean
*
* RTLSpectrumAnalyzerGUI turns a RTL2832 based DVB dongle into a spectrum analyzer
* 
* 
* Uses RTLSDRDevice.DLL for doing the frequency scans
* which makes use of the librtlsdr library: https://github.com/steve-m/librtlsdr
* and based on that library's included rtl_power.c code to get frequency strength readings
* 
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 2 of the License, or
* (at your option) any later version.
*
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
*
* You should have received a copy of the GNU General Public License
* along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using System.Threading;
using System.IO;

using System.Threading.Tasks;

namespace RTLSpectrumAnalyzerGUI
{    
    public partial class Form1 : Form
    {
        public const long MAXIMUM_GRAPH_BIN_COUNT = 100000;
        const double nearSignalMin = 10;    
        double graph1BinFreqInc;
        double graph2BinFreqInc;
        double graph1LowerFrequency;
        double graph1UpperFrequency;

        double graph2LowerFrequency;
        double graph2UpperFrequency;

        List<long> nearSignal = new List<long>();

        uint dataLowerFrequency = 0, dataUpperFrequency = 0, stepSize;

        double difThreshold = 0;
        uint totalBinCount = 0;        

        bool recordingSeries1 = false;
        bool recordingSeries2 = false;

        float[] difBinArray = null;

        bool resetGraph = true;

        bool newData = false;

        double minAvgStrength = 99999999;
        double maxAvgStrength = -99999999;

        Waterfall waterFall, waterFallAvg;

        double prevWaterFallMinimum;
        double prevWaterFallMaximum;
        double prevNearStrengthDeltaRange;
       

        float avgTotalMagnitude;
        int magnitudeBufferCount = 0;

        long[] magnitudeBuffer = new long[10];

        bool chart3RangeSet = false;

        double series1MinYChart1 = 99999999, series2MinYChart1 = 99999999;
        double series1MaxYChart1 = -99999999, series2MaxYChart1 = -99999999;

        static public double series1MinYChart2 = 99999999, series2MinYChart2 = 99999999;
        static public double series1MaxYChart2 = -99999999, series2MaxYChart2 = -99999999;

        static public double series2Max = -99999999;

        double averageSeries1CurrentFrameStrength = 0;
        double averageSeries2CurrentFrameStrength = 0;

        double averageSeries1TotalFramesStrength = 0;
        double averageSeries2TotalFramesStrength = 0;

        int deviceCount = 1;
        bool getDataForSeries1 = true;
        bool getDataForSeries2 = false;


        List<InterestingSignal> leaderBoardSignals = new List<InterestingSignal>();
        List<InterestingSignal> interestingSignals = new List<InterestingSignal>();


        int leaderBoardSignalIndex;
        short MAX_LEADER_BOARD_COUNT = 4;
        short MAX_LEADER_BOARD_LIST_COUNT = 4;

        string evaluatedFrequencyString = "";


        long prevSoundTime;

        System.Windows.Forms.Timer eventTimer;

        int currentLeaderBoardSignalIndex = -1;

        bool analyzingLeaderBoardSignals = false;

        string originalStartFrequency;

        string originalEndFrequency;

        class BackgroundBeep
        {
            static Thread _beepThread;
            static AutoResetEvent _signalBeep;

             static int frequency = 1000;
             static int duration = 1000;


            static BackgroundBeep()
            {
                _signalBeep = new AutoResetEvent(false);
                _beepThread = new Thread(() =>
                {
                    for (; ; )
                    {
                        _signalBeep.WaitOne();
                        Console.Beep(frequency, duration);
                    }
                }, 1);
                _beepThread.IsBackground = true;
                _beepThread.Start();
            }

            public static void Beep(int soundFrequency, int soundDuration)
            {
                frequency = soundFrequency;
                duration = soundDuration;
                _signalBeep.Set();
            }
        }

        Form2 form2 = new Form2();

        class BinData
        {
            public string dataSeries;

            public float[] binArray = null;
            public float[] avgBinArray = null;
            public float[] totalBinArray = null;
            
            public uint numberOfFrames = 0;
            public uint size = 0;

            public BinData(uint size, string series)
            {
                this.size = size;

                dataSeries = series;

                totalBinArray = new float[size];
                avgBinArray = new float[size];
                binArray = new float[size];
            }

            public void Clear()
            {
                for (int i = 0; i < this.size; i++)
                {
                    totalBinArray[i] = 0;

                    avgBinArray[i] = 0;
                    binArray[i] = 0;
                }

                numberOfFrames = 0;
            }
        }

        BinData series1BinData;
        BinData series2BinData;        
        

        double binSize;        

        class FrequencyRange
        {
            public double lower;
            public double upper;

            public FrequencyRange(double lower, double upper)
            {
                this.lower = lower;
                this.upper = upper;
            }
        }


        Stack<FrequencyRange> graph1FrequencyRanges = new Stack<FrequencyRange>();
        Stack<FrequencyRange> graph2FrequencyRanges = new Stack<FrequencyRange>();

        class InterestingSignal
        {
            public int index = -1;
            public double strength = 0;
            public double strengthDif = 0;
            public double rating = 0;
            public double frequency = 0;

            public double totalChange = 0;
            public double avgChange = 0;            

            private int MAX_AVG_COUNT= 10;
            private int avgCount = 0;

            public InterestingSignal(int index, double strength, double strengthDif, double frequency)
            {
                this.index = index;
                this.strength = strength;
                this.strengthDif = strengthDif;

                this.frequency = frequency;
            }

            public void SetStrength(double strength)
            {                                
                totalChange += (strength - this.strength);

                this.strength = strength;

                avgCount++;

                if (avgCount>=MAX_AVG_COUNT)
                {
                    avgChange = totalChange / MAX_AVG_COUNT;

                    totalChange = 0;

                    avgCount = 0;
                }
            }

            public double AvgChange()
            {
                return avgChange;                
            }

            public void ResetAvgChange()
            {
                avgChange = 0;
            }
        }

        private string GetFrequencyString(double frequency, short decimalPlaces=4)
        {            
            return (Math.Round((frequency/ 1000000), decimalPlaces)).ToString() + "MHz";
        }

        private double GetFrequencyValue(string frequency, short decimalPlaces = 4)
        {
            return Math.Round(double.Parse(frequency), decimalPlaces);
        }

        private double FrequencyToMHz(double frequency, short decimalPlaces = 4)
        {
            return Math.Round(frequency / 1000000, decimalPlaces);
        }
        
        private double RangeChanged(System.Windows.Forms.DataVisualization.Charting.Chart chart, string dataSeries, float[] data, long lowerIndex, long upperIndex, double newLowerFrequency, ref double graphBinFreqInc)
        {
            if (data.Length > 0)
            {                
                long graphBinCount = upperIndex - lowerIndex;

                long lowerResGraphBinCount;

                if (graphBinCount > MAXIMUM_GRAPH_BIN_COUNT)
                    lowerResGraphBinCount = MAXIMUM_GRAPH_BIN_COUNT;
                else
                    lowerResGraphBinCount = graphBinCount;

                double inc = (double)graphBinCount / lowerResGraphBinCount;

                graphBinFreqInc = inc * binSize;

                double index = lowerIndex;

                double value;

                double binFrequency = newLowerFrequency;


                int minYIndex = -1, maxYIndex = -1;

                double minY= 99999999, maxY=-99999999;

                chart.Series[dataSeries].MarkerStyle = System.Windows.Forms.DataVisualization.Charting.MarkerStyle.Star10;

                InterestingSignal interestingSignal;

                int interestingSignalIndex;

                System.Windows.Forms.DataVisualization.Charting.DataPoint graphPoint1;
                for (int i = 0; i < lowerResGraphBinCount; i++)
                {
                    value = data[(long)index];

                    /*if (Double.IsNaN(value) || value > 1000000 || value < -100)
                    {
                        value = -25;
                    }                    
                    else
                    */
                    /*if (Double.IsNaN(value) || value > 100 || value < 0)
                    {
                        value = 0;
                    }
                    else
                    */
                    {
                        if (i < chart.Series[dataSeries].Points.Count)
                        {
                            graphPoint1 = chart.Series[dataSeries].Points.ElementAt(i);

                            ////if (dataSeries == "Series3")
                                graphPoint1.SetValueXY(i, value);

                            graphPoint1.AxisLabel = GetFrequencyString(binFrequency);
                        }
                        else
                        {
                            graphPoint1 = new System.Windows.Forms.DataVisualization.Charting.DataPoint(i, value);                            
                            graphPoint1.AxisLabel = GetFrequencyString(binFrequency);

                            ////if (dataSeries == "Series3")
                            chart.Series[dataSeries].Points.Add(graphPoint1);                            
                        }

                        graphPoint1.Label = "";
                        //graphPoint1.Label = "433MHz";

                        if (interestingSignals != null && dataSeries == "Series3")
                        {
                            for (int j = (int)index; j < (int)(index + inc); j++)
                            {
                                interestingSignalIndex = interestingSignals.FindIndex(x => x.index == j);

                                if (interestingSignalIndex >= 0 && interestingSignalIndex < 4)
                                {
                                    interestingSignal = interestingSignals[interestingSignalIndex];

                                    //graphPoint1.Label = graphPoint1.AxisLabel;
                                    graphPoint1.Label = GetFrequencyString(binFrequency);


                                    /*normalizedColorRange = maxDelta / nearStrengthDeltaRange;

                                    if (normalizedColorRange > 1)
                                        normalizedColorRange = 1;

                                    if (normalizedColorRange < 0)
                                        normalizedColorRange = 0;


                                    SetPixel(x, y, colors[(int)((1 - normalizedColorRange) * (colors.Count - 1))]);
                                    */



                                    graphPoint1.LabelForeColor = Waterfall.colors[(int)((float)interestingSignalIndex / interestingSignals.Count * (Waterfall.colors.Count - 1))];

                                    ////graphPoint1.MarkerStyle = System.Windows.Forms.DataVisualization.Charting.MarkerStyle.Star10;

                                    break;
                                }
                            }
                        }



                        if (value < minY)
                        {
                            minY = value;

                            minYIndex = i;
                        }

                        if (value > maxY && index>0)
                        {
                            maxY = value;

                            maxYIndex = i;
                        }
                    }
                    

                    index += inc;

                    binFrequency += graphBinFreqInc;
                }


                double avgStrength = 0;
                int valueCount = 0;

                for (long i = lowerIndex+1; i < upperIndex; i++)
                {
                    value = data[i];

                    
                    /*if (Double.IsNaN(value) || value > 1000000 || value < -100)
                    {
                        value = -25;
                    }                                        
                    else
                    */
                    /*if (Double.IsNaN(value) || value > 100 || value < 0)
                    {
                        value = 0;
                    }
                    else
                    */
                    {
                        avgStrength += value;
                        valueCount++;
                    }
                }

                avgStrength /= valueCount;
                
                chart.Refresh();

                chart.ChartAreas[0].AxisX.ScaleView.Zoom(1, lowerResGraphBinCount-1);


                if (chart == chart1)
                {
                    if (dataSeries == "Series1")
                    {
                        series1MinYChart1 = minY;
                        series1MaxYChart1 = maxY;
                    }

                    if (dataSeries == "Series2")
                    {
                        series2MinYChart1 = minY;
                        series2MaxYChart1 = maxY;
                    }

                    minY = Math.Min(series1MinYChart1, series2MinYChart1);
                    maxY = Math.Max(series1MaxYChart1, series2MaxYChart1);
                }
                else
                {
                    if (dataSeries == "Series1")
                    {
                        series1MinYChart2 = minY;
                        series1MaxYChart2 = maxY;
                    }

                    if (dataSeries == "Series2")
                    {
                        series2MinYChart2 = minY;
                        series2MaxYChart2 = maxY;
                    }

                    minY = Math.Min(series1MinYChart2, series2MinYChart2);
                    maxY = Math.Max(series1MaxYChart2, series2MaxYChart2);
                }

                if (minY == maxY)
                    maxY = minY + 0.01;


                ////if (radioButton6.Checked || maxY <= 0)
                if (checkBox3.Checked || minY < chart.ChartAreas[0].AxisY.Minimum)
                    chart.ChartAreas[0].AxisY.Minimum = Math.Round(minY, 2);
                ///else
                ////chart.ChartAreas[0].AxisY.Minimum = 0;

                if (checkBox3.Checked || maxY > chart.ChartAreas[0].AxisY.Maximum)
                    chart.ChartAreas[0].AxisY.Maximum = Math.Round(maxY, 2);


                /*if (dataSeries == "Series1" || dataSeries == "Series2")
                {
                    ////minY = data[(long)minYIndex];
                    
                    graphPoint1 = chart.Series[j].Points.ElementAt(minYIndex);
                    minY = Math.Min(minY, graphPoint1.YValues[0]);


                    for (int j=0; j<chart.Series.Count; j++)
                    {
                        if (minYIndex < chart.Series[j].Points.Count)
                        {
                            graphPoint1 = chart.Series[j].Points.ElementAt(minYIndex);

                            minY = Math.Min(minY, graphPoint1.YValues[0]);

                            graphPoint1 = chart.Series[j].Points.ElementAt(maxYIndex);
                            maxY = Math.Max(maxY, graphPoint1.YValues[0]);
                        }
                    }

                    

                    ////if (minY < chart.ChartAreas[0].AxisY.Minimum)
                    chart.ChartAreas[0].AxisY.Minimum = Math.Round(minY, 2);

                    ////if (maxY > chart.ChartAreas[0].AxisY.Maximum)
                        chart.ChartAreas[0].AxisY.Maximum = Math.Round(maxY, 2);
                }*/


                /*if (dataSeries == "Series3" && minY!=maxY)
                {
                    chart.ChartAreas[0].AxisY.Minimum = Math.Round(minY, 2);

                    chart.ChartAreas[0].AxisY.Maximum = Math.Round(maxY, 2);
                }*/

                if (chart == chart2)
                ////if (chart == chart1)
                {
                    if (dataSeries == "Series1")
                        textBox7.Text = Math.Round(avgStrength,3).ToString();

                    if (dataSeries == "Series2")
                        textBox8.Text = Math.Round(avgStrength, 3).ToString();
                }

                return avgStrength;
            }

            return 0;
        }

        private void AxisViewChanged(System.Windows.Forms.DataVisualization.Charting.Chart chart, string dataSeries, float[] data, ref double lowerFrequency, ref double upperFrequency, ref double graphBinFreqInc)
        {
            if (data.Length > 0)
            {
                double min;
                double max;

                if (newData)
                {
                    min = 0;
                    max = totalBinCount;
                }
                else
                {
                    min = chart.ChartAreas[0].AxisX.ScaleView.ViewMinimum;
                    max = chart.ChartAreas[0].AxisX.ScaleView.ViewMaximum + 1;

                    min--;

                    if (min < 0)
                        min = 0;

                    if (max == 1)
                        max = chart.Series[dataSeries].Points.Count;

                    if (max <= 0 || max > totalBinCount)
                        max = totalBinCount;
                }

                upperFrequency = lowerFrequency + max * graphBinFreqInc;
                lowerFrequency = lowerFrequency + min * graphBinFreqInc;

                long lowerIndex = (long)((lowerFrequency - dataLowerFrequency) / binSize);
                long upperIndex = (long)((upperFrequency - dataLowerFrequency) / binSize);

                    RangeChanged(chart, dataSeries, data, lowerIndex, upperIndex, lowerFrequency, ref graphBinFreqInc);

                    if ((dataSeries == "Series1" || dataSeries == "Series2") && (recordingSeries1 || recordingSeries2))
                    {
                        if (chart == chart1)
                        {
                            if (recordingSeries2 && waterFall.GetMode() == WaterFallMode.Strength)
                            {
                                waterFall.RefreshWaterfall(series2BinData.binArray, series1BinData.binArray, lowerIndex + 1, upperIndex);

                                waterFall.CalculateRanges(series2BinData.binArray, series1BinData.binArray);
                                ////waterFallAvg.CalculateRanges(series2BinData.avgBinArray, series1BinData.avgBinArray);
                            }
                            else
                            {
                                waterFall.RefreshWaterfall(series1BinData.binArray, series2BinData.binArray, lowerIndex + 1, upperIndex);

                                waterFall.CalculateRanges(series1BinData.binArray, series2BinData.binArray);
                                ////waterFallAvg.CalculateRanges(series1BinData.avgBinArray, series2BinData.avgBinArray);
                            }

                            /*if (recordingSeries1)
                                waterFall.RefreshWaterfall(series1BinData.binArray, series2BinData.binArray, lowerIndex + 1, upperIndex);
                            else
                                waterFall.RefreshWaterfall(series2BinData.binArray, series1BinData.binArray, lowerIndex + 1, upperIndex);
                             */ 
                             
                        }
                        else
                        {
                            if (chart == chart2)
                            {
                                if (recordingSeries2 && waterFallAvg.GetMode() == WaterFallMode.Strength)
                                {
                                    waterFallAvg.RefreshWaterfall(series2BinData.avgBinArray, series1BinData.avgBinArray, lowerIndex + 1, upperIndex);

                                    ////waterFall.CalculateRanges(series2BinData.binArray, series1BinData.binArray);
                                    waterFallAvg.CalculateRanges(series2BinData.avgBinArray, series1BinData.avgBinArray);
                                }
                                else
                                {
                                double nearStrengthDeltaRange = 0;

                                if (interestingSignals != null && interestingSignals.Count > 0)
                                {
                                    int i = 0;

                                    while (i < interestingSignals.Count && interestingSignals[i].strengthDif > 0)
                                        nearStrengthDeltaRange = interestingSignals[i++].strengthDif;                                    
                                }

                                if (nearStrengthDeltaRange>0)
                                    waterFallAvg.SetNearStrengthDeltaRange(nearStrengthDeltaRange);
                                else
                                    waterFallAvg.CalculateRanges(series1BinData.avgBinArray, series2BinData.avgBinArray);

                                waterFallAvg.RefreshWaterfall(series1BinData.avgBinArray, series2BinData.avgBinArray, lowerIndex + 1, upperIndex);

                                    ////waterFall.CalculateRanges(series1BinData.binArray, series2BinData.binArray);
                                    ////waterFallAvg.CalculateRanges(series1BinData.avgBinArray, series2BinData.avgBinArray);                                    
                            }

                                if (waterFallAvg.GetMode() == WaterFallMode.Difference && waterFallAvg.GetRangeMode() == WaterFallRangeMode.Auto)
                                    textBox10.Text = Math.Round(waterFallAvg.GetNearStrengthDeltaRange(), 2).ToString();
                                else
                                    if (waterFallAvg.GetMode() == WaterFallMode.Strength && waterFallAvg.GetRangeMode() == WaterFallRangeMode.Auto)
                                    {
                                        textBox9.Text = Math.Round(waterFallAvg.GetStrengthMinimum(), 2).ToString();
                                        textBox10.Text = Math.Round(waterFallAvg.GetStrengthMaximum(), 2).ToString();
                                    }
                            }
                        }

                        //waterFall.CalculateRanges(series1BinData.binArray, series2BinData.binArray);
                        //waterFallAvg.CalculateRanges(series1BinData.avgBinArray, series2BinData.avgBinArray);
                    }
            }
        }

        private void chart1_AxisViewChanged(object sender, System.Windows.Forms.DataVisualization.Charting.ViewEventArgs e)
        {
            FrequencyRange frequencyRange = new FrequencyRange(graph1LowerFrequency, graph1UpperFrequency);
            graph1FrequencyRanges.Push(frequencyRange);

            if (series1BinData!=null)
            {
                AxisViewChanged(chart1, series1BinData.dataSeries, series1BinData.binArray, ref graph1LowerFrequency, ref graph1UpperFrequency, ref graph1BinFreqInc);                
            }

            if (series2BinData!=null)
            {
                AxisViewChanged(chart1, series2BinData.dataSeries, series2BinData.binArray, ref graph1LowerFrequency, ref graph1UpperFrequency, ref graph1BinFreqInc);                
            }
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            if (graph1FrequencyRanges.Count > 0)
            {
                FrequencyRange frequencyRange = graph1FrequencyRanges.Pop();

                long lowerIndex = (long)((frequencyRange.lower - dataLowerFrequency) / binSize);
                long upperIndex = (long)((frequencyRange.upper - dataLowerFrequency) / binSize);

                graph1LowerFrequency = frequencyRange.lower;
                graph1UpperFrequency = frequencyRange.upper;

                if (series1BinData != null)
                    RangeChanged(chart1, series1BinData.dataSeries, series1BinData.binArray, lowerIndex, upperIndex, graph1LowerFrequency, ref graph1BinFreqInc);

                if (series2BinData != null)
                    RangeChanged(chart1, series2BinData.dataSeries, series2BinData.binArray, lowerIndex, upperIndex, graph1LowerFrequency, ref graph1BinFreqInc);

                if (series1BinData != null && series2BinData != null)
                    GraphDifference(series1BinData, series2BinData);
            }
        }

        private void chart2_AxisViewChanged(object sender, System.Windows.Forms.DataVisualization.Charting.ViewEventArgs e)
        {
            FrequencyRange frequencyRange = new FrequencyRange(graph2LowerFrequency, graph2UpperFrequency);
            graph2FrequencyRanges.Push(frequencyRange);

            if (series1BinData != null)
            {
                AxisViewChanged(chart2, series1BinData.dataSeries, series1BinData.avgBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);                
            }

            if (series2BinData != null)
            {
                AxisViewChanged(chart2, series2BinData.dataSeries, series2BinData.avgBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);                
            }

            if (series1BinData != null && series2BinData != null)
                GraphDifference(series1BinData, series2BinData);
        }
        
        private void button2_Click_2(object sender, EventArgs e)
        {
            if (graph2FrequencyRanges.Count > 0)
            {
                FrequencyRange frequencyRange = graph2FrequencyRanges.Pop();

                long lowerIndex = (long)((frequencyRange.lower - dataLowerFrequency) / binSize);
                long upperIndex = (long)((frequencyRange.upper - dataLowerFrequency) / binSize);

                graph2LowerFrequency = frequencyRange.lower;
                graph2UpperFrequency = frequencyRange.upper;

                if (series1BinData != null)
                    RangeChanged(chart2, series1BinData.dataSeries, series1BinData.avgBinArray, lowerIndex, upperIndex, graph2LowerFrequency, ref graph2BinFreqInc);

                if (series2BinData != null)
                    RangeChanged(chart2, series2BinData.dataSeries, series2BinData.avgBinArray, lowerIndex, upperIndex, graph2LowerFrequency, ref graph2BinFreqInc);

                if (series1BinData != null && series2BinData != null)
                    GraphDifference(series1BinData, series2BinData);                              
            }
        }
        
        private void ScaleData(ref float[] binArray, double averageTotalFramesStrength1, double averageTotalFramesStrength2)
        {
            float ratio = (float) (averageTotalFramesStrength2 / averageTotalFramesStrength1);

            for (int j = 0; j < binArray.Length; j++)
            {
                binArray[j] *= ratio;                
            }
        }

            private void RecordData(ref BinData binData, ref double averageCurrentFrameStrength, ref double averageTotalFramesStrength, ref int totalMagnitude, int deviceIndex)
        {
            if (binData.binArray.Length==0)
                binData = new BinData(totalBinCount, binData.dataSeries);                        

            double value;

            averageCurrentFrameStrength = 0;

            averageTotalFramesStrength = 0;
            
            NativeMethods.GetBins(binData.binArray, deviceIndex);

            totalMagnitude = NativeMethods.GetTotalMagnitude();

            for (int j = 0; j < binData.size; j++)
            {
                value = binData.binArray[j];

                if (Double.IsNaN(value) || value < 0)
                {
                    //value = -25;

                    if (j>0)
                        value = binData.binArray[j-1];
                    else
                        if (j< binData.size-1)
                            value = binData.binArray[j + 1];


                    binData.binArray[j] = (float)value;
                }

                if (Double.IsNaN(value) || value < 0)
                {
                    value = -25;
                    binData.binArray[j] = (float)value;
                }

                    

                /*if (Double.IsNaN(value) || value > 100 || value < -100)
                    value = -25;
                    */

                binData.totalBinArray[j] += (float) value;

                averageCurrentFrameStrength += value;
            }

            binData.numberOfFrames++;            
            
            for (int j = 0; j < binData.size; j++)
            {
                binData.avgBinArray[j] = binData.totalBinArray[j] / binData.numberOfFrames;

                averageTotalFramesStrength += binData.avgBinArray[j];
            }

            averageCurrentFrameStrength /= binData.size;
            averageTotalFramesStrength /= binData.size;

            if (binData.numberOfFrames % 100 == 0)
            {
                minAvgStrength = 99999999;
                maxAvgStrength = -99999999;
            }

            if (averageTotalFramesStrength > maxAvgStrength)
                maxAvgStrength = averageTotalFramesStrength;

            if (averageTotalFramesStrength < minAvgStrength)
                minAvgStrength = averageTotalFramesStrength;

            if (resetGraph)
                newData = true;
        }

        private void GraphData(BinData binData)
        {
            if (binData.dataSeries=="Series1")
                textBox5.Text = binData.numberOfFrames.ToString();
            else
                textBox6.Text = binData.numberOfFrames.ToString();

            AxisViewChanged(chart1, binData.dataSeries, binData.binArray, ref graph1LowerFrequency, ref graph1UpperFrequency, ref graph1BinFreqInc);            
            chart1.Refresh();            

            AxisViewChanged(chart2, binData.dataSeries, binData.avgBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);
            chart2.Refresh();
            

            if (resetGraph && newData)
            {
                difBinArray = new float[totalBinCount];

                resetGraph = false;
                newData = false;
            }            
        }        

        private void GraphDifference(BinData series1BinData, BinData series2BinData)
        {
            if (series1BinData != null && series2BinData != null && series1BinData.numberOfFrames > 0 && series2BinData.numberOfFrames > 0 && series1BinData.size == series2BinData.size)
            {
                chart2.Series["Series3"].Points.Clear();

                if (radioButton2.Checked)
                    chart2.Series["Series3"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Column;
                else
                    chart2.Series["Series3"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;

                double minY = 99999999, maxY = -99999999;

                double interestingSignalDif;
                double dif;

                InterestingSignal interestingSignal;

                int interestingSignalIndex;

                interestingSignals.Clear();

                series2Max = -99999999;

                evaluatedFrequencyString = evaluatedFrequencyString.ToLower().Replace("mhz", "");
                ////double evaluatedFrequency = Math.Round(double.Parse(evaluatedFrequencyString), 3);

                double evaluatedFrequency = GetFrequencyValue(evaluatedFrequencyString, 3);


                string frequencyString;

                double frequencyValue;

                int i = 0;

                for (i = 0; i < totalBinCount; i++)
                {
                    if (series2BinData.avgBinArray[i] > series2Max)
                        series2Max = series2BinData.avgBinArray[i];
                }

                for (i = 0; i < totalBinCount; i++)
                {
                    //dif = (series2BinData.avgBinArray[i] - series1BinData.avgBinArray[i]) / series1BinData.avgBinArray[i];

                    interestingSignalDif = Waterfall.CalculateStrengthDifference(series1BinData.avgBinArray, series2BinData.avgBinArray, i);

                    dif = Waterfall.CalculateStrengthDifference2(series1BinData.avgBinArray, series2BinData.avgBinArray, i);
                    ////dif = Waterfall.CalculateStrengthDifference(series1BinData.avgBinArray, series2BinData.avgBinArray, i);

                    if (checkBox4.Checked)
                    { 
                        frequencyString = GetFrequencyString(dataLowerFrequency + (i * binSize));

                    ////frequencyValue = Math.Round(double.Parse(frequencyString.ToLower().Replace("mhz", "")), 3);


                    frequencyValue = GetFrequencyValue(frequencyString.ToLower().Replace("mhz", ""), 3);


                    /*if (!interestingSignals.Exists(x => i >= x.index-10 &&  i <= x.index+10))
                        interestingSignals.Add(new InterestingSignal(i, interestingSignalDif));
                        */



                        

                        if (frequencyValue == evaluatedFrequency)
                        {
                            interestingSignalIndex = interestingSignals.FindIndex(x => i == x.index);

                            if (interestingSignalIndex == -1)
                                interestingSignals.Add(new InterestingSignal(i, series2BinData.avgBinArray[i], interestingSignalDif, dataLowerFrequency + (i * binSize)));
                        }


                            
                            
                    }

                        
                        interestingSignalIndex = interestingSignals.FindIndex(x => i >= x.index - 10 && i <= x.index + 10);

                        if (interestingSignalIndex >= 0)
                        {

                        

                        interestingSignal = interestingSignals[interestingSignalIndex];

                            if (interestingSignalDif > interestingSignal.strengthDif)// || (frequencyValue == evaluatedFrequency && checkBox4.Checked))
                            {
                            frequencyValue = FrequencyToMHz(interestingSignal.frequency, 3);

                            if (!checkBox4.Checked || frequencyValue != evaluatedFrequency)
                                {
                                

                                interestingSignals.RemoveAt(interestingSignalIndex);
                                    //interestingSignals.Add(new InterestingSignal(i, series2BinData.avgBinArray[i], interestingSignalDif, dataLowerFrequency + (i * binSize)));
                                }
                            }
                        }
                        else
                            interestingSignals.Add(new InterestingSignal(i, series2BinData.avgBinArray[i], interestingSignalDif, dataLowerFrequency + (i * binSize)));



                        interestingSignals.Sort(delegate (InterestingSignal x, InterestingSignal y)
                        {
                            if (x.strengthDif < y.strengthDif)
                                return 1;
                            else if (x.strengthDif == y.strengthDif)
                                return 0;
                            else
                                return -1;
                        });

                        if (interestingSignals.Count > 10)
                        {                            
                            if (FrequencyToMHz(interestingSignals[interestingSignals.Count - 1].frequency, 3) != evaluatedFrequency)
                                    interestingSignals.RemoveAt(10);
                        }
                    

                    if (!checkBox1.Checked || dif >= difThreshold)
                        ////difBinArray[i] = (float)dif;
                        //difBinArray[i] = (float)series2BinData.avgBinArray[i];
                        //difBinArray[i] = (float) chart2.ChartAreas[0].AxisY.Minimum + (float) series2BinData.avgBinArray[i];

                        difBinArray[i] = (float)chart2.ChartAreas[0].AxisY.Minimum + (float)dif;
                    else
                        difBinArray[i] = -99999999;


                    if (dif < minY)
                        minY = dif;

                    if (dif > maxY)
                        maxY = dif;
                }

                //if (!analyzingLeaderBoardSignals)
                {
                    for (i = 0; i < interestingSignals.Count; i++)
                    {
                        leaderBoardSignalIndex = leaderBoardSignals.FindIndex(x => FrequencyToMHz(interestingSignals[i].frequency,3) == FrequencyToMHz(x.frequency,3));

                        if (leaderBoardSignalIndex >= 0)
                        {
                            ////leaderBoardSignals[leaderBoardSignalIndex].rating += (interestingSignals.Count - i);

                            if (!analyzingLeaderBoardSignals)
                                leaderBoardSignals[leaderBoardSignalIndex].rating += interestingSignals[i].strengthDif;

                            /*if (leaderBoardSignalIndex == 0)
                                ////if (interestingSignals[i].strength > leaderBoardSignals[leaderBoardSignalIndex].strength + 10)
                                if (leaderBoardSignals[leaderBoardSignalIndex].AvgChange()>0)
                                {
                                    System.Media.SystemSounds.Asterisk.Play();

                                    form2.BackColor = Color.Red;
                                }
                                else
                                    form2.BackColor = Color.Blue;
                                    */

                            ////leaderBoardSignals[leaderBoardSignalIndex].strength = interestingSignals[i].strength;
                            leaderBoardSignals[leaderBoardSignalIndex].SetStrength(interestingSignals[i].strength);
                        }
                        else if (!analyzingLeaderBoardSignals)
                        {
                            leaderBoardSignals.Add(interestingSignals[i]);
                            leaderBoardSignals[leaderBoardSignals.Count - 1].rating += interestingSignals[i].strengthDif;
                        }
                    }

                    if (!analyzingLeaderBoardSignals)
                    {
                        leaderBoardSignals.Sort(delegate (InterestingSignal x, InterestingSignal y)
                        {
                            if (x.rating < y.rating)
                                return 1;
                            else if (x.rating == y.rating)
                                return 0;
                            else
                                return -1;
                        });


                        if (leaderBoardSignals.Count > MAX_LEADER_BOARD_COUNT)
                        {
                            i = leaderBoardSignals.Count - 1;
                            while (i > MAX_LEADER_BOARD_COUNT - 1)
                            {
                                if (FrequencyToMHz(leaderBoardSignals[i].frequency, 3) != evaluatedFrequency)
                                    leaderBoardSignals.RemoveAt(i);
                                else
                                    leaderBoardSignals.RemoveAt(i-1);
                                i--;
                            }
                        }

                        listBox1.Items.Clear();
                    }
                }
                    long soundDelay;

                    bool foundSignal = false;
                    int soundFrequency;

                    i = 0;
                    while (i < leaderBoardSignals.Count)
                    {
                        frequencyValue = FrequencyToMHz(leaderBoardSignals[i].frequency, 3);

                        frequencyString = GetFrequencyString(leaderBoardSignals[i].frequency);

                    if (checkBox4.Checked && !foundSignal)
                        {
                            if (frequencyValue == evaluatedFrequency)
                            {
                                ////if (interestingSignals[i].strength > leaderBoardSignals[leaderBoardSignalIndex].strength + 10)
                                if (leaderBoardSignals[i].AvgChange() > 10)
                                {
                                    soundDelay = DateTime.Now.Ticks - prevSoundTime;
                                    if (soundDelay > 10000000)
                                    {
                                    /*System.Media.SystemSounds.Asterisk.Play();
                                    Thread.Sleep(1000);
                                    System.Media.SystemSounds.Beep.Play();
                                    Thread.Sleep(1000);
                                    System.Media.SystemSounds.Exclamation.Play();
                                    Thread.Sleep(1000);
                                    */
                                    ////System.Media.SystemSounds.Hand.Play();

                                    soundFrequency = (int) leaderBoardSignals[i].AvgChange();

                                    if (soundFrequency < 100)
                                        soundFrequency = 100;
                                    else if (soundFrequency > 17000)
                                        soundFrequency = 10000;

                                    ////Console.Beep(soundFrequency, 1000);

                                    BackgroundBeep.Beep(soundFrequency, 1000);

                                    ////Console.Beep(1000, 1000);

                                    /*Thread.Sleep(1000);
                                    System.Media.SystemSounds.Question.Play();
                                    */





                                    prevSoundTime = DateTime.Now.Ticks;

                                        leaderBoardSignals[i].ResetAvgChange();
                                    }

                                    form2.BackColor = Color.Red;
                                }
                                else
                                    form2.BackColor = Color.Blue;

                                foundSignal = true;
                            }
                        }

                        if (!analyzingLeaderBoardSignals)
                            if (i < MAX_LEADER_BOARD_LIST_COUNT)
                            {
                                listBox1.Items.Add(frequencyString + ": " + Math.Round(leaderBoardSignals[i].rating / 100000));
                            }

                        i++;
                    }
                





                AxisViewChanged(chart2, "Series3", difBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);
                /*
                ////if (dataSeries == "Series3" && minY != maxY)
                {
                    ////chart2.ChartAreas[0].AxisY.Minimum = Math.Round(minY, 2);

                    chart2.ChartAreas[0].AxisY.Minimum = minY;

                    chart2.ChartAreas[0].AxisY.Maximum = Math.Round(maxY, 2);
                }*/
                
                chart2.Refresh();
            }            
        }

        private void AddGradientPoint(System.Windows.Forms.DataVisualization.Charting.Chart chart, TextBox textBox, double gradientValue)
        {
            textBox.Text = gradientValue.ToString();

            ////graphPoint = new System.Windows.Forms.DataVisualization.Charting.DataPoint(chart4.Series["Series1"].Points.Count, gradient);
            System.Windows.Forms.DataVisualization.Charting.DataPoint graphPoint = new System.Windows.Forms.DataVisualization.Charting.DataPoint(chart4.Series["Series1"].Points.Count, gradientValue);


            if (chart.Series["Series1"].Points.Count > 100)
            {
                chart.Series["Series1"].Points.RemoveAt(0);

                for (int j = 0; j < chart.Series["Series1"].Points.Count; j++)
                {
                    chart.Series["Series1"].Points[j].XValue--;
                }
            }

            chart.Series["Series1"].Points.Add(graphPoint);

            if (chart.Series["Series1"].Points.Count > 10)
            {
                double minY = 99999999;
                double maxY = -99999999;

                for (int j = 0; j < chart.Series["Series1"].Points.Count; j++)
                {
                    ////chart.Series["Series1"].Points[j].XValue--;

                    if (chart.Series["Series1"].Points[j].YValues[0] < minY)
                        minY = chart.Series["Series1"].Points[j].YValues[0];

                    if (chart.Series["Series1"].Points[j].YValues[0] > maxY)
                        maxY = chart.Series["Series1"].Points[j].YValues[0];
                }

                if (minY == maxY)
                {
                    maxY++;
                    minY--;
                }

                chart.ChartAreas[0].AxisY.Maximum = maxY;
                chart.ChartAreas[0].AxisY.Minimum = minY;
            }

        }



        private void AddPointToAverageGraph(float value)
        {
            System.Windows.Forms.DataVisualization.Charting.DataPoint graphPoint = new System.Windows.Forms.DataVisualization.Charting.DataPoint(chart3.Series["Series1"].Points.Count, value);

            if (chart3.Series["Series1"].Points.Count > 10)
            {
                double minY = 99999999;
                double maxY = -99999999;

                for (int j = 0; j < chart3.Series["Series1"].Points.Count; j++)
                {
                    ////chart3.Series["Series1"].Points[j].XValue--;

                    if (chart3.Series["Series1"].Points[j].YValues[0] < minY)
                        minY = chart3.Series["Series1"].Points[j].YValues[0];

                    if (chart3.Series["Series1"].Points[j].YValues[0] > maxY)
                        maxY = chart3.Series["Series1"].Points[j].YValues[0];
                }

                if (minY == maxY)
                {
                    maxY++;
                    minY--;
                }

                chart3.ChartAreas[0].AxisY.Maximum = maxY;
                chart3.ChartAreas[0].AxisY.Minimum = minY;
            }


            ////chart3.ResetAutoValues();



            if (chart3.Series["Series1"].Points.Count > 100)
            {
                chart3.Series["Series1"].Points.RemoveAt(0);

                for (int j = 0; j < chart3.Series["Series1"].Points.Count; j++)
                {
                    chart3.Series["Series1"].Points[j].XValue--;
                }

                    ////chart3.ChartAreas[0].RecalculateAxesScale();
            }

            chart3.Series["Series1"].Points.Add(graphPoint);

            if (chart3.Series["Series1"].Points.Count > 1)
            {
                double gradient = 0;
                double totalGradient = 0;
                double avgGradient = 0;

                for (int j = 1; j < chart3.Series["Series1"].Points.Count; j++)
                {
                    totalGradient += (chart3.Series["Series1"].Points[j].YValues[0] - chart3.Series["Series1"].Points[j - 1].YValues[0]);
                }

                avgGradient = totalGradient / (chart3.Series["Series1"].Points.Count - 1);

                if (chart3.Series["Series1"].Points.Count > 1)
                {
                    gradient = chart3.Series["Series1"].Points[chart3.Series["Series1"].Points.Count - 1].YValues[0] - chart3.Series["Series1"].Points[chart3.Series["Series1"].Points.Count - 2].YValues[0];
                }

                AddGradientPoint(chart4, textBox12, avgGradient);
            }


            if (chart4.Series["Series1"].Points.Count > 1)
            {
                double gradient2 = 0;
                double totalGradient2 = 0;
                double avgGradient2 = 0;

                if (chart4.Series["Series1"].Points.Count > 1)
                    gradient2 = chart4.Series["Series1"].Points[chart4.Series["Series1"].Points.Count - 1].YValues[0] - chart4.Series["Series1"].Points[chart4.Series["Series1"].Points.Count - 2].YValues[0];

                ////int avgGradient2PointsCount = 10;
                int avgGradient2PointsCount = chart4.Series["Series1"].Points.Count;

                if (chart4.Series["Series1"].Points.Count >= avgGradient2PointsCount)
                {

                    for (int j = chart4.Series["Series1"].Points.Count - 1; j > chart4.Series["Series1"].Points.Count - avgGradient2PointsCount; j--)
                    {
                        totalGradient2 += (chart4.Series["Series1"].Points[j].YValues[0] - chart4.Series["Series1"].Points[j - 1].YValues[0]);
                    }

                    avgGradient2 = totalGradient2 / (avgGradient2PointsCount - 1);
                }

                AddGradientPoint(chart5, textBox13, avgGradient2);

                ////AddGradientPoint(chart5, textBox13, gradient2);
            }

            ////textBox12.Text = (totalGradient / (chart3.Series["Series1"].Points.Count - 1)).ToString();


            /*if (chart3.Series["Series1"].Points.Count > 1)
        {
            gradient = chart3.Series["Series1"].Points[chart3.Series["Series1"].Points.Count - 1].YValues[0] - chart3.Series["Series1"].Points[chart3.Series["Series1"].Points.Count - 2].YValues[0];

            textBox12.Text = avgGradient.ToString();

            ////graphPoint = new System.Windows.Forms.DataVisualization.Charting.DataPoint(chart4.Series["Series1"].Points.Count, gradient);
            graphPoint = new System.Windows.Forms.DataVisualization.Charting.DataPoint(chart4.Series["Series1"].Points.Count, avgGradient);


            if (chart4.Series["Series1"].Points.Count > 100)
            {
                chart4.Series["Series1"].Points.RemoveAt(0);

                for (int j = 0; j < chart4.Series["Series1"].Points.Count; j++)
                {
                    chart4.Series["Series1"].Points[j].XValue--;
                }

                    ////chart3.ChartAreas[0].RecalculateAxesScale();
            }

            chart4.Series["Series1"].Points.Add(graphPoint);

            if (chart4.Series["Series1"].Points.Count > 10)
            {
                double minY = 99999999;
                double maxY = -99999999;

                for (int j = 0; j < chart4.Series["Series1"].Points.Count; j++)
                {
                    ////chart4.Series["Series1"].Points[j].XValue--;

                    if (chart4.Series["Series1"].Points[j].YValues[0] < minY)
                        minY = chart4.Series["Series1"].Points[j].YValues[0];

                    if (chart4.Series["Series1"].Points[j].YValues[0] > maxY)
                        maxY = chart4.Series["Series1"].Points[j].YValues[0];
                }

                if (minY == maxY)
                {
                    maxY++;
                    minY--;
                }

                chart4.ChartAreas[0].AxisY.Maximum = maxY;
                chart4.ChartAreas[0].AxisY.Minimum = minY;
            }

        }
        */


            /*double gradient2 = 0;
            double totalGradient2 = 0;
            double avgGradient2 = 0;

            if (chart4.Series["Series1"].Points.Count > 1)
                gradient2 = chart4.Series["Series1"].Points[chart4.Series["Series1"].Points.Count - 1].YValues[0] - chart4.Series["Series1"].Points[chart4.Series["Series1"].Points.Count - 2].YValues[0];

            int avgGradient2PointsCount = 10;
            ////int avgGradient2PointsCount = chart4.Series["Series1"].Points.Count;

            if (chart4.Series["Series1"].Points.Count > avgGradient2PointsCount)
            {

                for (int j = chart4.Series["Series1"].Points.Count - 1; j > chart4.Series["Series1"].Points.Count - avgGradient2PointsCount; j--)
                {
                    totalGradient2 += (chart4.Series["Series1"].Points[j].YValues[0] - chart4.Series["Series1"].Points[j - 1].YValues[0]);
                }

                avgGradient2 = totalGradient2 / (avgGradient2PointsCount - 1);

                textBox13.Text = avgGradient2.ToString();
                ////textBox13.Text = gradient2.ToString();

                if (avgGradient2 > 0)
                {
                    int soundFrequency = (int)avgGradient2 * 100;

                    if (soundFrequency > 37 && soundFrequency < 32000)
                        BackgroundBeep.Beep(soundFrequency, 1000);
                }
            }*/


            /*if (!chart3RangeSet)
            {
                chart3.ChartAreas[0].AxisY.Maximum = value + 1;

                chart3.ChartAreas[0].AxisY.Minimum = value - 1;

                chart3RangeSet = true;
            }
            else
            {                
                if (value > chart3.ChartAreas[0].AxisY.Maximum)
                    chart3.ChartAreas[0].AxisY.Maximum = value;

                if (value < chart3.ChartAreas[0].AxisY.Minimum)
                    chart3.ChartAreas[0].AxisY.Minimum = value;
            }*/
        }


        private void GraphAverageStrength(BinData binData)
        {
            if (binData != null)
            {
                float averageStrength = 0;

                for (int i = 0; i < binData.avgBinArray.Length; i++)
                {
                    averageStrength += binData.avgBinArray[i];
                }

                averageStrength /= binData.avgBinArray.Length;

                if (binData.dataSeries == "Series1")
                    textBox7.Text = averageStrength.ToString();

                if (binData.dataSeries == "Series2")
                    textBox8.Text = averageStrength.ToString();
            }
            else
            {
                if (recordingSeries1)
                    AddPointToAverageGraph(float.Parse(textBox7.Text));

                if (recordingSeries2)
                    AddPointToAverageGraph(float.Parse(textBox8.Text));
            }
        }

        private void GraphTotalMagnitude(int totalMagnitude)
        {   
            /*
            ////if (binData.dataSeries == "Series1")
                textBox7.Text = totalMagnitude.ToString();

            ////if (binData.dataSeries == "Series2")
                textBox8.Text = totalMagnitude.ToString();
                */

            magnitudeBuffer[magnitudeBufferCount++] = totalMagnitude;

            if (magnitudeBufferCount == 10)
            {
                avgTotalMagnitude = 0;

                for (int i = 0; i < magnitudeBufferCount; i++)
                    avgTotalMagnitude += magnitudeBuffer[i];

                avgTotalMagnitude /= 10;


                magnitudeBufferCount = 0;

                AddPointToAverageGraph(avgTotalMagnitude);               
            }
        }




        private void button3_Click(object sender, EventArgs e)
        {
            if (button3.Text == "Record Series 1 Data (Far)")
            {
                if (series2BinData.numberOfFrames>0)
                    radioButton4.Enabled = true;    

                Task.Factory.StartNew(() =>
                {
                    recordingSeries1 = true;
                    while (recordingSeries1)
                    {
                        if ((deviceCount == 2 && getDataForSeries1) || deviceCount == 1)
                        {
                            //gettingDataForSeries1 = true;

                            int totalMagnitude = 0;

                            RecordData(ref series1BinData, ref averageSeries1CurrentFrameStrength, ref averageSeries1TotalFramesStrength, ref totalMagnitude, 0);

                            /*////if (averageSeries2CurrentFrameStrength > 0)
                                ScaleData(ref series1BinData.binArray, averageSeries1CurrentFrameStrength, averageSeries2CurrentFrameStrength);

                            if (averageSeries2TotalFramesStrength > 0)
                                ScaleData(ref series1BinData.avgBinArray, averageSeries1TotalFramesStrength, averageSeries2TotalFramesStrength);
                            */
                                
                            try
                            {
                                this.Invoke(new Action(() =>
                                {
                                    GraphData(series1BinData);
                                    GraphDifference(series1BinData, series2BinData);


                                    ////GraphTotalMagnitude(totalMagnitude);

                                    GraphAverageStrength(null);
                                }));
                            }
                            catch (Exception ex)
                            {

                            }

                            getDataForSeries1 = false;
                            getDataForSeries2 = true;
                        }
                    }


                    try
                    {
                        this.Invoke(new Action(() =>
                        {
                            button4.Enabled = true;
                            button5.Enabled = true;
                            button3.Text = "Record Series 1 Data (Far)";
                            button17.Text = "Record Far";
                            button18.Enabled = true;

                            this.Cursor = Cursors.Arrow;
                        }));
                    }
                    catch (Exception ex)
                    {

                    }
                    
                });

                if (deviceCount == 1)
                {
                    button4.Enabled = false;
                    button5.Enabled = false;
                    button18.Enabled = false;
                }

                button3.Text = "Stop Recording";
                button17.Text = "Stop";
            }
            else
            {
                this.Cursor = Cursors.WaitCursor;
                recordingSeries1 = false;
            }            
        }

        private void button5_Click(object sender, EventArgs e)
        {
            if (button5.Text == "Record Series 2 Data (Near)")
            {
                if (series1BinData.numberOfFrames > 0)
                {
                    radioButton4.Enabled = true;
                    radioButton4.Checked = true;
                }

                Task.Factory.StartNew(() =>
                {
                    recordingSeries2 = true;                    

                    while (recordingSeries2)
                    {
                        if ((deviceCount == 2 && getDataForSeries2) || deviceCount == 1)
                        {                            
                            int totalMagnitude = 0;
                            RecordData(ref series2BinData, ref averageSeries2CurrentFrameStrength, ref averageSeries2TotalFramesStrength, ref totalMagnitude, deviceCount - 1);

                            try
                            {
                                this.Invoke(new Action(() =>
                                {
                                    GraphData(series2BinData);
                                    GraphDifference(series1BinData, series2BinData);

                                    ////GraphTotalMagnitude(totalMagnitude);

                                    GraphAverageStrength(null);
                                }));
                            }
                            catch (Exception ex)
                            {

                            }

                            getDataForSeries2 = false;
                            getDataForSeries1 = true;

                        }
                    }


                    try
                    {
                        this.Invoke(new Action(() =>
                        {
                            button4.Enabled = true;
                            button5.Text = "Record Series 2 Data (Near)";
                            button18.Text = "Record Near";

                            button3.Enabled = true;
                            button17.Enabled = true;

                            this.Cursor = Cursors.Arrow;
                        }));
                    }
                    catch (Exception ex)
                    {

                    }                                            
                });

                if (deviceCount == 1)
                {
                    button3.Enabled = false;
                    button17.Enabled = false;
                    button4.Enabled = false;
                }

                button5.Text = "Stop Recording";
                button18.Text = "Stop";
            }
            else
            {
                this.Cursor = Cursors.WaitCursor;
                recordingSeries2 = false;
            }
        }

        private void LoadConfig()
        {
            TextReader tr = new StreamReader("config.txt");

            textBox1.Text = tr.ReadLine();
            textBox2.Text = tr.ReadLine();
            textBox3.Text = tr.ReadLine();
            textBox4.Text = tr.ReadLine();

            tr.Close();
        }

        private void SaveConfig()
        {
            TextWriter tw = new StreamWriter("config.txt");

            tw.WriteLine(originalStartFrequency);
            tw.WriteLine(originalEndFrequency);
            tw.WriteLine(stepSize);
            tw.WriteLine(difThreshold);

            tw.Close();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                dataLowerFrequency = uint.Parse(textBox1.Text);
                dataUpperFrequency = uint.Parse(textBox2.Text);

                if (!analyzingLeaderBoardSignals)
                {
                    originalStartFrequency = textBox1.Text;
                    originalEndFrequency = textBox2.Text;
                }


                evaluatedFrequencyString = textBox11.Text.ToLower();

                if (dataUpperFrequency <= dataLowerFrequency)
                    MessageBox.Show("End frequency must be greater than start frequency");
                else
                {
                    stepSize = uint.Parse(textBox3.Text);

                    difThreshold = double.Parse(textBox4.Text);

                    SaveConfig();

                    int result= 0;

                    try
                    {
                        result = NativeMethods.Initialize(dataLowerFrequency, dataUpperFrequency, stepSize);
                    }
                    catch(Exception ex)
                    {

                        MessageBox.Show(ex.ToString());
                    }

                    if (result < 0)
                    {
                        MessageBox.Show("Could not initialize. Is a device connected and not being used by another program?");
                    }
                    else
                    {

                        totalBinCount = NativeMethods.GetBufferSize();

                        binSize = (double)(dataUpperFrequency - dataLowerFrequency) / totalBinCount;

                        graph1BinFreqInc = binSize;
                        graph2BinFreqInc = binSize;

                        graph1LowerFrequency = dataLowerFrequency;
                        graph1UpperFrequency = dataUpperFrequency;

                        graph2LowerFrequency = dataLowerFrequency;
                        graph2UpperFrequency = dataUpperFrequency;

                        //if (series1BinData == null)
                            series1BinData = new BinData(0, "Series1");

                        //if (series2BinData == null)
                            series2BinData = new BinData(0, "Series2");                        

                        resetGraph = true;

                        radioButton3.Checked = true;
                        radioButton4.Enabled = false;

                        textBox5.Text = "0";
                        textBox6.Text = "0";

                        textBox7.Text = "0";
                        textBox8.Text = "0";
                       
                        chart1.Series["Series1"].Points.Clear();
                        chart2.Series["Series1"].Points.Clear();

                        chart1.Series["Series2"].Points.Clear();
                        chart2.Series["Series2"].Points.Clear();

                        chart2.Series["Series3"].Points.Clear();


                        ClearSeries1();
                        ClearSeries2();

                        graph1FrequencyRanges.Clear();
                        graph2FrequencyRanges.Clear();


                        button3.Enabled = true;
                        button5.Enabled = true;

                        button17.Enabled = true;
                        button18.Enabled = true;
                    }
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
        
        private void chart1_Click(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            recordingSeries1 = false;
            recordingSeries2 = false;
            
            Thread.Sleep(1000);            
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {            
        }

        private void LoadSeries(string filename, ref BinData series, string seriesString)
        {
            using (FileStream stream = new FileStream(filename, FileMode.Open))
            {                                
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    dataLowerFrequency = reader.ReadUInt32();
                    dataUpperFrequency = reader.ReadUInt32();
                    stepSize = reader.ReadUInt32();                    
                    totalBinCount = reader.ReadUInt32();

                    series = new BinData(totalBinCount, seriesString);
                    series.numberOfFrames = reader.ReadUInt32();

                    if (seriesString == "Series1")
                        textBox5.Text = series.numberOfFrames.ToString();
                    else
                        textBox6.Text = series.numberOfFrames.ToString();

                    binSize = (double)(dataUpperFrequency - dataLowerFrequency) / totalBinCount;

                    graph1LowerFrequency = dataLowerFrequency;
                    graph1UpperFrequency = dataUpperFrequency;

                    graph2LowerFrequency = dataLowerFrequency;
                    graph2UpperFrequency = dataUpperFrequency;

                    graph1BinFreqInc = binSize;
                    graph2BinFreqInc = binSize;

                    double value;
                    for (int i = 0; i < series.avgBinArray.Length; i++)
                    {
                        value = reader.ReadSingle();
                        
                        series.totalBinArray[i] = (float) value;

                        value /= series.numberOfFrames;

                        series.binArray[i] = (float) value;
                        series.avgBinArray[i] = (float) value;
                    }

                    reader.Close();
                }
            }

            resetGraph = true;
            newData = true;
        }

        private void SaveSeries(string filename, BinData series)
        {
            if (series != null)
            {
                using (FileStream stream = new FileStream(filename, FileMode.Create))
                {
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(dataLowerFrequency);
                        writer.Write(dataUpperFrequency);
                        writer.Write(stepSize);
                        writer.Write(totalBinCount);
                        writer.Write(series.numberOfFrames);

                        for (int i = 0; i < series.avgBinArray.Length; i++)
                        {
                            writer.Write(series.totalBinArray[i]);
                        }

                        writer.Close();
                    }
                }
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            DialogResult result = saveFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {                
                SaveSeries(saveFileDialog1.FileName, series1BinData);
            }
            
        }

        private void button7_Click(object sender, EventArgs e)
        {
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                LoadSeries(openFileDialog1.FileName, ref series1BinData, "Series1");

                GraphData(series1BinData);
                GraphDifference(series1BinData, series2BinData);                
            }            
        }

        private void button8_Click(object sender, EventArgs e)
        {
            DialogResult result = saveFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                SaveSeries(saveFileDialog1.FileName, series2BinData);
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {                
                LoadSeries(openFileDialog1.FileName, ref series2BinData, "Series2");

                GraphData(series2BinData);
                GraphDifference(series1BinData, series2BinData);
            }            
        }

        private void button10_Click(object sender, EventArgs e)
        {
            try
            {
                difThreshold = double.Parse(textBox4.Text);

                SaveConfig();


                GraphDifference(series1BinData, series2BinData);
            }
            catch (Exception)
            {

            }            
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton2.Checked)
            {
                checkBox1.Checked = true;
                checkBox1.Enabled = false;

                GraphDifference(series1BinData, series2BinData);
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton1.Checked)
            {                
                checkBox1.Enabled = true;

                GraphDifference(series1BinData, series2BinData);
            }

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (!checkBox1.Checked)
            {
                textBox4.Enabled = false;
                button10.Enabled = false;
            }
            else
            {
                textBox4.Enabled = true;
                button10.Enabled = true;
            }

            GraphDifference(series1BinData, series2BinData);
        }

        private void ClearSeries1()
        {
            if (series1BinData != null)
            {
                chart1.Series["Series1"].Points.Clear();
                chart2.Series["Series1"].Points.Clear();

                series1BinData.Clear();

                if (chart2.Series["Series3"].Points.Count > 0)
                    chart2.Series["Series3"].Points.Clear();


                series1MinYChart1 = 99999999;
                series1MaxYChart1 = -99999999;

                series1MinYChart2 = 99999999;
                series1MaxYChart2 = -99999999;

                GraphData(series1BinData);
                GraphDifference(series1BinData, series2BinData);

                textBox7.Text = "0";
            }
        }

        private void ClearSeries2()
        {
            if (series2BinData != null)
            {
                chart1.Series["Series2"].Points.Clear();
                chart2.Series["Series2"].Points.Clear();

                series2BinData.Clear();

                if (chart2.Series["Series3"].Points.Count > 0)
                    chart2.Series["Series3"].Points.Clear();

                series2MinYChart1 = 99999999;
                series2MaxYChart1 = -99999999;

                series2MinYChart2 = 99999999;
                series2MaxYChart2 = -99999999;

                GraphData(series2BinData);
                GraphDifference(series1BinData, series2BinData);

                textBox8.Text = "0";
            }
        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (!recordingSeries1)
            {
                radioButton3.Checked = true;
                radioButton4.Enabled = false;
            }

            ClearSeries1();
        }

        private void button12_Click(object sender, EventArgs e)
        {
            if (!recordingSeries2)
            {
                radioButton3.Checked = true;
                radioButton4.Enabled = false;
            }

            ClearSeries2();
        }


        public Form1()
        {
            InitializeComponent();            

            chart1.Series["Series1"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            chart1.Series["Series2"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;

            chart1.ChartAreas[0].CursorX.AutoScroll = false;
            chart1.ChartAreas[0].CursorX.IsUserSelectionEnabled = true;
            chart1.ChartAreas[0].AxisX.IsMarginVisible = false;
            chart1.ChartAreas[0].AxisX.ScrollBar.Enabled = false;


            chart2.Series["Series1"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            chart2.Series["Series2"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;

            chart2.Series["Series3"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Column;

            chart2.Series["Series3"]["PixelPointWidth"] = "1";

            chart2.ChartAreas[0].CursorX.AutoScroll = false;
            chart2.ChartAreas[0].CursorX.IsUserSelectionEnabled = true;
            chart2.ChartAreas[0].AxisX.IsMarginVisible = false;
            chart2.ChartAreas[0].AxisX.ScrollBar.Enabled = false;

            chart3.Series["Series1"].IsValueShownAsLabel = false;

            //chart3.ChartAreas[0].AxisY.Minimum = -20.5;
            //chart3.ChartAreas[0].AxisY.Maximum = -19.5;


            LoadConfig();

            try
            {
                dataLowerFrequency = uint.Parse(textBox1.Text);
                dataUpperFrequency = uint.Parse(textBox2.Text);
                stepSize = uint.Parse(textBox3.Text);
                difThreshold = double.Parse(textBox4.Text);
            }
            catch (Exception)
            {
                dataLowerFrequency = 87000000;
                dataUpperFrequency = 108000000;
                stepSize = 100;
                difThreshold = 10;

                textBox1.Text = dataLowerFrequency.ToString();
                textBox2.Text = dataUpperFrequency.ToString();
                textBox3.Text = stepSize.ToString();
                textBox4.Text = difThreshold.ToString();
            }

            pictureBox1.Image = new Bitmap(pictureBox1.Width, pictureBox1.Height);
            pictureBox2.Image = new Bitmap(pictureBox2.Width, pictureBox2.Height);

            waterFall = new Waterfall(pictureBox1);
            waterFallAvg = new Waterfall(pictureBox2);

            waterFall.SetStrengthRange(double.Parse(textBox9.Text), double.Parse(textBox10.Text));
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton3.Checked)
            {
                waterFall.SetMode(WaterFallMode.Strength);
                waterFallAvg.SetMode(WaterFallMode.Strength);

                textBox9.Text = waterFallAvg.GetStrengthMinimum().ToString();
                textBox9.Enabled = true;
                textBox10.Text = waterFallAvg.GetStrengthMaximum().ToString();
            }            
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton4.Checked)
            {
                waterFall.SetMode(WaterFallMode.Difference);
                waterFallAvg.SetMode(WaterFallMode.Difference);
                
                textBox9.Text = "0";
                textBox9.Enabled = false;
                textBox10.Text = waterFallAvg.GetNearStrengthDeltaRange().ToString();
            }            
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (this.checkBox2.Checked)
            {
                waterFall.SetRangeMode(WaterFallRangeMode.Auto);
                waterFallAvg.SetRangeMode(WaterFallRangeMode.Auto);
            }
            else
            {
                waterFall.SetRangeMode(WaterFallRangeMode.Fixed);
                waterFallAvg.SetRangeMode(WaterFallRangeMode.Fixed);
            }
        }

        private void radioButton6_CheckedChanged(object sender, EventArgs e)
        {
            //NativeMethods.SetUseDB(radioButton6.Checked);
            NativeMethods.SetUseDB(radioButton6.Checked ? 1 : 0);

            ClearSeries1();
            ////Thread.Sleep(1000);
            ClearSeries2();
        }

        private void radioButton7_CheckedChanged(object sender, EventArgs e)
        {
            ////NativeMethods.SetUseDB(!radioButton7.Checked);
            ////NativeMethods.SetUseDB();

            //ClearSeries1();
            //ClearSeries2();
        }

        private void button14_Click(object sender, EventArgs e)
        {
            leaderBoardSignals.Clear();
            listBox1.Items.Clear();

            currentLeaderBoardSignalIndex = -1;

            button16.Text = "Analyze Leader Board Frequencies";

            textBox1.Text = originalStartFrequency;

            textBox2.Text = originalEndFrequency;

            analyzingLeaderBoardSignals = false;

            checkBox4.Checked = false;

            button4.PerformClick();
        }
            

        private void button15_Click(object sender, EventArgs e)
        {
            if (form2 == null)
                form2 = new Form2();

            form2.Show();

            form2.Focus();
        }

        private void textBox11_TextChanged(object sender, EventArgs e)
        {
            evaluatedFrequencyString = textBox11.Text.ToLower();
        }

        private void NewSettingsThread(Object myObject, EventArgs myEventArgs)
        {
            if (eventTimer != null)
            {
                //eventTimer = (System.Windows.Forms.Timer)myObject;
                eventTimer.Stop();

                eventTimer.Interval = 10000000;

                eventTimer.Dispose();

                eventTimer = null;

                button4.PerformClick();

                //eventTimer.Enabled = false;


                //eventTimer.Dispose();

                /*eventTimer.Tick += new EventHandler(StartRecordingThread);
                eventTimer.Interval = 1000;
                eventTimer.Start();*/

            //button3.PerformClick();
        }
    }

        /*private void StartRecordingThread(Object myObject, EventArgs myEventArgs)
        {
            button4.PerformClick();

            eventTimer = new System.Windows.Forms.Timer();

            eventTimer.Tick += new EventHandler(StartRecordingThread);
            eventTimer.Interval = 1000;
            eventTimer.Start();
        }*/

        private void StopRecordingThread()
        {
            //Some process
            this.Invoke(new Action(() =>
            {
                //button1.PerformClick();
                if (button3.Text == "Stop Recording")
                    button3.PerformClick();
                else
                if (button5.Text == "Stop Recording")
                    button5.PerformClick();


                eventTimer.Tick += new EventHandler(NewSettingsThread);
                eventTimer.Interval = 1000;
                ////eventTimer.AutoReset = false;
                eventTimer.Start();

                //System.Threading.Thread th = new System.Threading.Thread(NewSettingsThread);
                //th.Start();

                /*while (!button4.Enabled)
                    Thread.Sleep(1000);

                button4.PerformClick();*/
            }));
        }

        private void button16_Click(object sender, EventArgs e)
        {
            if (leaderBoardSignals.Count > 0)
            {
                currentLeaderBoardSignalIndex++;

                if (currentLeaderBoardSignalIndex == 4)
                {
                    button16.Text = "Analyze Leader Board Frequencies";

                    currentLeaderBoardSignalIndex = -1;

                    analyzingLeaderBoardSignals = false;

                    checkBox4.Checked = false;

                    textBox1.Text = originalStartFrequency;

                    textBox2.Text = originalEndFrequency;

                    button4.PerformClick();
                }
                else
                {
                    button16.Text = "Next";

                    analyzingLeaderBoardSignals = true;

                    checkBox4.Checked = true;


                    if (currentLeaderBoardSignalIndex == 0)
                    {
                        originalStartFrequency = textBox1.Text;

                        originalEndFrequency = textBox2.Text;
                    }

                    textBox1.Text = (Math.Round(leaderBoardSignals[currentLeaderBoardSignalIndex].frequency) - 400000).ToString();

                    textBox2.Text = (Math.Round(leaderBoardSignals[currentLeaderBoardSignalIndex].frequency) + 600000).ToString();

                    textBox11.Text = GetFrequencyString(leaderBoardSignals[currentLeaderBoardSignalIndex].frequency);



                    //System.Threading.Thread th = new System.Threading.Thread(StopRecordingThread);
                    //th.Start();




                    if (button3.Text == "Stop Recording")
                        button3.PerformClick();
                    else
                        if (button5.Text == "Stop Recording")
                        button5.PerformClick();

                    eventTimer = new System.Windows.Forms.Timer();

                    eventTimer.Tick += new EventHandler(NewSettingsThread);
                    eventTimer.Interval = 1000;
                    ////eventTimer.AutoReset = false;
                    eventTimer.Start();

                    /*Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            this.Invoke(new Action(() =>
                            {

                        currentLeaderBoardSignalIndex++;

                        /*textBox1.Text = (Math.Round(leaderBoardSignals[currentLeaderBoardSignalIndex].frequency) - 500000).ToString();

                        textBox2.Text = (Math.Round(leaderBoardSignals[currentLeaderBoardSignalIndex].frequency)+500000).ToString();
                        */
                    /*  if (button3.Text == "Stop Recording")
                          button3.PerformClick();
                      else
                      if (button5.Text == "Stop Recording")
                          button5.PerformClick();

                              Task.Factory.StartNew(() =>
                              {
                                  try
                                  {
                                      this.Invoke(new Action(() =>
                                      {


                                          //Thread.Sleep(10000);

                                          while (!button4.Enabled);
                                              //Thread.Sleep(1000);

                                          button4.PerformClick();
                                          }));
                      }
                      catch (Exception ex)
                      {

                      }
                  });
                              /*
                                          //Thread.Sleep(10000);

                                          while (!button3.Enabled)
                                              Thread.Sleep(1000);

                                          button3.PerformClick();
                                          */

                    /* }));
        }
        catch (Exception ex)
        {

        }
        });
        */
                }
            }
        }

        private void button17_Click(object sender, EventArgs e)
        {
            button3.PerformClick();
        }

        private void button18_Click(object sender, EventArgs e)
        {
            button5.PerformClick();
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton5.Checked)
            {
                waterFall.SetMode(WaterFallMode.Off);
                waterFallAvg.SetMode(WaterFallMode.Off);
            }            

        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                prevWaterFallMinimum = waterFallAvg.GetStrengthMinimum();
                prevWaterFallMaximum = waterFallAvg.GetStrengthMaximum();
                prevNearStrengthDeltaRange = waterFallAvg.GetNearStrengthDeltaRange();

                waterFall.SetRangeMode(WaterFallRangeMode.Auto);
                waterFallAvg.SetRangeMode(WaterFallRangeMode.Auto);
            }
            else
            {
                waterFall.SetRangeMode(WaterFallRangeMode.Fixed);
                waterFallAvg.SetRangeMode(WaterFallRangeMode.Fixed);

                waterFall.SetStrengthRange(prevWaterFallMinimum, prevWaterFallMaximum);
                waterFall.SetNearStrengthDeltaRange(prevNearStrengthDeltaRange);

                waterFallAvg.SetStrengthRange(prevWaterFallMinimum, prevWaterFallMaximum);
                waterFallAvg.SetNearStrengthDeltaRange(prevNearStrengthDeltaRange);


                if (waterFallAvg.GetMode() == WaterFallMode.Difference)
                {
                    textBox9.Text = "0";
                    textBox10.Text = Math.Round(prevNearStrengthDeltaRange, 2).ToString();
                }
                else
                {
                    textBox9.Text = Math.Round(prevWaterFallMinimum, 2).ToString();
                    textBox10.Text = Math.Round(prevWaterFallMaximum, 2).ToString();
                }
            }
        }

        private void chart3_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void textBox9_TextChanged(object sender, EventArgs e)
        {
            
        }

        private void textBox10_TextChanged(object sender, EventArgs e)
        {

        }

        private void button13_Click(object sender, EventArgs e)
        {
            if (waterFall.GetMode() == WaterFallMode.Strength)
            {
                waterFall.SetStrengthRange(double.Parse(textBox9.Text), double.Parse(textBox10.Text));
                waterFallAvg.SetStrengthRange(double.Parse(textBox9.Text), double.Parse(textBox10.Text));
            }
            else
            {
                waterFall.SetNearStrengthDeltaRange(double.Parse(textBox10.Text));
                waterFallAvg.SetNearStrengthDeltaRange(double.Parse(textBox10.Text));
            }
        }

    }

    public class NoiseFloor
    {
        static public int NoiseFloorRangeAroundSignal = 100;
    }
}


