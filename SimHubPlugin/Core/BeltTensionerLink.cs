using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiyBeltTensioner.Core
{
    /// <summary>
    /// Serial connection to the belt tensioner controller.
    ///
    /// Speaks both protocols of the firmware (see docs/commands.md):
    ///  - binary SimHub frames  FF FF [cmd] [payload] 0A 0D  (position, speed, park, ...)
    ///  - ASCII commands terminated with a line feed       (CONFIG, SET_PITCH, HOME, ...)
    ///
    /// The port is opened like SimHub Motion does (250000 baud, DTR/RTS low), so the
    /// native USB workarounds of the firmware (HostSerial.h) apply unchanged.
    /// Only one program can use the port: close the connection before enabling SimHub Motion.
    /// </summary>
    public sealed class BeltTensionerLink : IDisposable
    {
        public const int BaudRate = 250000;

        private readonly object writeLock = new object();
        private readonly object listenerLock = new object();
        private readonly List<Action<string>> listeners = new List<Action<string>>();
        private SerialPort port;
        private Thread readerThread;
        private volatile bool running;

        /// <summary>Every line received from the controller (raised on the reader thread).</summary>
        public event Action<string> LineReceived;

        /// <summary>Raised when the connection is opened or closed (raised on any thread).</summary>
        public event Action<bool> ConnectionChanged;

        public bool IsOpen => port != null && port.IsOpen && running;
        public string PortName { get; private set; } = "";
        public string FirmwareVersion { get; private set; } = "";
        public int EnabledMotors { get; private set; } = 0;

        public void Open(string portName)
        {
            Close();
            if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("No COM port selected");

            var p = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                DtrEnable = false,
                RtsEnable = false,
                Handshake = Handshake.None,
                ReadTimeout = 100,
                WriteTimeout = 500,
                Encoding = Encoding.GetEncoding(28591), // Latin-1, 1:1 byte mapping
            };

            try
            {
                p.Open();
            }
            catch (UnauthorizedAccessException)
            {
                p.Dispose();
                throw new IOException(portName + " is in use by another program. Disable SimHub Motion for the belt tensioner (and close any serial monitor), then try again.");
            }

            port = p;
            PortName = portName;
            FirmwareVersion = "";
            EnabledMotors = 0;
            running = true;
            readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "BeltTensionerReader" };
            readerThread.Start();
            ConnectionChanged?.Invoke(true);
        }

        public void Close()
        {
            bool wasOpen = port != null;
            running = false;
            var p = port;
            port = null;
            if (p != null)
            {
                try { p.Close(); } catch (Exception) { }
                try { p.Dispose(); } catch (Exception) { }
            }
            var t = readerThread;
            readerThread = null;
            if (t != null && t != Thread.CurrentThread)
            {
                t.Join(500);
            }
            if (wasOpen)
            {
                ConnectionChanged?.Invoke(false);
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void ReaderLoop()
        {
            var buffer = new byte[512];
            var line = new StringBuilder();
            while (running)
            {
                int count;
                try
                {
                    var p = port;
                    if (p == null) break;
                    count = p.BaseStream.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (Exception)
                {
                    // Device unplugged / restarted
                    if (running)
                    {
                        Task.Run(() => Close());
                    }
                    break;
                }

                for (int i = 0; i < count; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\n')
                    {
                        Dispatch(line.ToString().TrimEnd('\r'));
                        line.Clear();
                    }
                    else if (c != '\r' || line.Length > 0)
                    {
                        line.Append(c);
                        if (line.Length > 4096)
                        {
                            Dispatch(line.ToString());
                            line.Clear();
                        }
                    }
                }
            }
        }

        private void Dispatch(string line)
        {
            if (line.StartsWith("Enabled motors:", StringComparison.Ordinal))
            {
                int n;
                if (int.TryParse(line.Substring("Enabled motors:".Length).Trim(), out n)) EnabledMotors = n;
            }
            else if (line.EndsWith(" steppers enabled", StringComparison.Ordinal))
            {
                int n;
                if (int.TryParse(line.Substring(0, line.IndexOf(' ')), out n)) EnabledMotors = n;
            }
            else if (IsVersionLine(line))
            {
                FirmwareVersion = line.Trim();
            }

            LineReceived?.Invoke(line);

            Action<string>[] current;
            lock (listenerLock)
            {
                current = listeners.ToArray();
            }
            foreach (var l in current)
            {
                try { l(line); } catch (Exception) { }
            }
        }

        private static bool IsVersionLine(string line)
        {
            Version v;
            string t = line.Trim();
            return t.Length > 0 && t.Length < 16 && char.IsDigit(t[0]) && t.Contains(".") && Version.TryParse(t, out v);
        }

        private void Write(byte[] data)
        {
            var p = port;
            if (p == null || !p.IsOpen) throw new InvalidOperationException("Not connected");
            lock (writeLock)
            {
                p.BaseStream.Write(data, 0, data.Length);
                p.BaseStream.Flush();
            }
        }

        // ------------------------------------------------------------------
        // ASCII commands
        // ------------------------------------------------------------------

        public void SendLine(string command)
        {
            Write(Encoding.ASCII.GetBytes(command.Trim() + "\n"));
        }

        /// <summary>
        /// Sends an ASCII command and collects the reply lines.
        /// Finishes when <paramref name="isLastLine"/> returns true, when no line arrived for
        /// <paramref name="idleMs"/> after the first reply line, or after <paramref name="timeoutMs"/>.
        /// </summary>
        public async Task<List<string>> RequestAsync(string command, Func<string, bool> isLastLine, int timeoutMs = 2000, int idleMs = 400)
        {
            return await CollectAsync(() => SendLine(command), isLastLine, timeoutMs, idleMs).ConfigureAwait(false);
        }

        public async Task<List<string>> CollectAsync(Action send, Func<string, bool> isLastLine, int timeoutMs, int idleMs)
        {
            var lines = new List<string>();
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            DateTime lastLine = DateTime.MinValue;

            Action<string> listener = line =>
            {
                lock (lines)
                {
                    lines.Add(line);
                    lastLine = DateTime.UtcNow;
                }
                if (isLastLine != null && isLastLine(line)) done.TrySetResult(true);
            };

            lock (listenerLock) listeners.Add(listener);
            try
            {
                try
                {
                    send();
                }
                catch (Exception ex)
                {
                    LineReceived?.Invoke("[plugin] send failed: " + ex.Message);
                    return new List<string>();
                }
                var start = DateTime.UtcNow;
                while (!done.Task.IsCompleted)
                {
                    await Task.WhenAny(done.Task, Task.Delay(50)).ConfigureAwait(false);
                    var now = DateTime.UtcNow;
                    if ((now - start).TotalMilliseconds > timeoutMs) break;
                    lock (lines)
                    {
                        if (lines.Count > 0 && idleMs > 0 && (now - lastLine).TotalMilliseconds > idleMs) break;
                    }
                }
            }
            finally
            {
                lock (listenerLock) listeners.Remove(listener);
            }
            lock (lines) return new List<string>(lines);
        }

        // ------------------------------------------------------------------
        // Binary SimHub frames
        // ------------------------------------------------------------------

        public void SendFrame(byte command, params byte[] payload)
        {
            var frame = new byte[2 + 1 + payload.Length + 2];
            frame[0] = 0xFF;
            frame[1] = 0xFF;
            frame[2] = command;
            Buffer.BlockCopy(payload, 0, frame, 3, payload.Length);
            frame[frame.Length - 2] = 0x0A;
            frame[frame.Length - 1] = 0x0D;
            Write(frame);
        }

        /// <summary>CMD 1: target positions (0..65535) of motor 1 and 2.</summary>
        public void SetPosition(ushort m1, ushort m2)
        {
            SendFrame(1, (byte)(m1 >> 8), (byte)m1, (byte)(m2 >> 8), (byte)m2);
        }

        /// <summary>CMD 2: SimHub "maximum steppers pulse frequency" (36000 = full speed).</summary>
        public void SetMaxSpeed(ushort m1, ushort m2)
        {
            SendFrame(2, (byte)(m1 >> 8), (byte)m1, (byte)(m2 >> 8), (byte)m2);
        }

        /// <summary>CMD 3: SimHub "maximum steppers acceleration" (100000 = full acceleration).</summary>
        public void SetMaxAcceleration(uint m1, uint m2)
        {
            SendFrame(3,
                (byte)(m1 >> 24), (byte)(m1 >> 16), (byte)(m1 >> 8), (byte)m1,
                (byte)(m2 >> 24), (byte)(m2 >> 16), (byte)(m2 >> 8), (byte)m2);
        }

        public void QueryEnabledMotors() { SendFrame(10); }
        public void DumpSensor(byte axisIndex) { SendFrame(11, axisIndex); }
        public void ParkNow() { SendFrame(12); }
        public void Recalibrate() { SendFrame(13); }
        public void QueryFirmwareVersion() { SendFrame(14); }

        /// <summary>Converts a stroke position in percent (0..100) to the 16 bit SimHub value.</summary>
        public static ushort PercentToValue(double percent)
        {
            if (double.IsNaN(percent)) percent = 0;
            percent = Math.Max(0, Math.Min(100, percent));
            return (ushort)Math.Round(percent / 100.0 * 65535.0);
        }
    }
}
