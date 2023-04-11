using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace CalibrateDevice
{
	/*
	/// <summary>Класс работы с динамометром T-8</summary>
	public class Dynamometer
	{
		private SerialPort _serialPort;

		#region Public

		/// <summary>Старт чтения данных поступающих от прибора</summary>
		/// <param name="ConnectionPortName">Название порта подключения к прибору</param>
		public void StartReading(string ConnectionPortName)
		{
		//	StopReading();
			var comSteam = new SerialPort(ConnectionPortName, 2400, Parity.None, 8, StopBits.One);
			_serialPort = comSteam;
			try
			{
				// Открываем com-порт			
				comSteam.OpenAsync(CancellationToken.None);
			}
			catch (Exception e)
			{
				Trace.TraceError(e.Message, e.ToString());
				System.Windows.Forms.MessageBox.Show("Не удаётся открыть порт", "Ошибка", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
			//	StopReading();
				return;
			}

			//base.StartReading(ConnectionPortName);
		}

		/// <summary>Переключатель записи данных поступающих от динамометра в список slMeasure</summary>
		public bool CollectData
		{
			get
			{
				return _collectData;
			}

			set
			{				
				_collectData = value;

				Trace.TraceError("Запись данных с АЦДР-100 " + (value ? "включена" : "выключена" ), "");
			}
		}

		bool _collectData = false;

		/// <summary>Список с результатами измерений</summary>
		public SortedList<DateTime, float> slMeasure = new SortedList<DateTime, float>();

		/// <summary>Среднее значение усилий из коллекции измеренных значений slMeasure</summary>
		public float AverageForce
		{
			get
			{
				float fAvgDyn = 0;
				for(int i = 0; i < slMeasure.Count; i++)
					fAvgDyn += slMeasure.Values[i];

				return fAvgDyn / slMeasure.Count;
			}
		}

		#endregion		
		
		/// <summary>Нить, выполняющая разбор данных поступающих через порт</summary>
		protected void ReadDataThread()
		{
			const byte cSyncByte = 0xa5; // байт синхронизации

			byte[] buf = new byte[11];

			try
			{
				// очищаем входной буфер потока
				while (_serialPort.Read(buf, 0, buf.Length) == buf.Length)
				{
				}

				while(true)
				{
					Thread.Sleep(100);

#if EMUL_DEVICES
					Thread.Sleep(900);
					Random r = new Random();
					float val = (float)r.NextDouble();

					if(updateCurrentValueCallback != null)
						updateCurrentValueCallback(val);

					if(CollectData)
						slMeasure.Add(DateTime.Now, val);
#else
					if(_serialPort.IsOpen && _serialPort.Read(buf, 0, 1) == 1) // ждём байт синхронизации
					{
						Thread.Sleep(10);
						if(buf[0] == cSyncByte)
						{
							Thread.Sleep(50);
							int nReaden = 0;
							if((nReaden = _serialPort.Read(buf, 1, 10)) == 10) // получен байт синхронизации, читаем данные измерения
							{
								string sValue = Encoding.Default.GetString(buf, 2, 6);

								int exp;
								if(!int.TryParse(Encoding.Default.GetString(buf, 8, 1), out exp))
								{
									Trace.TraceError("Ошибка при разборе значения полученного от АЦДР-100", "");
									continue;
								}

								sValue = sValue.Insert(exp, ".");

								float value;
								if(!float.TryParse(sValue, out value))
								{
									sValue = sValue.Replace('.', ',');
									if (!float.TryParse(sValue, out value))
									{
										Trace.TraceError("Ошибка при разборе значения полученного от АЦДР-100", "");
										continue;
									}
								}

								if(buf[1] == '-')
									value *= -1;

								float currentForce = GetForce(buf[9], value);
								if (float.IsNaN(currentForce))
								{
									Trace.TraceError("Ошибка при разборе значения полученного от АЦДР-100", "");
									continue;
								}

								Trace.TraceError("Получено значение усилия: " + currentForce, "");

							//	updateCurrentValueCallback?.Invoke(currentForce);

								if(CollectData)
									slMeasure.Add(DateTime.Now, currentForce);
							}
						}
					}
#endif
				}
			}
			catch(Exception e)
			{
				Trace.TraceError("Ошибка в нити чтения усилия АЦДР-100", e.ToString());
			}
		}

		#region Функции работы с данными прибора

		/// <summary>
		/// Функция декодирования диапазона измерения 
		/// </summary>
		/// <param name="b">Байт-код, передающий шестнадцатеричный номер диапазона измерения</param>
		/// <returns>Диапазон измерения</returns>
		static int GetRange(byte b)
		{
			switch(b)
			{
				case 0x00: return 2000;
				case 0x01: return 3000;
				case 0x02: return 4000;
				case 0x03: return 5000;
				case 0x04: return 6000;
				case 0x05: return 8000;
				case 0x06: return 10000;
				case 0x07: return 15000;
				case 0x08: return 20000;
				case 0x09: return 30000;
				case 0x0a: return 40000;
				case 0x0b: return 50000;
				case 0x0c: return 60000;
				case 0x0d: return 80000;
				case 0x0e: return 100000;
				case 0x0f: return 150000;
				case 0x10: return 200000;
				case 0x11: return 300000;
				case 0x12: return 400000;
				case 0x13: return 500000;
				default: return 0;
			}
		}

		/// <summary>Выяснить наличие перегрузки в процессе измерения</summary>
		/// <param name="b">Байт, содержащий информацию о перегрузке</param>
		/// <returns>true, если была перегрузка</returns>
		static bool GetOverload(byte b)
		{
			return b >= 0x35;
		}

		/// <summary>Получить значение силы в ед. СИ</summary>
		/// <param name="b">Код размерности усилия</param>
		/// <param name="force">Значение усилия в проивольных ед. измерения</param>
		/// <returns>Значение усилия в ед. СИ</returns>
		static float GetForce(byte b, float force)
		{
			const float g = 9.81f; // ускорение свободного падения

			if (b == 0x30 || b == 0x35) // килоньютон              
				return 1000f * force;

			if (b == 0x31 || b == 0x36) // ньютоны 
				return force;

			if (b == 0x32 || b == 0x37) // тонны 
				return (float) Formatter.ConvertFromUnit(force, Formatter.UnitsListEnumerator.масса_тн);

			if (b == 0x33 || b == 0x38) // килограммы
				return force * g;

			Trace.TraceError("GetForce unexpected value b: {0}", b);

			return float.NaN;
		}

		#endregion
	}
	*/
}
