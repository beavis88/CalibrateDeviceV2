using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Parity = System.IO.Ports.Parity;
using StopBits = System.IO.Ports.StopBits;
using Autograph.Navigator40.Core;

namespace CalibrateDevice.DeviceIo
{
	/// <summary>Манометр ДМ5002</summary>
	public class Dm5002 : IDm5002, IDisposable
	{
		private SerialPort _serialPort;

		public Task OpenAsync(string portName, CancellationToken cancellationToken)
		{
			Dispose();
			_serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
			return _serialPort.OpenAsync(cancellationToken);
		}

		public void Dispose()
		{
			_serialPort?.Close();
			_serialPort?.Dispose();
		}

		/// <summary>Получить значение давление, Па</summary>
		/// <returns>Возвращает значение давления, Па</returns>
		public async Task<double> GetPressure(CancellationToken cancellationToken)
		{
			// Отправляем запрос
			var request = CreateRequest(Command.ReadPressure, new byte[] { });
			await _serialPort.WriteAsync(request, 0, request.Length, cancellationToken);

			// Читаем ответ
			var answer = GetData(Command.ReadPressure, ReceiveData(_serialPort));

			// Разбираем результат
			Array.Reverse(answer);
			return Formatter.ConvertFromUnit(BitConverter.ToSingle(answer, 0), GetUnit(answer[0]));
		}

		private byte[] ReceiveData(SerialPort stream)
		{
			var result = new List<byte>();
			var stopWatch = new Stopwatch();
			stopWatch.Start();

			while (stopWatch.ElapsedMilliseconds < 150)
			{
				var buffer = new byte[100];
				int readenBytes = stream.Read(buffer, 0, buffer.Length);
				for (int i = 0; i < readenBytes; i++)
					result.Add(buffer[i]);
			}
			stopWatch.Reset();
			return result.ToArray();
		}

		/// <summary>Единицы измерения из её кода присланного устройством</summary>
		/// <param name="b">Код единицы измерения</param>
		/// <returns>Возвращает единицу измерения давления соответствующую коду</returns>
		private Formatter.UnitsListEnumerator GetUnit(byte b)
		{
			switch (b)
			{
				case 1:
					return Formatter.UnitsListEnumerator.давление_кг_см2;
				case 2:
					return Formatter.UnitsListEnumerator.давление_МПа;
				case 3:
					return Formatter.UnitsListEnumerator.давление_кПа;
				case 4:
					return Formatter.UnitsListEnumerator.давление_Па;
				case 5:
					return Formatter.UnitsListEnumerator.давление_кгс_м2;
				case 6:
					return Formatter.UnitsListEnumerator.давление_атм;
				case 7:
					return Formatter.UnitsListEnumerator.давление_мм_рт_ст;
				case 8:
					return Formatter.UnitsListEnumerator.давление_мм_вод_ст;
				case 9:
					return Formatter.UnitsListEnumerator.давление_бар;
				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>Контрольная сумма пакета</summary>
		/// <param name="pack">Пакет данных</param>
		/// <returns>Возвращает контрольную сумму</returns>
		private byte Crc(byte[] pack)
		{
			byte crc = 0;
			for (int i = 3; i < pack.Length; i++)
				crc ^= pack[i];

			return crc;
		}

		/// <summary>Получить блок данных из пакета</summary>
		/// <param name="command">Команда (для проверки правильности пакета)</param>
		/// <param name="pack">Пакет</param>
		/// <returns>Возвращает блок данных пакета</returns>
		private byte[] GetData(Command command, byte[] pack)
		{
			if (pack.Length < 10 || pack[10] + 12 > pack.Length)
				throw new IOException("[Dm5002] Too short packet");

			if (pack[9] != (byte)command)
				throw new IOException("[Dm5002]Wrong command");

			if (0 != Crc(pack))
				throw new IOException("[Dm5002]Crc error");

			var data = new byte[pack[10]];
			for (int i = 0; i < data.Length; i++)
				data[i] = pack[13 + i];

			return data;
		}

		/// <summary>Создать пакет для отправки в прибор</summary>
		/// <param name="command">Команда</param>
		/// <param name="data">Данные для отправки</param>
		/// <returns>Возвращает пакет</returns>
		private byte[] CreateRequest(Command command, byte[] data)
		{
			var pack = new List<byte>
			{
				// Преамбула
				0xFF,
				0xFF,
				0xFF,

				// Стартовый символ
				0x82,

				// Адрес
				0xFF,
				0xFF,
				0xFF,
				0xFF,
				0,

				// Команда
				(byte)command,

				// Число байт
				(byte)data.Length
			};

			// Данные
			foreach (var d in data)
				pack.Add(d);

			// Контрольная сумма
			pack.Add(Crc(pack.ToArray()));

			return pack.ToArray();
		}

		/// <summary>Команды</summary>
		private enum Command : byte
		{
			/// <summary>Прочитать давление</summary>
			ReadPressure = 1
		}
	}

	/// <summary>Интерфейс манометра ДМ5002</summary>
	public interface IDm5002
	{
		Task OpenAsync(string portName, CancellationToken cancellationToken);

		Task<double> GetPressure(CancellationToken cancellationToken);
	}

	/// <summary>Эмулятор манометра ДМ5002</summary>
	public class Dm5002Emulator : IDm5002, IDisposable
	{
		private readonly Func<DateTime, double> _pressureFunc;

		public Dm5002Emulator(Func<DateTime, double> pressureFunc = null)
		{
			_pressureFunc = pressureFunc ?? (t => t.Second);
		}

		public Task OpenAsync(string portName, CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task<double> GetPressure(CancellationToken cancellationToken)
		{
			return Task.Run(() =>
			{
				cancellationToken.WaitHandle.WaitOne(300);
				return _pressureFunc(DateTime.Now);
			}, cancellationToken);
		}

		public void Dispose()
		{
		}
	}
}
