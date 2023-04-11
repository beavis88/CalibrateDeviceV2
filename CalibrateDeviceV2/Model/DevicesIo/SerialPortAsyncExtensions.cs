using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace CalibrateDevice
{
	/// <summary>Асинхронные расширения COM-порта</summary>
	public static class SerialPortAsyncExtensions
	{
		public static Task OpenAsync(this SerialPort serialPort, CancellationToken cancellationToken)
		{
			return Task.Run(serialPort.Open, cancellationToken);
		}

		public static Task<int> ReadByteAsync(this SerialPort serialPort, CancellationToken cancellationToken)
		{
			return Task.Run(serialPort.ReadByte, cancellationToken);
		}

		public static Task<int> ReadCharAsync(this SerialPort serialPort, CancellationToken cancellationToken)
		{
			return Task.Run(serialPort.ReadChar, cancellationToken);
		}

		public static async Task WriteAsync(this SerialPort serialPort, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
		{
			await serialPort.BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
		}
		
		public static Task<int> ReadAsync(this SerialPort serialPort, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
		{
			return serialPort.BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
		}

		public static async Task ReadAllAsync(this SerialPort serialPort, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default, int timeout = 1000)
		{
			var stopReadDateTime = DateTime.Now.AddMilliseconds(timeout);
			var bytesToRead = count;
			var temp = new byte[count];

			while (bytesToRead > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (DateTime.Now > stopReadDateTime)
					throw new TimeoutException();

				var readBytes = await serialPort.BaseStream.ReadAsync(temp, 0, bytesToRead, cancellationToken);
				if (readBytes > 0)
				{
					stopReadDateTime = DateTime.Now.AddMilliseconds(timeout);
					Array.Copy(temp, 0, buffer, offset + count - bytesToRead, readBytes);
					bytesToRead -= readBytes;
				}
			}
		}

		public static async Task<byte[]> ReadAllAsync(this SerialPort serialPort, int count, CancellationToken cancellationToken = default, int timeout = 1000)
		{
			var buffer = new byte[count];
			await serialPort.ReadAllAsync(buffer, 0, count, cancellationToken, timeout);
			return buffer;
		}

		public static Task<string> ReadStringAsync(this SerialPort serialPort, CancellationToken cancellationToken = default)
		{
			return Task.Run(serialPort.ReadLine, cancellationToken);
		}
	}
}
