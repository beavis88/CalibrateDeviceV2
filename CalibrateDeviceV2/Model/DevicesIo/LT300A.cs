using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidLibrary;
using Handshake = System.IO.Ports.Handshake;
using Parity = System.IO.Ports.Parity;
using StopBits = System.IO.Ports.StopBits;

namespace CalibrateDevice.DeviceIo
{
	/// <summary>Термометр ЛТ-300А через USB-переходник</summary>
	public class LT300A : IDisposable
	{
		private SerialPort _serialPort;

		public Task OpenAsync(string portName, CancellationToken cancellationToken)
		{
			Dispose();
			_serialPort = new SerialPort(portName, 4800, Parity.None, 8, StopBits.One);
			_serialPort.ReadTimeout = 500;
			_serialPort.DtrEnable = true;
			_serialPort.RtsEnable = false;
			_serialPort.Handshake = Handshake.None;

			return _serialPort.OpenAsync(cancellationToken);
		}

		public void Dispose()
		{
			_serialPort?.Close();
			_serialPort?.Dispose();
		}

		/// <summary>Прочитать температуру по COM-порту</summary>
		/// <returns>Возвращает температуру</returns>
		public float GetTemperatureCom()
		{
			try
			{
				_serialPort.DiscardInBuffer();
				_serialPort.DiscardOutBuffer();

				// Отправляем запрос на температуру
				_serialPort.Write(new byte[] { 0x64, 0x0D }, 0, 2);

				// Читаем ответ
				var response = new List<byte>();
				var dtStopRead = DateTime.Now.AddSeconds(2);

				int readenByte;
				while ((readenByte = _serialPort.ReadByte()) != 0x0D)
				{
					if (readenByte != -1)
						response.Add((byte)readenByte);

					if (DateTime.Now > dtStopRead)
						throw new TimeoutException("[LT300A] timeout error");
				}

				// Удаляем лишние символы и сдвоенные пробелы
				string resultString = System.Text.Encoding.ASCII.GetString(response.ToArray()).Trim();

				float r, t;
				ParseData(resultString, out r, out t);
				return t;
			}
			catch (Exception ex)
			{
				Trace.TraceError("[LT300A] " + ex.Message);
				throw;
			}
		}
	

		/// <summary>Прочитать температуру по USB</summary>
		/// <returns>Возвращает температуру</returns>
		public float GetTemperatureUsb()
		{
			var device = HidDevices.Enumerate(0xFFFF, 0x0002).FirstOrDefault();

			if (device != null)
				try
				{
					device.OpenDevice();

					device.Inserted += DeviceOnInserted;
					device.Removed += DeviceOnRemoved;

					device.MonitorDeviceEvents = true;

					var request = new byte[64];
					request[0] = 2;
					request[4] = 0x64;
					request[5] = 0x0D;

					var report = device.CreateReport();
					report.Data = request;

					if (device.WriteReport(report))
					{
						var readenReport = device.ReadReport(1000);
						string resultString = System.Text.Encoding.ASCII.GetString(readenReport.Data, 4, readenReport.Data[0]);

						float r, t;
						ParseData(resultString, out r, out t);
						return t;
					}
				}
				finally
				{
					device.CloseDevice();
				}
			else
				Trace.TraceError("[LT300A] Can't open device");

			throw new Exception("[LT300A] IO Error");
		}

		private void ParseData(string resultString, out float r, out float t)
		{
			var splitResultString = resultString.Trim().Split(' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
			if (splitResultString.Length > 1)
			{
				Trace.Write($"[LT300A] Temperature string: {splitResultString[1]}");

				if (!float.TryParse(splitResultString[0], NumberStyles.Number, CultureInfo.InvariantCulture, out r) ||
					!float.TryParse(splitResultString[1], NumberStyles.Number, CultureInfo.InvariantCulture, out t) ||

					// Формат вывода температуры printf("%6.2f"), поэтому проверяем наличие точки и еще двух символов после неё чтобы отсечь приходящие иногда непонятные значения
					!splitResultString[1].Contains('.') || splitResultString[1].Split('.')[1].Length != 2)
					throw new IOException("[LT300A] IO Error");
			}
			else
				throw new IOException($"[LT300A] Error string result: {resultString}");
		}

		private void DeviceOnRemoved()
		{
		}

		private void DeviceOnInserted()
		{
		}
	}
}
