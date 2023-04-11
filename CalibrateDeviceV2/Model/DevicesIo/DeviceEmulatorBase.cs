using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CalibrateDeviceV2.Model.DevicesIo
{
	public class DeviceEmulatorBase
	{
		private Random _random = new Random(DateTime.Now.Microsecond);

		protected double GetRandomEpsilon(double epsilon) => epsilon * (_random.NextDouble() - 1);
	}
}
