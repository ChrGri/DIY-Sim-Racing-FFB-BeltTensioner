using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace DiyBeltTensioner.Core
{
    /// <summary>
    /// Finds out whether a USB-UART port (CP2102 boards) runs the belt tensioner firmware.
    /// These boards have no USB product string of their own, so the port is opened like
    /// SimHub does and the boot greeting ("1 steppers enabled") or the reply to CMD 14
    /// (protocol version) is awaited. Opening the port resets an ESP32 behind a CP2102.
    /// </summary>
    public static class DeviceScanner
    {
        public static async Task<DeviceKind> ProbeAsync(string portName, int timeoutMs = 3000)
        {
            return await Task.Run(() => Probe(portName, timeoutMs)).ConfigureAwait(false);
        }

        private static DeviceKind Probe(string portName, int timeoutMs)
        {
            SerialPort port = null;
            try
            {
                port = new SerialPort(portName, BeltTensionerLink.BaudRate, Parity.None, 8, StopBits.One)
                {
                    DtrEnable = false,
                    RtsEnable = false,
                    ReadTimeout = 100,
                    WriteTimeout = 300,
                    Encoding = Encoding.GetEncoding(28591),
                };
                port.Open();
            }
            catch (Exception)
            {
                port?.Dispose();
                return DeviceKind.Unknown; // in use or gone
            }

            try
            {
                var text = new StringBuilder();
                var buffer = new byte[256];
                var sw = Stopwatch.StartNew();
                bool queried = false;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    // After the boot time ask for the protocol version (in case the board did not reset)
                    if (!queried && sw.ElapsedMilliseconds > 1500)
                    {
                        queried = true;
                        try
                        {
                            port.BaseStream.Write(new byte[] { 0xFF, 0xFF, 14, 0x0A, 0x0D }, 0, 5);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    int count;
                    try
                    {
                        count = port.BaseStream.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        text.Append((char)buffer[i]);
                    }
                    string s = text.ToString();
                    if (s.Contains("steppers enabled") || (queried && LooksLikeVersion(s)))
                    {
                        return DeviceKind.BeltTensioner;
                    }
                    if (s.IndexOf("pedal", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return DeviceKind.FfbPedal;
                    }
                    if (text.Length > 8192) text.Remove(0, 4096);
                }
                return DeviceKind.Unknown;
            }
            catch (Exception)
            {
                return DeviceKind.Unknown;
            }
            finally
            {
                try { port.Close(); } catch (Exception) { }
                port.Dispose();
            }
        }

        private static bool LooksLikeVersion(string s)
        {
            foreach (var raw in s.Split('\n'))
            {
                string line = raw.Trim();
                Version v;
                if (line.Length > 0 && line.Length < 8 && char.IsDigit(line[0]) && line.Contains(".") && Version.TryParse(line, out v))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
