/*
 * This file is part of LapRFCSharpDemo.
 *
 * LapRFCSharpDemo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * LapRFCSharpDemo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with LapRFCSharpDemo.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.IO.Ports;
using System.Management;
using System.Collections.Generic;
using System.Windows.Forms;

//-----------------------------------------------------------------------------------------------------------------------------------------------------------
public class USBCDCInterface : IDisposable
{
	public SerialPort comPort;
	private string m_comPortPnpName;
	int iConnectRefCount = 0;

	//-----------------------------------------------------------------------------------------------------------
	// version to use when simply enumerating installed ports
	public USBCDCInterface()
	{
	}

	//-----------------------------------------------------------------------------------------------------------
	public USBCDCInterface(string comPortPnpName)
	{
		m_comPortPnpName = comPortPnpName;
		comPort = new SerialPort();
		comPort.BaudRate = 115200;
		comPort.RtsEnable = true;
		comPort.DtrEnable = true;
		//comPort.NewLine = "["; // EOR= 0x5B
	}

	//-----------------------------------------------------------------------------------------------------------
	public List<string> EnumerateCDCPorts()
	{
		List<string> comPortList = new List<string>();

		try
		{
			// Query the device list trough the WMI. If you want to get
			// all the properties listen in the MSDN article mentioned
			// below, use "select * from Win32_PnPEntity" instead!
			ManagementObjectSearcher deviceList = new ManagementObjectSearcher("Select Name, Status, DeviceID, ConfigManagerErrorCode from Win32_PnPEntity");
			ManagementObjectCollection deviceCollection = deviceList.Get();

			// Any results? There should be!
			if (deviceList != null)
			{
				// Enumerate the devices
				foreach (ManagementObject device in deviceCollection)
				{
					// To make the example more simple,
					try
					{
						string name = device.GetPropertyValue("Name").ToString();
						string status = device.GetPropertyValue("Status").ToString();
						string deviceID = device.GetPropertyValue("DeviceID").ToString();
						string errorState = device.GetPropertyValue("ConfigManagerErrorCode").ToString();

						try
						{
							if (errorState == "0")  // ensure that the device is working properly
							{
								if (name.Contains("(COM") && deviceID.Substring(0, 3) == "USB")
								{
									comPortList.Add(name);
								}
							}
						}
						catch
						{
						}
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
		return comPortList;
	}

	//-----------------------------------------------------------------------------------------------------------
	public string ExtractComPortNameFromPnpName()
	{
		// extract 'COMx' from 'Blab Blah (COMx)'
		int iStart = m_comPortPnpName.IndexOf("(COM");
		int iEnd = m_comPortPnpName.IndexOf(")", iStart);

		return m_comPortPnpName.Substring(iStart + 1, (iEnd - iStart - 1));
	}

	//-----------------------------------------------------------------------------------------------------------------------
	public void FlushComBuffer()
	{
		Console.WriteLine("FlushComBuffer: ");

		// ugly, wait for the PIC to do its thing... we don't have any other way to handshake, so this is 
		// actually not so crazy
		System.Threading.Thread.Sleep(200);

		string dumpString;
		if (comPort.IsOpen)
		{
			if (comPort.BytesToRead > 0)
			{
				dumpString = comPort.ReadExisting();

				Console.WriteLine("dumpString: " + dumpString);
			}
		}
	}

	//-----------------------------------------------------------------------------------------------------------------------
	public Boolean ConnectToDevice(int timeoutMs = 6000, bool skipFlush = false)
	{
		if (iConnectRefCount > 0)
		{
			++iConnectRefCount;

			// we do need to reset the timeouts in this case, since the second connection may expect different timeouts
			comPort.ReadTimeout = timeoutMs;
			comPort.WriteTimeout = timeoutMs * 2;
			return true;
		}

		try
		{
			ClosePort();

			comPort.PortName = ExtractComPortNameFromPnpName();
			comPort.ReadTimeout = timeoutMs;
			comPort.WriteTimeout = timeoutMs * 2;
			comPort.Open();

			if (comPort.IsOpen)
			{
				// flush any previous junk, left in the buffer
				if (!skipFlush)
					FlushComBuffer();

				++iConnectRefCount;         // successful connection
				return true;
			}
		}
		catch
		{
		}

		return false;
	}

	//-----------------------------------------------------------------------------------------------------------------------
	public void ClosePort(bool bForce = false)
	{
		if (iConnectRefCount > 0)
		{
			if (--iConnectRefCount == 0 || bForce)
			{
				try
				{
					if (comPort.IsOpen)
						comPort.Close();
				}
				catch
				{
				}
			}
		}
	}

	//-----------------------------------------------------------------------------------------------------------------------
	public void Dispose()
	{
		ClosePort();
	}

	//---------------------------------------------------------------------------------------------------------------------------------
	public bool SendBytes(byte[] buffer)
	{
		bool success = false;

		try
		{
			if (ConnectToDevice(1000))        // skip version check, 2 second timeout
			{
				comPort.Write(buffer, 0, buffer.Length);
				success = true;
			}
		}
		catch
		{
		}

		ClosePort();


		return success;
	}

	//---------------------------------------------------------------------------------------------------------------------------------
	public int ReceivedBytes(byte[] buffer)
	{
		int byte_received = 0;
		try
		{
			if (ConnectToDevice(1000))        // skip version check, 2 second timeout
			{
				while (byte_received < buffer.Length)
				{
					buffer[byte_received] = (byte)comPort.ReadByte();
					byte_received++;
				}
			}
		}
		catch
		{
		}

		ClosePort();

		return byte_received;
	}
}
