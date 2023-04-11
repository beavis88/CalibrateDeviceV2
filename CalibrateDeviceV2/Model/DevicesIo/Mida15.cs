using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CalibrateDevice.DeviceIo
{
	/*
	/// <summary>Датчик давления Мида 15</summary>
	public class Mida15 : IDisposable
	{
		#region Fields

		private Modbus.Device.ModbusMaster _modbusMaster;
		private readonly IStreamFactory _streamFactory;
		private Stream _stream;
		private TcpClient _tcpClient;

		#endregion Fields

		#region Properties

		/// <summary>Таймаут, мс</summary>
		public uint Timeout { get; set; } = 1000;

		/// <summary>Адрес подключенного slave-устройства</summary>
		public byte SlaveAddress { get; set; } = 1;

		#endregion Properties

		#region Mida15 protocol


		public async Task<double> ReadPressureAsync(CancellationToken cancellationToken)
		{
			// MUnit
			var mUnit = await ReadSingleHoldingRegisterAsync(7, cancellationToken);
			Formatter.UnitsListEnumerator unit;
			switch (mUnit & 0x000F)
			{
				case 0: //– Па
					unit = Formatter.UnitsListEnumerator.давление_Па;
					break;
				case 1: //– кПа
					unit = Formatter.UnitsListEnumerator.давление_кПа;
					break;
				case 2: //– МПа
					unit = Formatter.UnitsListEnumerator.давление_МПа;
					break;
				case 3: //– bar
					unit = Formatter.UnitsListEnumerator.давление_бар;
					break;
				case 4: //– psi
					unit = Formatter.UnitsListEnumerator.none;
					break;
				case 5: //– кгc / см2
					unit = Formatter.UnitsListEnumerator.none;
					break;
				case 6: //– мм.рт.ст.
					unit = Formatter.UnitsListEnumerator.давление_мм_рт_ст;
					break;
				case 7: //– % от диапазона
					unit = Formatter.UnitsListEnumerator.none;
					break;
				default:
					unit = Formatter.UnitsListEnumerator.none;
					break;
			}

			if(unit == Formatter.UnitsListEnumerator.none)
				throw new ArgumentException($"Measure unit {mUnit} not supported");

			// Pressure
			var pressureRegs = (await ReadInputRegistersAsync(2, 2, cancellationToken)).ToArray();
			var pressure = BitConverter.ToSingle(BitConverter.GetBytes(pressureRegs[0]).Concat(BitConverter.GetBytes(pressureRegs[1])).ToArray(), 0);
			
			return Formatter.ConvertFromUnit(pressure, unit);
		}

		#endregion Mida15 protocol

		public Mida15(IStreamFactory streamFactory = null)
		{
			_streamFactory = streamFactory;
		}
		
		public async Task OpenAsync(string connectionString, CancellationToken cancellationToken)
		{
			var transmissionMode = ModbusTransmissionMode.Ascii;

			var ps = new ParametersString(connectionString);
			string transmissionModeString;
			if (ps.GetParameterValue("ModbusTransport", out transmissionModeString))
			{
				switch (transmissionModeString.ToLower())
				{
					case "rtu":
						transmissionMode = ModbusTransmissionMode.Rtu;
						break;
					case "tcp":
						transmissionMode = ModbusTransmissionMode.Tcp;
						break;
				}
			}

			string timeoutString;
			if (ps.GetParameterValue("timeout", out timeoutString))
				Timeout = Convert.ToUInt32(timeoutString);

			string addressString;
			if (ps.GetParameterValue("ModbusAddress", out addressString))
				SlaveAddress = Convert.ToByte(addressString);

			if (transmissionMode == ModbusTransmissionMode.Tcp)
			{
				string hostName;
				if (!ps.GetParameterValue("hostname", out hostName))
					throw new FormatException("Hostname in URL required");

				string port;
				if (!ps.GetParameterValue("PortTcp", out port))
					throw new FormatException("TCP Port in URL required");

				try
				{
					_tcpClient = new TcpClient();
					cancellationToken.ThrowIfCancellationRequested();
					await _tcpClient.ConnectAsync(hostName, Convert.ToInt32(port));
					if (!_tcpClient.Connected)
						throw new OpenFailedException();

					_tcpClient.ReceiveTimeout = _tcpClient.SendTimeout = (int) Timeout;
				}
				catch (SocketException e)
				{
					throw new OpenFailedException(e.Message);
				}
			}
			else if (_streamFactory == null)
				throw new NotSupportedException("Stream factory for ModbusOpcServer is null");
			else
				await Task.Run(() => _stream = _streamFactory.GetStream(connectionString, cancellationToken), cancellationToken);

			_modbusMaster = transmissionMode == ModbusTransmissionMode.Tcp
				? (Modbus.Device.ModbusMaster)Modbus.Device.ModbusTcpMaster.CreateTcp(_tcpClient, cancellationToken)
				: transmissionMode == ModbusTransmissionMode.Rtu
					? Modbus.Device.ModbusStreamMaster.CreateRtu(_stream, cancellationToken)
					: Modbus.Device.ModbusStreamMaster.CreateAscii(_stream, cancellationToken);

			_modbusMaster.Transport.Timeout = Timeout;
		}

		public async Task<bool> ReadSingleCoilAsync(ushort startAddress, CancellationToken cancellationToken)
		{
			return await Task.Run(() => _modbusMaster.ReadSingleCoil(SlaveAddress, startAddress), cancellationToken);
		}

		public async Task<IEnumerable<bool>> ReadCoilsAsync(ushort startAddress, int count, CancellationToken cancellationToken)
		{
			var result = new bool[count];
			await Task.Run(() => _modbusMaster.ReadCoils(SlaveAddress, startAddress, result), cancellationToken);
			return result;
		}

		public async Task<bool> ReadSingleInputAsync(ushort startAddress, CancellationToken cancellationToken)
		{
			return await Task.Run(() => _modbusMaster.ReadSingleInput(SlaveAddress, startAddress), cancellationToken);
		}

		public async Task<IEnumerable<bool>> ReadInputsAsync(ushort startAddress, int count, CancellationToken cancellationToken)
		{
			var result = new bool[count];
			await Task.Run(() => _modbusMaster.ReadInputs(SlaveAddress, startAddress, result), cancellationToken);
			return result;
		}

		public async Task<ushort> ReadSingleHoldingRegisterAsync(ushort startAddress, CancellationToken cancellationToken)
		{
			return await Task.Run(() => _modbusMaster.ReadSingleHoldingRegister(SlaveAddress, startAddress), cancellationToken);
		}

		public async Task<IEnumerable<ushort>> ReadHoldingRegistersAsync(ushort startAddress, int count, CancellationToken cancellationToken)
		{
			var result = new ushort[count];
			await Task.Run(() => _modbusMaster.ReadHoldingRegisters(SlaveAddress, startAddress, result), cancellationToken);
			return result;
		}

		public async Task<ushort> ReadSingleInputRegisterAsync(ushort startAddress, CancellationToken cancellationToken)
		{
			return await Task.Run(() => _modbusMaster.ReadSingleInputRegister(SlaveAddress, startAddress), cancellationToken);
		}

		public async Task<IEnumerable<ushort>> ReadInputRegistersAsync(ushort startAddress, int count, CancellationToken cancellationToken)
		{
			var result = new ushort[count];
			await Task.Run(() => _modbusMaster.ReadInputRegisters(SlaveAddress, startAddress, result), cancellationToken);
			return result;
		}

		public async Task WriteSingleCoilAsync(ushort startAddress, bool value, CancellationToken cancellationToken)
		{
			await Task.Run(() => _modbusMaster.WriteSingleCoil(SlaveAddress, startAddress, value), cancellationToken);
		}

		public async Task WriteMultipleCoilsAsync(ushort startAddress, IEnumerable<bool> data, CancellationToken cancellationToken)
		{
			await Task.Run(() => _modbusMaster.WriteMultipleCoils(SlaveAddress, startAddress, data.ToArray()), cancellationToken);
		}

		public async Task WriteSingleRegisterAsync(ushort startAddress, ushort value, CancellationToken cancellationToken)
		{
			await Task.Run(() => _modbusMaster.WriteSingleRegister(SlaveAddress, startAddress, value), cancellationToken);
		}

		public async Task WriteMultipleRegistersAsync(ushort startAddress, IEnumerable<ushort> data, CancellationToken cancellationToken)
		{
			await Task.Run(() => _modbusMaster.WriteMultipleRegisters(SlaveAddress, startAddress, data.ToArray()), cancellationToken);
		}

		public void Dispose()
		{
			if (_stream != null)
			{
				_stream.Close();
				_stream = null;
				System.Diagnostics.Trace.TraceInformation("Stream client closed");
			}

			if (_tcpClient != null)
			{
				_tcpClient.Close();
				_tcpClient = null;
				System.Diagnostics.Trace.TraceInformation("TCP client closed");
			}
		}
	}
	
	/// <summary>Способы передачи modbus-пакетов</summary>
	public enum ModbusTransmissionMode { Rtu, Ascii, Tcp }
	*/
}
