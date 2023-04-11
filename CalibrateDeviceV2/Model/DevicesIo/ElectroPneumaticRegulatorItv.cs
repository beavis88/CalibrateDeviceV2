using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CalibrateDevice.DeviceIo
{
	/// <summary>Electro-Pneumatic Regulator for RS232C (ITV1050-RCF2N)</summary>
	public class ElectroPneumaticRegulatorItv : IDisposable
	{
		private SerialPort _serialPort;
		private const string NewLine = "\r\n";
		private const int InputDataMax = 1023;

		/// <summary>Имя COM-порта</summary>
		public string PortName { get; set; }

		/// <summary>Полный диапазон давления, МПа</summary>
		public double PressureRange { get; set; } = 0.9;

		/// <summary>
		/// Таймаут ввода/вывода, мс
		/// </summary>
		public int Timeout { get; set; } = 1000;

		#region Methods

		public Task OpenAsync(CancellationToken cancellationToken = default)
		{
#if EMUL_DEVICES
			return Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
#endif
			_serialPort?.Close();
			_serialPort?.Dispose();
			_serialPort = new SerialPort(PortName, 9600, Parity.None, 8, StopBits.One);
			_serialPort.ReadTimeout = _serialPort.WriteTimeout = Timeout;
			return _serialPort.OpenAsync(cancellationToken);
		}

		/// <summary>Стравить выходное давление (прибор перестанет поддерживать давление)</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task BleedOutputPressureAsync(CancellationToken cancellationToken = default)
		{
#if EMUL_DEVICES
			await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
			return;
#endif
			await SetOutputPressureAsync(0, cancellationToken);
		}

		/// <summary>Установить значение выходного давления</summary>
		/// <param name="outputPressure">Выходное давление, МПа</param>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task SetOutputPressureAsync(double outputPressure, CancellationToken cancellationToken = default)
		{
#if EMUL_DEVICES
			await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
			return;
#endif

			if (outputPressure < 0)
				throw new ArgumentOutOfRangeException($"Output pressure {outputPressure} < 0");

			var setData = (int) (outputPressure / PressureRange * InputDataMax);
			if (setData > InputDataMax)
				throw new ArgumentOutOfRangeException($"Set data {setData} > {InputDataMax}");

			await WriteStringAsync($"{SetCommand} {setData}");
			var result = await ReadStringAsync(cancellationToken);
			System.Diagnostics.Trace.TraceInformation($"ITV SET pressure {result}");
		}

		public async Task<double> GetPressureAsync(CancellationToken cancellationToken = default)
		{
#if EMUL_DEVICES
			await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
			return 0.8;
#endif

			await WriteStringAsync(MonCommand);
			var result = await ReadStringAsync(cancellationToken);
			var setData = Convert.ToInt32(result);
			var outputPressure = setData * PressureRange / InputDataMax;
			System.Diagnostics.Trace.TraceInformation($"ITV output pressure: {outputPressure}");
			return outputPressure;
		}

		public async Task ConfirmOutputPressureAsync(CancellationToken cancellationToken = default)
		{
#if EMUL_DEVICES
			await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
			return;
#endif

			await WriteStringAsync(ReqCommand);
			var result = await ReadStringAsync(cancellationToken);
			System.Diagnostics.Trace.TraceInformation($"ITV Confirmed output pressure: {result}");
		}

		public async Task WriteStringAsync(string str)
		{
			var bytes = Encoding.ASCII.GetBytes($"{str}{NewLine}");
			await _serialPort.WriteAsync(bytes, 0, bytes.Length);
		}

		public Task<string> ReadStringAsync(CancellationToken cancellationToken = default)
		{
			return Task.Run(() => _serialPort.ReadLine(), cancellationToken);
		}

#endregion Methods

#region Commands

		/// <summary>Setting of output pressure</summary>
		private const string SetCommand = "SET";
		/// <summary>Increase setting of output pressure</summary>
		private const string IncCommand= "INC";
		/// <summary>Decrease setting of output pressure</summary>
		private const string DecCommand= "DEC";
		/// <summary>Confirmation of setting data</summary>
		private const string ReqCommand= "REQ";
		/// <summary>Requirement of output pressure data</summary>
		private const string MonCommand= "MON";

#endregion Commands

		private void ReleaseUnmanagedResources()
		{
#if EMUL_DEVICES
			return;
#endif
			_serialPort.Close();
			_serialPort?.Dispose();
		}

		public void Dispose()
		{
			ReleaseUnmanagedResources();
			GC.SuppressFinalize(this);
		}

		~ElectroPneumaticRegulatorItv()
		{
			ReleaseUnmanagedResources();
		}
	}
}
