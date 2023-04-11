using System.Runtime.InteropServices;

namespace CalibrateDevice.DeviceIo
{
	public static class Ft4222hImports
	{
#if EMUL_DEVICES
		public static int SetGpioValue(int gpioValue, int gpioNumber)
		{
			return 0;
		}
#else

		[DllImport("FT4222H.dll", EntryPoint = "SetGpioValue", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
		public static extern int SetGpioValue(int gpioValue, int gpioNumber);
#endif
	}
}
