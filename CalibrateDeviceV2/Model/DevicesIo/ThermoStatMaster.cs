using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidLibrary;

namespace CalibrationProcess
{
	/// <summary>Термостат Мастер</summary>
	public class ThermoStatMaster : IThermoStatMaster, IDisposable
	{
		private HidDevice _hidDevice;

		#region Public methods

		public Task OpenAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() =>
			{
				Dispose();
				cancellationToken.WaitHandle.WaitOne(300);
				cancellationToken.ThrowIfCancellationRequested();
				_hidDevice = HidDevices.Enumerate(VendorId, ProductId).FirstOrDefault();
				if (_hidDevice == null)
					throw new IOException("[Thermostat Master] Open device error");

				_hidDevice.MonitorDeviceEvents = true;
			}, cancellationToken);
		}

		public void Dispose()
		{
			_hidDevice?.Dispose();
		}

		/// <summary>Запустить установку заданной температуры в термостате</summary>
		/// <param name="temperature">Целевая температура</param>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task SetupTemperature(float temperature, CancellationToken cancellationToken)
		{
			await TurnOffAsync(cancellationToken);
			await TurnOnAsync(cancellationToken);
			await SetRtcAsync(cancellationToken);
			await SetFluAsync(cancellationToken);
			await TurnOnFswControlAsync(cancellationToken);
			await SetTimeSchemeAsync(new[] { temperature }, new[] { 999 }, cancellationToken);
			await StartProgramModeAsync(cancellationToken);
		}

		/// <summary>Выключение</summary>
		public async Task TurnOffAsync(CancellationToken cancellationToken)
		{
			await ProcessRequestAsync(CmdRun, OperWrite, null, null, "0", cancellationToken);

			var dtStop = DateTime.Now.AddSeconds(10);
			while (DateTime.Now < dtStop)
			{
				cancellationToken.WaitHandle.WaitOne(200);
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					// Переподключаемся так как термостат меняет дескрипторы HID устройств
					Dispose();
					await OpenAsync(cancellationToken);

					var result = await ProcessRequestAsync(CmdRun, OperRead, null, null, null, cancellationToken);
					Trace.TraceInformation("[Thermostat Master] CmdRun result (Length {1}): {0}", result, result.Length);
					if (result.Length > 0 && result[0] == '0')
						return;
				}
				catch (Exception ex)
				{
					Trace.TraceInformation("[Thermostat Master] TurnOff error: {0}", ex.Message);
				}
			}
		}

		/// <summary>Включение термостата</summary>
		public async Task TurnOnAsync(CancellationToken cancellationToken)
		{
			await ProcessRequestAsync(CmdRun, OperWrite, null, null, "1", cancellationToken);

			var dtStop = DateTime.Now.AddSeconds(10);
			while (DateTime.Now < dtStop)
			{
				cancellationToken.WaitHandle.WaitOne(200);
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					// Переподключаемся так как термостат меняет дескрипторы HID устройств
					Dispose();
					await OpenAsync(cancellationToken);

					var result = await ProcessRequestAsync(CmdRun, OperRead, null, null, null, cancellationToken);
					Trace.TraceInformation("[Thermostat Master] CmdRun result (Length {1}): {0}", result, result.Length);
					if (result.Length > 0 && result[0] == '1')
						return;
				}
				catch (Exception ex)
				{
					Trace.TraceInformation("[Thermostat Master] TurnOn error: {0}", ex.Message);
				}
			}

			throw new Exception("[Thermostat Master] TurnOn error: can't start device");
		}

		/// <summary>Включить управление холодильной машиной</summary>
		private async Task TurnOnFswControlAsync(CancellationToken cancellationToken)
		{
			await ProcessRequestAsync(CmdFsw, OperWrite, null, null, "1", cancellationToken);
		}

		/// <summary>Выключить управление холодильной машиной</summary>
		private async Task TurnOffFswControlAsync(CancellationToken cancellationToken)
		{
			await ProcessRequestAsync(CmdFsw, OperWrite, null, null, "0", cancellationToken);
		}

		/// <summary>Установка часов RTC</summary>
		private async Task SetRtcAsync(CancellationToken cancellationToken)
		{
			await ProcessRequestAsync(CmdRtc, OperWrite, ParamRtcTime, null, DateTime.Now.ToString("HH:mm"), cancellationToken);
		}

		/// <summary>Запись временной схемы в термостат</summary>
		/// <param name="temperatures">Массив температур (максимум 10 элементов)</param>
		/// <param name="durations">Массив продолжительностей этапов (максимум 10 элементов)</param>
		/// <param name="cancellationToken">Токен отмены</param>
		public async Task SetTimeSchemeAsync(float[] temperatures, int[] durations, CancellationToken cancellationToken)
		{
			for (int i = 1; i <= 10; i++)
			{
				await ProcessRequestAsync(CmdPrg, OperWrite, ParamPrgTemp, i.ToString(),
					(temperatures.Length < i ? 0 : temperatures[i - 1]).ToString(CultureInfo.InvariantCulture), cancellationToken);
				await ProcessRequestAsync(CmdPrg, OperWrite, ParamPrgTime, i.ToString(),
					(durations.Length < i ? 0 : durations[i - 1]).ToString(CultureInfo.InvariantCulture), cancellationToken);
			}
		}

		/// <summary>Установить режим регулирования по программе и запустить её выполнение</summary>
		/// <remarks>При переключении в режим регулирования по программе, программа начинает выполняться с первого не нулевого этапа</remarks>
		public async Task StartProgramModeAsync(CancellationToken cancellationToken)
		{
			await ProcessRequestAsync(CmdMod, OperWrite, null, null, ValueModP, cancellationToken);
		}

		/// <summary>Установка типа теплоносителя</summary>
		/// <param name="cancellationToken">Токен отмены</param>
		/// <param name="flu">Тип теплоносителя</param>
		public async Task SetFluAsync(CancellationToken cancellationToken, ValuesCmdFlu flu = ValuesCmdFlu.Any)
		{
			await ProcessRequestAsync(CmdFlu, OperWrite, null, null, ((byte)flu).ToString(CultureInfo.InvariantCulture), cancellationToken);
		}

		/// <summary>Прочитать температуру теплоносителя</summary>
		/// <returns>Возвращает температуру, °C</returns>
		public async Task<double> GetTemperatureAsync(CancellationToken cancellationToken)
		{
			var tempString = await ProcessRequestAsync(CmdDat, OperRead, ParamDatT, null, null, cancellationToken);

			double temp;
			if (!double.TryParse(tempString, NumberStyles.Any, CultureInfo.InvariantCulture, out temp))
				throw new Exception("[Thermostat Master] Float parse error");

			return temp;
		}

		#endregion Public methods

		#region Methods

		/// <summary>Обработка запроса термостатом</summary>
		/// <param name="command">Команда</param>
		/// <param name="operation">Операция</param>
		/// <param name="parameter">Параметр</param>
		/// <param name="subParameter">Подпараметр</param>
		/// <param name="value">Значение параметра</param>
		/// <param name="cancellationToken">Токен отмены</param>
		/// <returns>Возвращает ответ термостата (если он есть)</returns>
		private async Task<string> ProcessRequestAsync(string command, string operation, string parameter, string subParameter, string value, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			parameter = string.IsNullOrEmpty(parameter)
				? ""
				: $".{parameter}";

			subParameter = string.IsNullOrEmpty(subParameter)
				? ""
				: $".{subParameter}";

			var results = (await SendRequestAsync($":{Address} {command}{parameter}{subParameter} {operation} {value}\n", cancellationToken))
				.Split(' ');

			switch (results[1].ToLower())
			{
				case "0x00": // Операция выполнена успешно
					break;
				case "0x01": // Неверный формат запроса
					throw new Exception("Неверный формат запроса");
				case "0x02": // Неверный формат значения
					throw new Exception("Неверный формат значения");
				case "0x03": // Неизвестный адресат
					throw new Exception("Неизвестный адресат");
				case "0x04": // Неизвестная операция
					throw new Exception("Неизвестная операция");
				case "0x05": // Значение вне диапазона
					throw new Exception("Значение вне диапазона");
				case "0x06": // Команда не доступна в состоянии "выключено"
					throw new Exception(@"Команда не доступна в состоянии ""выключено""");
				default:
					throw new Exception($"Неизвестный статус {results[1]}");
			}

			return results.Length > 2
				? results[2].TrimEnd('\r', '\n', '\0')
				: "";
		}

		/// <summary>Отправка запроса термостату</summary>
		/// <param name="request">Запрос</param>
		/// <param name="cancellationToken">Токен отмены</param>
		/// <returns>Возвращает ответ полученный от термостата</returns>
		private async Task<string> SendRequestAsync(string request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Trace.TraceInformation($"[Thermostat Master] TX: {request}");

			var report = _hidDevice.CreateReport();
			var bytes = System.Text.Encoding.ASCII.GetBytes(request);
			int len = Math.Min(ReportMaxSize, request.Length);
			report.Data = new byte[ReportMaxSize];
			Array.Copy(bytes, report.Data, len);

			if (await _hidDevice.WriteReportAsync(report))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var readenReport = await _hidDevice.ReadReportAsync(1000);
				cancellationToken.ThrowIfCancellationRequested();
				var response = System.Text.Encoding.ASCII.GetString(readenReport.Data, 0, ReportMaxSize);
				Trace.TraceInformation($"[Thermostat Master] RX: {response}");
				return response;
			}

			throw new IOException("[Thermostat Master] WriteReport error");
		}

		#endregion Methods

		#region Const

		/// <summary>Чтение</summary>
		private const string OperRead = "RD";

		/// <summary>Запись</summary>
		private const string OperWrite = "WR";

		/// <summary>Включение/выключение термостата</summary>
		private const string CmdRun = "RUN";

		/// <summary>Уставки температуры</summary>
		private const string CmdSet = "SET";

		#region CmdSet parameters

		/// <summary>Минимально допустимое значение уставок</summary>
		private const string ParamMin = "MIN";

		/// <summary>Максимально допустимое значение уставок</summary>
		private const string ParamMax = "MAX";

		/// <summary>Индекс текущей уставки</summary>
		private const string ParamIdx = "IDX";

		/// <summary>Значение уставки</summary>
		private const string ParamVal = "VAL";

		#endregion CmdSet parameters

		/// <summary>Программа темперирования</summary>
		private const string CmdPrg = "PRG";

		#region CmdPrg parameters

		/// <summary>Температура этапа</summary>
		private const string ParamPrgTemp = "TEMP";

		/// <summary>Продолжительность этапа</summary>
		private const string ParamPrgTime = "TIME";

		#endregion CmdPrg parameters

		/// <summary>Режим регулирования</summary>
		private const string CmdMod = "MOD";

		#region CmdMod values

		/// <summary>Для режима регулирования по уставке</summary>
		private const string ValueModS = "S";

		/// <summary>Для режима регулирования по программе</summary>
		private const string ValueModP = "P";

		#endregion CmdMod values

		/// <summary>Температура теплоносителя</summary>
		private const string CmdDat = "DAT";

		#region CmdDat parameters

		/// <summary>Температура, °C</summary>
		private const string ParamDatT = "T";

		/// <summary>Сопротивление, Ом</summary>
		private const string ParamDatR = "R";

		#endregion CmdDat parameters

		/// <summary>Управление защитами</summary>
		private const string CmdAlm = "ALM";

		#region CmdAlm parameters

		/// <summary>Текущее состояние защит</summary>
		private const string ParamAlmStatus = "STATUS";

		/// <summary>Нижнее значение установщика</summary>
		private const string ParamAlmMin = "MIN";

		/// <summary>Верхнее значение установщика</summary>
		private const string ParamAlmMax = "MAX";

		/// <summary>Установленное значение</summary>
		private const string ParamAlmSet = "SET";

		/// <summary>Текущая температура датчика защиты</summary>
		private const string ParamAlmTemp = "TEMP";

		#endregion CmdAlm parameters

		/// <summary>Параметры датчиков</summary>
		private const string CmdRtd = "RTD";

		#region CmdRtd subparameters (коэффициенты уравнения Каллендара – Ван Дузена)

		private const string SubparamR0 = "R0";

		private const string SubparamA = "A";

		private const string SubparamB = "B";

		private const string SubparamC = "C";

		#endregion CmdRtd subparameters (коэффициенты уравнения Каллендара – Ван Дузена)

		/// <summary>Параметры ПИД-регуляторов</summary>
		private const string CmdPid = "PID";

		#region CmdPid parameters

		/// <summary>Уставка регулятора</summary>
		private const string ParamPidSet = "SET";

		/// <summary>Текущая выходная мощность регулятора</summary>
		private const string ParamPidPwr = "PWR";

		/// <summary>Адаптивный режим</summary>
		private const string ParamPidAuto = "AUTO";

		/// <summary>Коэффициент усиления в адаптивном режиме</summary>
		private const string ParamPidKa = "KA";

		/// <summary>Коэффициент пропорционального регулирования</summary>
		private const string ParamPidKp = "KP";

		/// <summary>Постоянная времени интегрирования</summary>
		private const string ParamPidTi = "TI";

		/// <summary>Постоянная времени дифференцирования</summary>
		private const string ParamPidTd = "TD";

		#endregion CmdPid parameters

		/// <summary>Часы реального времени</summary>
		private const string CmdRtc = "RTC";

		#region CmdRtc parameters

		/// <summary>текущее время</summary>
		private const string ParamRtcTime = "TIME";

		/// <summary>время включения термостата</summary>
		private const string ParamRtcOntime = "ONTIME";

		/// <summary>время выключения термостата</summary>
		private const string ParamRtcOfftime = "OFFTIME";

		/// <summary>разрешение для автоматического включения термостата</summary>
		private const string ParamRtcEnon = "ENON";

		/// <summary>разрешение для автоматического выключения термостата</summary>
		private const string ParamRtcEnoff = "ENOFF";

		#endregion CmdRtc parameters

		/// <summary>Холодильная машина</summary>
		private const string CmdFsw = "FSW";

		/// <summary>Порог готовности</summary>
		private const string CmdRdy = "RDY";

		/// <summary>Серийный номер</summary>
		private const string CmdSer = "SER";

		/// <summary>Тип теплоносителя</summary>
		private const string CmdFlu = "FLU";

		/// <summary>Возможные типы теплоносителей</summary>
		public enum ValuesCmdFlu : byte
		{
			/// <summary>Любой</summary>
			Any = 1,

			/// <summary>Вода</summary>
			Water = 2,

			/// <summary>ПМС-5</summary>
			Pms5 = 3,

			/// <summary>ПМС-10</summary>
			Pms10 = 4,

			/// <summary>ПМС-20</summary>
			Pms20 = 5,

			/// <summary>ПМС-50</summary>
			Pms50 = 6,

			/// <summary>ПМС-100</summary>
			Pms100 = 7,

			/// <summary>Этанол</summary>
			Ethanol = 8,

			/// <summary>ТОСОЛ</summary>
			Tosol = 9
		}

		/// <summary>Внешний датчик</summary>
		private const string CmdExt = "EXT";

		/// <summary>Температурная коррекция</summary>
		private const string CmdCor = "COR";

		/// <summary>Cетевой адрес термостата. Представляет собой строку длиной до 8 символов из множества [0-9], [A-Z], [a-z].
		/// В качестве сетевого адреса в термостатах используется значение уникального серийного номера изделия.
		/// В качестве сетевого адреса, в запросе может использоваться широковещательный адрес, равный "00000000", на который откликается любой термостат.</summary>
		private string Address = "00000000";

		/// <summary>Размер репорта</summary>
		private const int ReportMaxSize = 64;

		private const int VendorId = 0xFFFF;
		private const int ProductId = 0x0003;

		#endregion Const
	}

	/// <summary>Интерфейс термостата мастер</summary>
	public interface IThermoStatMaster
	{
		Task OpenAsync(CancellationToken cancellationToken);

		Task StartProgramModeAsync(CancellationToken cancellationToken);

		Task SetTimeSchemeAsync(float[] temperatures, int[] durations, CancellationToken cancellationToken);

		Task SetupTemperature(float temperature, CancellationToken cancellationToken);

		Task<double> GetTemperatureAsync(CancellationToken cancellationToken);

		Task TurnOffAsync(CancellationToken cancellationToken);

		Task TurnOnAsync(CancellationToken cancellationToken);
	}

	/// <summary>Эмулятор термостата Мастер</summary>
	public class ThermoStatMasterEmulator : IThermoStatMaster, IDisposable
	{
		private double _temperature;

		public Task OpenAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task StartProgramModeAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task SetTimeSchemeAsync(float[] temperatures, int[] durations, CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task SetupTemperature(float temperature, CancellationToken cancellationToken)
		{
			_temperature = temperature * 0.95;
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task<double> GetTemperatureAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() =>
			{
				cancellationToken.WaitHandle.WaitOne(300);
				return _temperature;
			}, cancellationToken);
		}

		public Task TurnOffAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public Task TurnOnAsync(CancellationToken cancellationToken)
		{
			return Task.Run(() => cancellationToken.WaitHandle.WaitOne(300), cancellationToken);
		}

		public void Dispose()
		{
		}
	}
}
