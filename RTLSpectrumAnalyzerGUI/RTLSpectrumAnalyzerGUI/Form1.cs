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
        const long MAXIMUM_GRAPH_BIN_COUNT = 100000;
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

        double[] difBinArray = null;

        bool resetGraph = true;

        bool newData = false;

        class BinData
        {
            public string dataSeries;

            public double[] binArray = null;
            public double[] avgBinArray = null;
            public double[] totalBinArray = null;

            public uint numberOfFrames = 0;
            public uint size = 0;

            public BinData(uint size, string series)
            {
                this.size = size;

                dataSeries = series;

                totalBinArray = new double[size];
                avgBinArray = new double[size];
                binArray = new double[size];
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

        private void RangeChanged(System.Windows.Forms.DataVisualization.Charting.Chart chart, string dataSeries, double[] data, long lowerIndex, long upperIndex, double newLowerFrequency, ref double graphBinFreqInc)
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

                System.Windows.Forms.DataVisualization.Charting.DataPoint graphPoint;
                for (int i = 0; i < lowerResGraphBinCount; i++)
                {
                    value = data[(long)index];

                    if (Double.IsNaN(value) || value > 100 || value < -100)
                    {
                        value = -25;
                    }
                    else
                    {
                        if (i < chart.Series[dataSeries].Points.Count)
                        {
                            graphPoint = chart.Series[dataSeries].Points.ElementAt(i);
                            graphPoint.SetValueXY(i, value);                            
                            graphPoint.AxisLabel = (Math.Round((binFrequency / 1000000), 4)).ToString() + "MHz";                            
                        }
                        else
                        {
                            graphPoint = new System.Windows.Forms.DataVisualization.Charting.DataPoint(i, value);                            
                            graphPoint.AxisLabel = (Math.Round((binFrequency / 1000000), 4)).ToString() + "MHz";                            
                            chart.Series[dataSeries].Points.Add(graphPoint);                            
                        }
                    }
                    

                    index += inc;

                    binFrequency += graphBinFreqInc;
                }
                
                chart.Refresh();
                chart.ChartAreas[0].AxisX.ScaleView.Zoom(1, lowerResGraphBinCount-1);
            }
        }

        private void AxisViewChanged(System.Windows.Forms.DataVisualization.Charting.Chart chart, string dataSeries, double[] data, ref double lowerFrequency, ref double upperFrequency, ref double graphBinFreqInc)
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
            }
        }

        private void chart1_AxisViewChanged(object sender, System.Windows.Forms.DataVisualization.Charting.ViewEventArgs e)
        {
            FrequencyRange frequencyRange = new FrequencyRange(graph1LowerFrequency, graph1UpperFrequency);
            graph1FrequencyRanges.Push(frequencyRange);

            AxisViewChanged(chart1, series1BinData.dataSeries, series1BinData.binArray, ref graph1LowerFrequency, ref graph1UpperFrequency, ref graph1BinFreqInc);
            AxisViewChanged(chart1, series2BinData.dataSeries, series2BinData.binArray, ref graph1LowerFrequency, ref graph1UpperFrequency, ref graph1BinFreqInc);
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

                RangeChanged(chart1, series1BinData.dataSeries, series1BinData.binArray, lowerIndex, upperIndex, graph1LowerFrequency, ref graph1BinFreqInc);
                RangeChanged(chart1, series2BinData.dataSeries, series2BinData.binArray, lowerIndex, upperIndex, graph1LowerFrequency, ref graph1BinFreqInc);

                GraphDifference(series1BinData, series2BinData);
            }
        }

        private void chart2_AxisViewChanged(object sender, System.Windows.Forms.DataVisualization.Charting.ViewEventArgs e)
        {
            FrequencyRange frequencyRange = new FrequencyRange(graph2LowerFrequency, graph2UpperFrequency);
            graph2FrequencyRanges.Push(frequencyRange);

            AxisViewChanged(chart2, series1BinData.dataSeries, series1BinData.avgBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);
            AxisViewChanged(chart2, series2BinData.dataSeries, series2BinData.avgBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);
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

                RangeChanged(chart2, series1BinData.dataSeries, series1BinData.avgBinArray, lowerIndex, upperIndex, graph2LowerFrequency, ref graph2BinFreqInc);
                RangeChanged(chart2, series2BinData.dataSeries, series2BinData.avgBinArray, lowerIndex, upperIndex, graph2LowerFrequency, ref graph2BinFreqInc);

                GraphDifference(series1BinData, series2BinData);
            }
        }

        private void RecordData(ref BinData binData)
        {
            if (binData.binArray.Length==0)
                binData = new BinData(totalBinCount, binData.dataSeries);                        

            double value;
            
            NativeMethods.GetBins(binData.binArray);

            for (int j = 0; j < binData.size; j++)
            {
                value = binData.binArray[j];

                if (Double.IsNaN(value) || value > 100 || value < -100)
                    value = -25;

                binData.totalBinArray[j] += value;
            }

            binData.numberOfFrames++;            

            for (int j = 0; j < binData.size; j++)
            {
                binData.avgBinArray[j] = binData.totalBinArray[j] / binData.numberOfFrames;
            }

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
                difBinArray = new double[totalBinCount];

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

                double dif;
                for (int i = 0; i < totalBinCount; i++)
                {
                    dif = series2BinData.avgBinArray[i] - series1BinData.avgBinArray[i];

                    if (!checkBox1.Checked || dif >= difThreshold)
                        difBinArray[i] = dif;
                    else
                        difBinArray[i] = -9999;
                }

                AxisViewChanged(chart2, "Series3", difBinArray, ref graph2LowerFrequency, ref graph2UpperFrequency, ref graph2BinFreqInc);                
                chart2.Refresh();
            }
        }


        

        

        private void button3_Click(object sender, EventArgs e)
        {
            if (button3.Text == "Record Series 1 Data (Far)")
            {
                Task.Factory.StartNew(() =>
                {
                    recordingSeries1 = true;
                    while (recordingSeries1)
                    {                        
                        RecordData(ref series1BinData);

                        try
                        {
                            this.Invoke(new Action(() =>
                            {
                                GraphData(series1BinData);
                                GraphDifference(series1BinData, series2BinData);
                            }));
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                });

                button4.Enabled = false;
                button5.Enabled = false;
                button3.Text = "Stop Recording";
            }
            else
            {
                button4.Enabled = true;
                button5.Enabled = true;
                button3.Text = "Record Series 1 Data (Far)";

                recordingSeries1 = false;
            }            
        }

        private void button5_Click(object sender, EventArgs e)
        {
            if (button5.Text == "Record Series 2 Data (Near)")
            {
                Task.Factory.StartNew(() =>
                {
                    recordingSeries2 = true;                    

                    while (recordingSeries2)
                    {
                        RecordData(ref series2BinData);

                        try
                        {
                            this.Invoke(new Action(() =>
                            {
                                GraphData(series2BinData);
                                GraphDifference(series1BinData, series2BinData);
                            }));
                        }
                        catch(Exception ex)
                        {

                        }
                    }
                });

                button3.Enabled = false;
                button4.Enabled = false;
                button5.Text = "Stop Recording";
            }
            else
            {
                button4.Enabled = true;
                button5.Text = "Record Series 2 Data (Near)";

                button3.Enabled = true;
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

            tw.WriteLine(dataLowerFrequency);
            tw.WriteLine(dataUpperFrequency);
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

                if (dataUpperFrequency <= dataLowerFrequency)
                    MessageBox.Show("End frequency must be greater than start frequency");
                else
                {
                    stepSize = uint.Parse(textBox3.Text);

                    difThreshold = double.Parse(textBox4.Text);

                    SaveConfig();

                    int result = NativeMethods.Initialize(dataLowerFrequency, dataUpperFrequency, stepSize);

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

                        if (series1BinData == null)
                            series1BinData = new BinData(0, "Series1");

                        if (series2BinData == null)
                            series2BinData = new BinData(0, "Series2");

                        button3.Enabled = true;
                        button5.Enabled = true;

                        resetGraph = true;
                    }
                }
            }
            catch(Exception )
            {
                MessageBox.Show("Invalid Values");
            }
        }


        public Form1()
        {
            InitializeComponent();        

            chart1.Series["Series1"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            chart1.Series["Series2"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;

            chart1.ChartAreas[0].CursorX.IsUserSelectionEnabled = true;
            chart1.ChartAreas[0].AxisX.IsMarginVisible = false;
            chart1.ChartAreas[0].AxisX.ScrollBar.Enabled = false;


            chart2.Series["Series1"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            chart2.Series["Series2"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            
            chart2.Series["Series3"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Column;

            chart2.Series["Series3"]["PixelPointWidth"] = "1";


            chart2.ChartAreas[0].CursorX.IsUserSelectionEnabled = true;
            chart2.ChartAreas[0].AxisX.IsMarginVisible = false;
            chart2.ChartAreas[0].AxisX.ScrollBar.Enabled = false;

            LoadConfig();

            try
            {
                dataLowerFrequency = uint.Parse(textBox1.Text);
                dataUpperFrequency = uint.Parse(textBox2.Text);
                stepSize = uint.Parse(textBox3.Text);
                difThreshold = double.Parse(textBox4.Text);
            }
            catch(Exception )
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
                        value = reader.ReadDouble();
                        
                        series.totalBinArray[i] = value;

                        value /= series.numberOfFrames;

                        series.binArray[i] = value;
                        series.avgBinArray[i] = value;
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
    }
}

