/*
* Author: Clint Mclean
*
* RTLSDRDevice is a DLL for doing frequency scans using an RTL2832 based DVB dongle
* Code uses the librtlsdr library: https://github.com/steve-m/librtlsdr
* and code based on the included rtl_power.c to get frequency strength readings
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


#ifdef RTLSDRDEVICED_EXPORTS
#define RTLSDRDEVICE_API __declspec(dllexport) 
#else
#define RTLSDRDEVICE_API __declspec(dllimport) 
#endif	

	RTLSDRDEVICE_API void Initialize(unsigned int startFrequency, unsigned int endFrequency, unsigned int stepSize);    
	RTLSDRDEVICE_API unsigned int GetBufferSize();    	
	RTLSDRDEVICE_API void GetBins(float* binsArray);
	RTLSDRDEVICE_API int GetTotalMagnitude();
	RTLSDRDEVICE_API int SetUseDB(int value);
