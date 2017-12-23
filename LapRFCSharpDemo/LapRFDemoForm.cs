/*
 * A decoder/encoder for the ImmersionRC LapRF family of race timing systems
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
using System.IO;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Text;

namespace WindowsFormsApplication1
{
	//------------------------------------------------------------------------------------------------------------
	public partial class LapRFDemoForm : Form
	{
		static Socket sock;
		static USBCDCInterface usbcdc_itf;
		static LapRF.LapRFProtocol laprf;

		//------------------------------------------------------------------------------------------------------------
		public LapRFDemoForm()
		{
			laprf = new LapRF.LapRFProtocol();

			InitializeComponent();
		}

		//------------------------------------------------------------------------------------------------------------
		private void Form1_Load(object sender, EventArgs e)
		{
			RefreshComPortList();

			timer1.Tick += new EventHandler(TimerEventProcessor);

			// Sets the timer interval to 100ms
			timer1.Interval = 100;
			timer1.Start();
		}

		//-----------------------------------------------------------------------------------------------------------
		private void RefreshComPortList()
		{
			System.Collections.Generic.List<string> comPortList = new System.Collections.Generic.List<string>();

			USBCDCInterface cdcIf = new USBCDCInterface();

			comPortList = cdcIf.EnumerateCDCPorts();

			if (comPortList.Count == 0)
				comPortList.Add("No Device Found");

			bool bListsTheSame = true;
			if (comPortList.Count != comPortCombo.Items.Count)
				bListsTheSame = false;
			else
			{
				int i;
				for (i = 0; i < comPortList.Count; ++i)
				{
					if (comPortList[i] != comPortCombo.Items[i].ToString())
						bListsTheSame = false;
				}
			}

			if (!bListsTheSame)
			{
				comPortCombo.Items.Clear();
				int i;
				for (i = 0; i < comPortList.Count; ++i)
				{
					comPortCombo.Items.Add(comPortList[i]);
				}
				comPortCombo.SelectedIndex = 0;
			}
		}

		//------------------------------------------------------------------------------------------------------------
		// periodic timer to handle network reads (ugly, but functional, and with simple to read code)
		private void TimerEventProcessor(Object myObject,
												EventArgs myEventArgs)
		{
			timer1.Stop();

			// read a packet of data from the gate
			//
			if (sock != null && sock.Available > 0)
			{
				byte[] rxBuf = new byte[256];
				int numBytes = sock.Receive(rxBuf);
				if (numBytes > 0)
				{
					laprf.processBytes(rxBuf, numBytes);
				}
			}

			// update the progress bars with RSSI values
			//
			for (int idx = 1; idx <= 8; ++idx)
			{
				ProgressBar ctn = (ProgressBar)this.Controls["progressBar" + idx];
				ctn.Value = (int)laprf.getRssiPerSlot(idx).lastRssi;
			}

			// captured any passing records?
			//
			if (laprf.getPassingRecordCount() > 0)
			{
				PassingRecord nextRecord = laprf.getNextPassingRecord();
				passingRecordText.Text = String.Format("Passing: {0} {1} {2}", nextRecord.passingNumber, nextRecord.pilotId, nextRecord.rtcTime);
			}

			timer1.Enabled = true;
		}

		//------------------------------------------------------------------------------------------------------------
		public async Task UpdatePassingRecord()
		{
			if (usbcdc_itf.ConnectToDevice(10, true))
			{
				byte[] data = new byte[256];
				while (usbcdc_itf != null)
				{
					int read = await usbcdc_itf.comPort.BaseStream.ReadAsync(data, 0, 256).ConfigureAwait(true);
					if (read > 0)
					{
						laprf.processBytes(data, read);
					}
				}
			}
		}

		//------------------------------------------------------------------------------------------------------------
		// helper to retrieve value from a check box
		private UInt16 getCheckBoxValue(String controlName)
		{
			CheckBox textBox = (CheckBox)this.Controls[controlName];
			if (textBox.Checked)
				return 1;
			return 0;
		}

		//------------------------------------------------------------------------------------------------------------
		// helper to retrieve value from a text box with exception handling
		private UInt16 getTextValue(String controlName, UInt16 defaultValue)
		{
			UInt16 returnValue = defaultValue;
			TextBox textBox = (TextBox)this.Controls[controlName];
			try
			{
				returnValue = Convert.ToUInt16(textBox.Text);
			}
			catch
			{

			}

			return returnValue;
		}

		//------------------------------------------------------------------------------------------------------------
		// send the current settings (Enable, Freq, Gain, Atten) to the LapRF
		private void sendSettingToLapRF_Click(object sender, EventArgs e)
		{
			laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_RFSETUP);
			for (int i = 1; i <= 8; ++i)
			{
				UInt16 slotEnabled = getCheckBoxValue("enableCheck" + i);
				UInt16 freqValue = getTextValue("freqText" + i, 5800);
				UInt16 threshValue = getTextValue("thresholdText" + i, 800);
				UInt16 gainValue = getTextValue("gainText" + i, 59);

				laprf.append_field_of_record_u8(0x01, (Byte)i);                // pilot ID
				laprf.append_field_of_record_u16(0x20, (Byte)slotEnabled);      // Enable
				laprf.append_field_of_record_fl32(0x23, (Single)threshValue);   // Threshold
				laprf.append_field_of_record_u16(0x24, gainValue);              // Gain
				laprf.append_field_of_record_u16(0x25, freqValue);              // Frequency
			}
			MemoryStream dataStream = laprf.finalize_sendable_packet();
			byte[] pack = dataStream.ToArray();
			if (sock != null)
				sock.Send(pack);
			if (usbcdc_itf != null)
				usbcdc_itf.SendBytes(pack);
		}

		//------------------------------------------------------------------------------------------------------------
		// pre-configure all channels for RaceBand, 25mW threshold and gain settings
		private void raceband25mWButton_Click(object sender, EventArgs e)
		{
			int baseFreq = 5658;
			for (int i = 1; i <= 8; ++i)
			{
				((CheckBox)this.Controls["enableCheck" + i]).Checked = true;
				((TextBox)this.Controls["freqText" + i]).Text = baseFreq.ToString();
				((TextBox)this.Controls["thresholdText" + i]).Text = "800";
				((TextBox)this.Controls["gainText" + i]).Text = "59";

				baseFreq += 37;     // 37MHz channel spacing
			}
		}

		//------------------------------------------------------------------------------------------------------------
		// Connect to the LapRF at the specified IP address
		private async void connectButton_Click(object sender, EventArgs e)
		{
			if (USBSelected())
			{
				if (usbcdc_itf == null)
				{
					usbcdc_itf = new USBCDCInterface(comPortCombo.Text);
					await UpdatePassingRecord().ConfigureAwait(false);
				}
			}
			else
			{
				string host = ipAddressText.Text;
				int portNumber = 5403;                                  // standard port number for the LapRF
				IPAddress[] IPs = Dns.GetHostAddresses(host);

				sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				//asynchronous connect request
				IAsyncResult result = sock.BeginConnect(IPs[0], portNumber, null, null);
			}
		}

		//------------------------------------------------------------------------------------------------------------
		private void startRaceButton_Click(object sender, EventArgs e)
		{
			laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_STATE_CONTROL);
			laprf.append_field_of_record_u8(0x20, 1);                // 1 = start race
			byte[] pack = laprf.finalize_sendable_packet().ToArray();
			if (sock != null)
				sock.Send(pack);
			if (usbcdc_itf != null)
				usbcdc_itf.SendBytes(pack);
		}

		//------------------------------------------------------------------------------------------------------------
		private void stopRaceButton_Click(object sender, EventArgs e)
		{
			laprf.prepare_sendable_packet(LapRF.LapRFProtocol.laprf_type_of_record.LAPRF_TOR_STATE_CONTROL);
			laprf.append_field_of_record_u8(0x20, 0);                // 0 = stop race
			byte[] pack = laprf.finalize_sendable_packet().ToArray();
			if (sock != null)
				sock.Send(pack);
			if (usbcdc_itf != null)
				usbcdc_itf.SendBytes(pack);
		}

		//------------------------------------------------------------------------------------------------------------
		bool USBSelected()
		{
			return interfaceCombo.Text == "USB";
		}

		//------------------------------------------------------------------------------------------------------------
		private void interfaceCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (USBSelected())
			{
				label7.Hide();
				ipAddressText.Hide();
				label8.Show();
				comPortCombo.Show();
			}
			else
			{
				label7.Show();
				ipAddressText.Show();
				label8.Hide();
				comPortCombo.Hide();
			}
		}
	}
}
