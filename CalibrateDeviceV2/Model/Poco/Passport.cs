using System;
using System.Collections.Generic;
using System.Linq;

namespace CalibrateDeviceV2
{
	public class Passport
	{
		public ushort DeviceType { get; set; }
		public ushort SerialNumber { get; set; }
		public ushort Modification { get; set; }
		public ushort Build { get; set; }
		public DateTime NextCheckDateTime { get; set; }
		public string Address { get; set; }
	}

	public class MeasureEntry
	{
		public DateTime MeasureDateTime { get; set; }
		public double ActualTemperatureC { get; set; }
	}

	public class RowViewModel
	{
		public RowViewModel(IReadOnlyCollection<MeasureEntry> measureEntries)
		{
			MeasureDateTime = measureEntries.First().MeasureDateTime;
			AverageActualTemperatureC = measureEntries.Average(e => e.ActualTemperatureC);
			MeasureCount = measureEntries.Count;
			ActualTemperatureC = string.Join(" ", measureEntries.Select(e => e.ActualTemperatureC.ToString("0.##")));
		}

		public DateTime MeasureDateTime { get; }
		public int MeasureCount { get; }
		public double AverageActualTemperatureC { get; }
		public string ActualTemperatureC { get; }
	}
}
