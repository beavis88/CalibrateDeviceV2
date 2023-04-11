using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace CalibrateDevice
{
	/// <summary>Класс управления термокамерой</summary>
	public class HeatChamber : IHeatChamber, IDisposable
	{
		private SerialPort _serialPort;

		#region Public

		/// <summary>Таймаут (мс) при ожидании ответа от термокамеры</summary>
		public uint Timeout = 1000;

		/// <summary>Адрес устройства</summary>
		public ushort DeviceAddress = 1;

		/// <summary>Тип устройства</summary>
		public byte DeviceType = 0x62;

		public Task OpenAsync(string portName, CancellationToken cancellationToken)
		{
			Dispose();
			_serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
			return _serialPort.OpenAsync(cancellationToken);
		}

		public void Dispose()
		{
			_serialPort?.Close();
			_serialPort?.Dispose();
		}

		/// <summary>Задать специальные параметры (параметры, устанавливаемые при настройке устройства)</summary>
		/// <param name="temperHigh">Температура граничная верхняя</param>
		/// <param name="temperLow">Температура граничная нижняя</param>
		/// <param name="temperDelta">Гистерезис по температуре</param>
		/// <param name="temperDeadZone">Граница  алгоритмов</param>
		/// <param name="temperCorr">Коррекция температуры</param>
		/// <param name="temperSound">Гистерезис по звуку</param>
		/// <param name="coolerOffDelay">Задержка выключения компрессора после последнего срабатывания клапана</param>
		/// <param name="heatTime">Максимальное время нагрева</param>
		/// <param name="coolTime">Максимальное время охлаждения</param>
		/// <param name="flagHumidity">Есть/Нет влажность</param>
		/// <param name="humidityDelta">Гистерезис по влажности</param>
		/// <param name="humidityDeadZone">Граница  алгоритмов</param>
		/// <param name="humidityCorr">Коррекция влажности</param>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task WriteSetupParamsAsync(sbyte temperHigh,
			sbyte temperLow,
			sbyte temperDelta,
			sbyte temperDeadZone,
			sbyte temperCorr,
			sbyte temperSound,
			sbyte coolerOffDelay,
			uint heatTime,
			uint coolTime,
			byte flagHumidity,
			sbyte humidityDelta,
			sbyte humidityDeadZone,
			sbyte humidityCorr,
			CancellationToken cancellationToken = default)
		{
			Trace.TraceInformation("Старт записи настроек термокамеры");

			await SendCommandAsync(CommandId.WriteSettings,
				new[]
				{
					(byte)temperHigh, (byte)temperLow, (byte)temperDelta, (byte)temperDeadZone, (byte)temperCorr,
					(byte)temperSound, (byte)coolerOffDelay,
					(byte)(heatTime >> 8), (byte)heatTime,
					(byte)(coolTime >> 8), (byte)coolTime,
					flagHumidity, (byte)humidityDelta, (byte)humidityDeadZone, (byte)humidityCorr
				}, cancellationToken);
		}

		/// <summary>Прочитать временную схему термокамеры</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		/// <returns>Возвращает временную схему</returns>
		public async Task<(ushort QuantRepeat, TimeSchemeEntry[] TimeScheme)> ReadTimeSchemeAsync(
			CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Старт чтения временной схемы термокамеры");

			var receivedData = await SendCommandAsync(CommandId.ReadTimeScheme, null, cancellationToken);
			if (receivedData.Length < 65)
				throw new IOException($"Ответ на команду {0x05} меньше 65 байт");

			// Разбор временной схемы
			var quantRepeat = BitConverter.ToUInt16(receivedData, 0);
			var timeScheme = new TimeSchemeEntry[9];
			for (int i = 0; i < timeScheme.Length; i++)
			{
				timeScheme[i].Used = receivedData[2 + 7 * i];
				timeScheme[i].Temper = (sbyte)receivedData[2 + 7 * i + 1];
				timeScheme[i].Humidity = (sbyte)receivedData[2 + 7 * i + 2];
				timeScheme[i].MinutesGo = BitConverter.ToUInt16(receivedData, 2 + 7 * i + 3);
				timeScheme[i].MinutesStay = BitConverter.ToUInt16(receivedData, 2 + 7 * i + 5);
			}

			return (quantRepeat, timeScheme);
		}

		/// <summary>Записать временную схему в термокамеру</summary>
		/// <param name="quantRepeat">Количество повторов</param>
		/// <param name="timeScheme">Временная схема</param>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task WriteTimeSchemeAsync(ushort quantRepeat, TimeSchemeEntry[] timeScheme,
			CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Старт записи временной схемы термокамеры, T0=" + timeScheme[0].Temper);

			// Заполнение временной схемы
			byte[] sendData = new byte[65];
			sendData[0] = (byte)quantRepeat;
			sendData[1] = (byte)(quantRepeat >> 8);

			for (int i = 0; i < timeScheme.Length; i++)
			{
				sendData[2 + 7 * i] = timeScheme[i].Used;
				sendData[2 + 7 * i + 1] = (byte)timeScheme[i].Temper;
				sendData[2 + 7 * i + 2] = (byte)timeScheme[i].Humidity;
				sendData[2 + 7 * i + 3] = (byte)timeScheme[i].MinutesGo;
				sendData[2 + 7 * i + 4] = (byte)(timeScheme[i].MinutesGo >> 8);
				sendData[2 + 7 * i + 5] = (byte)timeScheme[i].MinutesStay;
				sendData[2 + 7 * i + 6] = (byte)(timeScheme[i].MinutesStay >> 8);
			}

			await SendCommandAsync(CommandId.WriteTimeScheme, sendData, cancellationToken);
		}

		/// <summary>Считывает адрес и тип подключенного прибора</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task GetConnectedDeviceIdAsync(CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Старт чтения адреса и типа термокамеры");
			var receivedData = await SendCommandAsync(0x00, null, cancellationToken);
		}

		/// <summary>Запуск выполнения временной схемы термокамерой</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task StartProcessAsync(CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Запуск выполнения временной схемы термокамеры");
			await SendCommandAsync(CommandId.StartProcess, null, cancellationToken);
		}

		/// <summary>Остановка выполнения временной схемы термокамерой</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task StopProcessAsync(CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Остановка выполнения временной схемы термокамеры");
			await SendCommandAsync(CommandId.StopProcess, null, cancellationToken);
		}

		/// <summary>Синхронизация даты/времени прибора</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task SetCurrentDateTimeAsync(CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Старт установки текущей даты/времени термокамеры");
			await SendCommandAsync(CommandId.SetCurrentDateTime,
				new[]
				{
					(byte)DateTime.Now.Second,
					(byte)DateTime.Now.Minute,
					(byte)DateTime.Now.Hour,
					(byte)DateTime.Now.Day,
					(byte)DateTime.Now.Month,
					(byte)(DateTime.Now.Year % 100)
				}, cancellationToken);
		}

		/// <summary>Выдать текущие параметры</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		/// <returns>Возвращает упорядоченную тройку (текущая температура (в градусах Цельсия, со знаком), Влажность, в %, Время, прошедшее с начала процесса, в %)</returns>
		public async Task<(sbyte Temperature, byte Humidity, byte Progress)> GetCurrentParams(
			CancellationToken cancellationToken)
		{
			Trace.TraceInformation("Старт чтения текущих измеренных параметров термокамеры");

			var receivedData = await SendCommandAsync(CommandId.ReadCurrentParams, null, cancellationToken);
			if (receivedData.Length < 13)
				throw new IOException($"Ответ на команду {0x01} меньше 13 байт");

			return ((sbyte)receivedData[10], receivedData[11], receivedData[12]);
		}

		#endregion

		/// <summary>Посылает команду в термокамеру</summary>
		/// <param name="commandId">Идентификатор команды</param>
		/// <param name="data2Send">Данные команды</param>
		/// <param name="cancellationToken">Токен отмены</param>
		private async Task<byte[]> SendCommandAsync(CommandId commandId, byte[] data2Send, CancellationToken cancellationToken)
		{
			byte[] sendPack = new byte[6 + (data2Send != null ? data2Send.Length : 0)];
			sendPack[0] = (byte)sendPack.Length; // Длина пакета
			sendPack[1] = DeviceType; // Тип устройства
			sendPack[2] = (byte)DeviceAddress; // Мл. байт адреса устройства
			sendPack[3] = (byte)(DeviceAddress >> 8); // Ст. байт адреса устройства
			sendPack[4] = (byte)commandId; // Команда

			// Заполнение поля данных
			for (int i = 0; data2Send != null && i < data2Send.Length; i++)
				sendPack[5 + i] = data2Send[i];

			// Контрольная сумма
			sendPack[sendPack.Length - 1] = Crc(sendPack);

			// Отправка пакета
			foreach (byte b in sendPack)
				try
				{
					await _serialPort.WriteAsync(new[] { b }, 0, 1, cancellationToken);
				}
				catch (Exception ex)
				{
					throw new IOException($"Ошибка при отправке в термокамеру команды {commandId:X}, {ex.Message}");
				}

			// Ждём прихода первого байта (содержащего длину приходящего пакета)
			byte[] buf1 = new byte[1];

			var iEndReading = DateTime.Now.AddSeconds(Timeout);
			int iBytesReceived = 0;

			while (DateTime.Now < iEndReading)
			{
				if ((iBytesReceived = await _serialPort.ReadAsync(buf1, 0, 1, cancellationToken)) == 1)
					break;

				cancellationToken.WaitHandle.WaitOne(10);
				cancellationToken.ThrowIfCancellationRequested();
			}

			// Байт не пришёл
			if (iBytesReceived == 0)
				throw new TimeoutException($"Не пришёл ответ на команду {commandId:X}");

			var receivePack = new byte[buf1[0]];
			receivePack[0] = buf1[0];

			// Считываем остальную часть пакета
			for (int q = 1; q < receivePack.Length; q++)
			{
				iEndReading = DateTime.Now.AddSeconds(Timeout);
				iBytesReceived = 0;

				while (DateTime.Now < iEndReading)
				{
					if ((iBytesReceived = await _serialPort.ReadAsync(buf1, 0, 1, cancellationToken)) == 1)
						break;

					cancellationToken.WaitHandle.WaitOne(10);
					cancellationToken.ThrowIfCancellationRequested();
				}

				// Пришли не все байты
				if (iBytesReceived != 1)
					throw new TimeoutException($"Пришёл не полный ответ на команду {commandId:X}");

				receivePack[q] = buf1[0];
			}

			// Проверка Crc
			if (Crc(receivePack) != receivePack[receivePack.Length - 1])
				throw new TimeoutException($"Ошибка crc на команду {commandId:X}");

			// Сохраняем принятые данные (если что-то пришло)
			var receivedData = new byte[receivePack.Length - 6];
			for (int i = 0; i < receivedData.Length; i++)
				receivedData[i] = receivePack[5 + i];

			var readenDeviceType = receivePack[1];
			var readenDeviceAddress = BitConverter.ToUInt16(receivePack, 2);

			Trace.TraceInformation(
				$"Получен ответ (тип устройства {readenDeviceType}, адрес {readenDeviceAddress}) на команду " +
				commandId.ToString("X"), receivedData.ToString());

			return receivedData;
		}

		/// <summary>Вычисляет контрольную сумму элементов вектора кроме последнего</summary>
		/// <param name="data">Входной массив</param>
		/// <returns>Значение контрольной суммы массива, не включая последний элемент</returns>
		private static byte Crc(byte[] data)
		{
			byte crc = 0;
			for (int f = 0; f < data.Length - 1; f++)
				crc += data[f];

			return (byte)(256 - crc);
		}
	}

	/// <summary>Структура с параметрами одного шага для циклического режима</summary>
	public struct TimeSchemeEntry
	{
		/// <summary>Использован или нет</summary>
		public byte Used;

		/// <summary>Температура установки заданная </summary>
		public sbyte Temper;

		/// <summary>Влажность заданная</summary>
		public sbyte Humidity;

		/// <summary>Время выхода на температуру установки (в минутах)</summary>
		public ushort MinutesGo;

		/// <summary>Время выдержки при температуре установки (в минутах)</summary>
		public ushort MinutesStay;
	}

	/// <summary>Коды команд</summary>
	public enum CommandId : byte
	{
		ReadCurrentParams = 0x01,
		WriteTimeScheme = 0x04,
		ReadTimeScheme = 0x05,
		StartProcess = 0x06,
		StopProcess = 0x07,
		WriteSettings = 0x14,
		SetCurrentDateTime = 0x0b
	}

	/// <summary>Интерфейс термокамеры</summary>
	public interface IHeatChamber
	{
		Task OpenAsync(string portName, CancellationToken cancellationToken);

		Task WriteSetupParamsAsync(sbyte temperHigh,
			sbyte temperLow,
			sbyte temperDelta,
			sbyte temperDeadZone,
			sbyte temperCorr,
			sbyte temperSound,
			sbyte coolerOffDelay,
			uint heatTime,
			uint coolTime,
			byte flagHumidity,
			sbyte humidityDelta,
			sbyte humidityDeadZone,
			sbyte humidityCorr,
			CancellationToken cancellationToken = default);

		Task<(ushort QuantRepeat, TimeSchemeEntry[] TimeScheme)> ReadTimeSchemeAsync(CancellationToken cancellationToken);

		Task WriteTimeSchemeAsync(ushort quantRepeat, TimeSchemeEntry[] timeScheme, CancellationToken cancellationToken);

		Task StartProcessAsync(CancellationToken cancellationToken);

		Task StopProcessAsync(CancellationToken cancellationToken);

		Task SetCurrentDateTimeAsync(CancellationToken cancellationToken);

		Task<(sbyte Temperature, byte Humidity, byte Progress)> GetCurrentParams(CancellationToken cancellationToken);
	}

	public class HeatChamberEmulator : IHeatChamber, IDisposable
	{
		private TimeSchemeEntry[] _timeScheme;

		public Task OpenAsync(string portName, CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task WriteSetupParamsAsync(sbyte temperHigh, sbyte temperLow, sbyte temperDelta, sbyte temperDeadZone, sbyte temperCorr,
			sbyte temperSound, sbyte coolerOffDelay, uint heatTime, uint coolTime, byte flagHumidity, sbyte humidityDelta,
			sbyte humidityDeadZone, sbyte humidityCorr, CancellationToken cancellationToken = default)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task<(ushort QuantRepeat, TimeSchemeEntry[] TimeScheme)> ReadTimeSchemeAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() =>
			{
				cancellationToken.WaitHandle.WaitOne(300);
				return ((ushort)1, _timeScheme);
			}, cancellationToken);
		}

		public Task WriteTimeSchemeAsync(ushort quantRepeat, TimeSchemeEntry[] timeScheme, CancellationToken cancellationToken)
		{
			_timeScheme = timeScheme;
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task StartProcessAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task StopProcessAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task SetCurrentDateTimeAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task<(sbyte Temperature, byte Humidity, byte Progress)> GetCurrentParams(CancellationToken cancellationToken)
		{
			return Task.Run(() =>
			{
				cancellationToken.WaitHandle.WaitOne(300);
				return (_timeScheme[0].Temper, (byte)25, (byte)1);
			}, cancellationToken);
		}

		public void Dispose()
		{
		}
	}
}
