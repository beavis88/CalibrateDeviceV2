using System;
using System.Collections.Generic;
using System.Linq;

namespace CalibrateDeviceV2
{
	public class A835Passport : Passport
	{
		public double PressureUpperLimitMpa { get; set; }

		public string ReferenceDataFileName { get; set; }

		public double MeasuredMaxPressureKpa { get; set; }

		public double MeasuredMaxTemperatureC { get; set; }

		public List<List<CalibrationMeasureEntry>> TemperatureCalibrationEntries { get; set; }

		public List<List<PressureTemperatureMeasureEntry>> TemperatureCheckMeasureEntries { get; set; }

		public List<List<CalibrationMeasureEntry>> PressureCalibrationEntries { get; set; }

		public List<List<ThermalCompensationMeasureEntry>> ThermalCompensationEntries { get; set; }

		public List<List<PressureCheckEntry>> PressureCheckMeasureEntries { get; set; }


		#region Binding properties (not serializable)

		public List<TemperatureCalibrationRowViewModel> TemperatureCalibrationRowsViewModel => TemperatureCalibrationEntries.Select(e => new TemperatureCalibrationRowViewModel(e)).ToList();

		public List<TemperatureCheckRowViewModel> TemperatureCheckRowsViewModel => TemperatureCheckMeasureEntries.Select(e => new TemperatureCheckRowViewModel(e, MeasuredMaxTemperatureC)).ToList();

		#endregion
	}

	#region Row view models



	public class TemperatureCalibrationRowViewModel : RowViewModel
	{
		public TemperatureCalibrationRowViewModel(IReadOnlyCollection<CalibrationMeasureEntry> measureEntries)
			: base(measureEntries)
		{
			AverageTemperatureRaw = (int) measureEntries.Average(e => e.TemperatureRaw);
			TemperatureRaw = string.Join(" ", measureEntries.Select(e => e.TemperatureRaw.ToString()));
		}

		public int AverageTemperatureRaw { get; }

		public string TemperatureRaw { get; }
	}

	public class TemperatureCheckRowViewModel : RowViewModel
	{
		public TemperatureCheckRowViewModel(IReadOnlyCollection<PressureTemperatureMeasureEntry> measureEntries, double measuredMaxTemperatureC)
			: base(measureEntries)
		{
			AverageTemperatureC = measureEntries.Average(e => e.TemperatureC01 * 0.01);
			TemperatureC = string.Join(" ", measureEntries.Select(e => (e.TemperatureC01 * 0.01).ToString("0.##")));
			AbsoluteErrorTemperatureC = Math.Abs(AverageTemperatureC - AverageActualTemperatureC);
			PercentageErrorTemperature = 100 * measuredMaxTemperatureC / AbsoluteErrorTemperatureC;
		}

		public double AverageTemperatureC { get; }

		public string TemperatureC { get; }

		public double AbsoluteErrorTemperatureC { get; }

		public double PercentageErrorTemperature { get; }
	}

	#endregion Row view models

	public class CalibrationMeasureEntry : MeasureEntry
	{
		public int ActualPressurePa { get; set; }

		public int PressureRaw { get; set; }

		public int TemperatureRaw { get; set; }

		public int InternalTemperatureRaw { get; set; }

		public int DieTemperatureC01 { get; set; }
	}

	public class ThermalCompensationMeasureEntry : CalibrationMeasureEntry
	{
		public int AtmospherePressurePa { get; set; }

		public double TargetActualTemperatureC { get; set; }
	}

	public class PressureTemperatureMeasureEntry : MeasureEntry
	{
		public int PressurePa { get; set; }

		public short TemperatureC01 { get; set; }
	}

	public class PressureCheckEntry : PressureTemperatureMeasureEntry
	{
		public ushort BatteryLevel { get; set; }

		public int AtmospherePressurePa { get; set; }
	}

	public class ReferenceMeasureEntry : CalibrationMeasureEntry
	{
		public double TargetActualTemperatureC { get; set; }

		public int TargetActualPressurePa { get; set; }
	}

	public class A835ReferenceMeasure
	{
		public List<ReferenceMeasureEntry> ReferenceMeasureEntries { get; set; }
	}
}
