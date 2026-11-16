using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DiyBeltTensioner.Core
{
    /// <summary>
    /// One belt channel: one controller board with one actuator on one COM port.
    /// Holds the connection, the configuration read from the controller, edits that are not
    /// saved yet and the polled status. Long operations are async and meant to be started from
    /// the UI thread; events can be raised on any thread.
    /// </summary>
    public class TensionerChannel
    {
        // Config.h defaults, shown until the controller has been read
        public const double DefaultPitchMm = 10;
        public const double DefaultRangeMm = 60;

        public TensionerChannel(string key, string title, ChannelSettings settings)
        {
            Key = key;
            Title = title;
            Settings = settings;
            Link = new BeltTensionerLink();
            Tester = new TestRunner(Link) { Axis1 = true, Axis2 = false };
            Link.LineReceived += Link_LineReceived;
            Link.ConnectionChanged += open =>
            {
                if (!open)
                {
                    ConfigRead = false;
                    State = null;
                    LastLoad = null;
                }
                Changed?.Invoke();
            };
        }

        /// <summary>"Single", "Left" or "Right".</summary>
        public string Key { get; private set; }

        /// <summary>"Belt", "Left belt", "Right belt".</summary>
        public string Title { get; private set; }

        public ChannelSettings Settings { get; private set; }
        public BeltTensionerLink Link { get; private set; }
        public TestRunner Tester { get; private set; }

        /// <summary>Configuration, pending edits or the connection changed (any thread).</summary>
        public event Action Changed;

        /// <summary>A new status (CMD 16) or load (CMD 11) arrived (reader thread).</summary>
        public event Action StateUpdated;

        /// <summary>Message for the status display (any thread).</summary>
        public event Action<string> Message;

        /// <summary>Serial line for the log; polled status lines are filtered out (reader thread).</summary>
        public event Action<string> LogLine;

        public bool IsOpen => Link.IsOpen;

        // ------------------------------------------------------------------
        // Device data
        // ------------------------------------------------------------------

        public AxisConfigInfo DeviceAxis { get; private set; }
        public Dictionary<string, ControllerSetting> DeviceSettings { get; private set; } = new Dictionary<string, ControllerSetting>(StringComparer.OrdinalIgnoreCase);
        public bool ConfigRead { get; private set; }
        public bool SettingsSupported { get; private set; }

        public AxisStateInfo State { get; private set; }
        public int? LastLoad { get; private set; }
        public bool StateSupported { get; private set; } = true;
        private int stateRequestsWithoutReply;

        /// <summary>The controller runs a command that blocks it for a while (no status polling).</summary>
        public volatile bool BlockingCommand;

        /// <summary>A long running action of this channel (save, flash, measure, ...).</summary>
        public bool Busy { get; set; }

        // ------------------------------------------------------------------
        // Pending edits
        // ------------------------------------------------------------------

        public double? PendingPitch { get; private set; }
        public double? PendingRange { get; private set; }
        private readonly Dictionary<string, double> pendingSettings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public int PendingCount => pendingSettings.Count + (PendingPitch.HasValue ? 1 : 0) + (PendingRange.HasValue ? 1 : 0);

        public bool RestartPending => (DeviceAxis != null && DeviceAxis.RestartPending) || DeviceSettings.Values.Any(s => s.RestartPending);

        public static double DefaultOf(string key)
        {
            switch (key)
            {
                case "INVERT_TENSION": return 0;
                case "IDLE_POSITION": return 10;
                case "MIN_LIMIT": return 5;
                case "MAX_LIMIT": return 95;
                case "CENTER_AFTER_HOMING": return 0;
                case "IDLE_DELAY": return 10;
                case "HOME_AT_BOOT": return 0;
                case "HOMING_THRESHOLD": return 15;
                case "HOMING_BACKOFF": return 3.125;
                case "WEBGRAPHS": return 1;
                default: return 0;
            }
        }

        public double DeviceSetting(string key)
        {
            ControllerSetting s;
            return DeviceSettings.TryGetValue(key, out s) ? s.Value : DefaultOf(key);
        }

        /// <summary>Pending edit, else controller value, else Config.h default.</summary>
        public double EffectiveSetting(string key)
        {
            double v;
            return pendingSettings.TryGetValue(key, out v) ? v : DeviceSetting(key);
        }

        public double Pitch => PendingPitch ?? (DeviceAxis != null ? DeviceAxis.PitchMm : DefaultPitchMm);
        public double Range => PendingRange ?? (DeviceAxis != null ? DeviceAxis.RangeMm : DefaultRangeMm);

        public void SetSetting(string key, double value)
        {
            if (DeviceSettings.ContainsKey(key) && Math.Abs(DeviceSetting(key) - value) < 0.0005) pendingSettings.Remove(key);
            else pendingSettings[key] = value;
            Changed?.Invoke();
        }

        public void SetPitch(double value)
        {
            PendingPitch = (DeviceAxis != null && Math.Abs(DeviceAxis.PitchMm - value) < 0.0005) ? (double?)null : value;
            Changed?.Invoke();
        }

        public void SetRange(double value)
        {
            PendingRange = (DeviceAxis != null && Math.Abs(DeviceAxis.RangeMm - value) < 0.0005) ? (double?)null : value;
            Changed?.Invoke();
        }

        public void DiscardEdits()
        {
            PendingPitch = null;
            PendingRange = null;
            pendingSettings.Clear();
            Changed?.Invoke();
        }

        /// <summary>Game input (0..100 %) that keeps the actuator at its park position (motion test).</summary>
        public double IdleInputPercent
        {
            get
            {
                double min = DeviceSetting("MIN_LIMIT"), max = DeviceSetting("MAX_LIMIT"), idle = DeviceSetting("IDLE_POSITION");
                bool inverted = DeviceSetting("INVERT_TENSION") >= 0.5;
                double span = Math.Max(1, max - min);
                double input = inverted ? (max - (100 - idle)) / span * 100 : (idle - min) / span * 100;
                return Math.Max(0, Math.Min(100, input));
            }
        }

        private void Say(string text)
        {
            Message?.Invoke(Title + ": " + text);
        }

        // ------------------------------------------------------------------
        // Serial lines
        // ------------------------------------------------------------------

        private void Link_LineReceived(string line)
        {
            AxisStateInfo stateInfo;
            if (AxisConfigParser.TryParseState(line, out stateInfo))
            {
                if (stateInfo.AxisIndex == 0)
                {
                    State = stateInfo;
                    LastLoad = stateInfo.LoadPercent;
                    stateRequestsWithoutReply = 0;
                    StateUpdated?.Invoke();
                }
                return; // polled, not for the log
            }

            int axis, load, level;
            bool triggered;
            if (AxisConfigParser.TryParseSensor(line, out axis, out load, out level, out triggered))
            {
                if (axis == 0)
                {
                    LastLoad = load;
                    StateUpdated?.Invoke();
                }
                if (!StateSupported) return; // polled fallback, not for the log
            }
            LogLine?.Invoke(line);
        }

        // ------------------------------------------------------------------
        // Connection
        // ------------------------------------------------------------------

        public async Task<bool> ConnectAsync()
        {
            string port = Settings.ComPort;
            if (string.IsNullOrEmpty(port))
            {
                Say("select a COM port first.");
                return false;
            }
            try
            {
                Link.Open(port);
            }
            catch (Exception ex)
            {
                Say("connect failed: " + ex.Message);
                return false;
            }
            Say("connected to " + port + ".");
            await AfterConnectAsync(true);
            return true;
        }

        public void Disconnect()
        {
            Tester.Stop(park: true);
            Link.Close();
        }

        public async Task AfterConnectAsync(bool waitForGreeting)
        {
            try
            {
                if (waitForGreeting && !Link.GreetingReceived)
                {
                    // CP2102 boards reset when the port opens, native USB boards repeat their greeting
                    await Link.CollectAsync(() => { }, l => l.EndsWith("steppers enabled"), 3000, 0);
                }
                if (!Link.IsOpen) return;
                await Link.CollectAsync(() => Link.QueryFirmwareVersion(), l => Link.FirmwareVersion.Length > 0, 800, 0);
                await Link.CollectAsync(() => Link.QueryEnabledMotors(), l => l.StartsWith("Enabled motors:"), 800, 0);
                if (Link.FirmwareVersion.Length > 0) ComPortInfo.RememberKind(Link.PortName, DeviceKind.BeltTensioner);
                if (Link.EnabledMotors >= 2)
                {
                    Say("this controller drives " + Link.EnabledMotors + " actuators; the plugin uses one actuator per COM port (axis 1). Set NUM_ACTUATORS to 1 in the firmware.");
                }
                StateSupported = true;
                stateRequestsWithoutReply = 0;
                await ReadAllAsync();
            }
            catch (Exception ex)
            {
                Say("query failed: " + ex.Message);
            }
        }

        /// <summary>Waits until the port is present again and connects (after flashing or a restart).</summary>
        public async Task<bool> ReconnectAsync(int timeoutMs)
        {
            string port = Settings.ComPort;
            await Task.Delay(1500);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (ComPortInfo.Find(port) != null)
                {
                    try
                    {
                        Link.Open(port);
                        Say("reconnected to " + port + ".");
                        await AfterConnectAsync(true);
                        return true;
                    }
                    catch (Exception)
                    {
                        // driver not ready yet
                    }
                }
                await Task.Delay(500);
            }
            Say("could not reconnect to " + port + ". Connect manually.");
            return false;
        }

        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------

        /// <summary>Reads pitch / range (CONFIG) and the behaviour settings (SETTINGS).</summary>
        public async Task ReadAllAsync()
        {
            if (!Link.IsOpen) return;
            LogLine?.Invoke("> CONFIG");
            var lines = await Link.RequestAsync("CONFIG", l => l.Contains("RESET_CONFIG"), 2500, 600);
            var axes = AxisConfigParser.Parse(lines);
            if (axes.Count == 0)
            {
                Say("no configuration received. Is this the belt tensioner firmware?");
                return;
            }
            DeviceAxis = axes.FirstOrDefault(a => a.Axis == 1) ?? axes[0];

            LogLine?.Invoke("> SETTINGS");
            var settingLines = await Link.RequestAsync("SETTINGS", l => l.StartsWith("End of settings"), 1500, 500);
            var parsed = AxisConfigParser.ParseSettings(settingLines);
            SettingsSupported = parsed.Count > 0;
            DeviceSettings = parsed.ToDictionary(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase);

            ConfigRead = true;
            PendingPitch = null;
            PendingRange = null;
            pendingSettings.Clear();
            Changed?.Invoke();
        }

        private static string FormatValue(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Writes all pending edits. Returns false if the controller rejected a value.</summary>
        public async Task<bool> SaveAsync()
        {
            if (!Link.IsOpen || PendingCount == 0) return true;

            var commands = new List<string>();
            if (PendingPitch.HasValue) commands.Add("SET_PITCH 1 " + FormatValue(PendingPitch.Value));
            if (PendingRange.HasValue) commands.Add("SET_RANGE 1 " + FormatValue(PendingRange.Value));
            if (SettingsSupported)
            {
                foreach (var kv in pendingSettings) commands.Add("SET " + kv.Key + " " + FormatValue(kv.Value));
            }

            foreach (var cmd in commands)
            {
                LogLine?.Invoke("> " + cmd);
                var reply = await Link.RequestAsync(cmd, l => l.Contains("saved") || l.StartsWith("Invalid") || l.StartsWith("Usage") || l.StartsWith("Error"), 1500, 300);
                if (!reply.Any(l => l.Contains("saved")))
                {
                    Say("controller rejected " + cmd + (reply.Count > 0 ? " (" + reply.Last() + ")" : ""));
                    return false;
                }
            }
            Say("saved " + commands.Count + " value" + (commands.Count == 1 ? "" : "s") + ".");
            await ReadAllAsync();
            return true;
        }

        /// <summary>RESTART, then the port is reopened (reopening alone resets CP2102 boards).</summary>
        public async Task RestartAsync()
        {
            Tester.Stop(park: false);
            try
            {
                LogLine?.Invoke("> RESTART");
                Link.SendLine("RESTART");
                await Task.Delay(300);
            }
            catch (Exception)
            {
            }
            Link.Close();
            Say("restarting...");
            await ReconnectAsync(12000);
            if (Link.IsOpen && RestartPending)
            {
                Say("the controller did not restart (firmware without RESTART). Press RST or unplug USB.");
            }
        }

        public async Task ResetConfigAsync()
        {
            LogLine?.Invoke("> RESET_CONFIG");
            await Link.RequestAsync("RESET_CONFIG", null, 1500, 400);
            await ReadAllAsync();
        }

        private static readonly Regex MeasuredRangeLine = new Regex(@"^Measured working range axis 1: ([\d.]+) mm", RegexOptions.Compiled);

        /// <summary>MEASURE_RANGE 1: homing to both end stops. Returns the range in mm or null.</summary>
        public async Task<double?> MeasureRangeAsync()
        {
            Tester.Stop(park: false);
            const string done = "Measured working range axis 1";
            const string failed = "Working range measurement axis 1";
            LogLine?.Invoke("> MEASURE_RANGE 1");
            var ack = await Link.RequestAsync("MEASURE_RANGE 1",
                l => l.StartsWith("Measuring working range") || l.StartsWith(done) || l.StartsWith(failed) || l.StartsWith("Usage"),
                2500, 0);
            if (ack.Count == 0)
            {
                Say("no answer to MEASURE_RANGE. Flash the latest firmware to auto-detect the working range.");
                return null;
            }
            if (ack.Any(l => l.StartsWith("Usage")))
            {
                Say(ack.Last());
                return null;
            }

            Say("measuring the working range...");
            var lines = ack.Any(l => l.StartsWith(done) || l.StartsWith(failed))
                ? ack
                : await Link.CollectAsync(() => { }, l => l.StartsWith(done) || l.StartsWith(failed), 120000, 0);

            string result = lines.FirstOrDefault(l => l.StartsWith(done));
            var m = result != null ? MeasuredRangeLine.Match(result) : null;
            double measured;
            if (m == null || !m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out measured))
            {
                Say(lines.FirstOrDefault(l => l.StartsWith(failed)) ?? "no measurement result (timeout). Check the servo power and the homing load threshold.");
                return null;
            }
            Say("measured working range " + measured.ToString("0.0", CultureInfo.InvariantCulture) + " mm.");
            return measured;
        }

        // ------------------------------------------------------------------
        // Servo
        // ------------------------------------------------------------------

        public async Task<bool> FlashServoAsync()
        {
            Tester.Stop(park: false);
            BlockingCommand = true;
            try
            {
                LogLine?.Invoke("> FLASH_SERVO 1");
                Say("flashing the servo parameters (see Serial Logs)...");
                var reply = await Link.RequestAsync("FLASH_SERVO 1",
                    l => l.Contains("[Flash] Done!") || l.Contains("[Flash] Warning") || l.Contains("is not enabled"), 120000, 15000);
                bool ok = reply.Any(l => l.Contains("[Flash] Done!"));
                Say(ok ? "servo parameters saved. Power cycle the servo." : "servo flash did not report success. Check the servo power and the RS232 wiring.");
                return ok;
            }
            finally
            {
                BlockingCommand = false;
            }
        }

        /// <summary>
        /// DIFF_SERVO: compares the servo registers with the tuned table of the firmware.
        /// Returns null if the controller did not answer.
        /// </summary>
        public async Task<ServoCheckResult> DiffServoAsync()
        {
            BlockingCommand = true;
            try
            {
                LogLine?.Invoke("> DIFF_SERVO 1");
                Say("checking the servo registers...");
                var reply = await Link.RequestAsync("DIFF_SERVO 1",
                    l => l.Contains("read errors.") || l.Contains("is not enabled"), 60000, 8000);
                var result = ServoCheckResult.Parse(reply);
                if (result == null)
                {
                    Say("no answer to DIFF_SERVO.");
                }
                else
                {
                    Say(result.SummaryText);
                }
                return result;
            }
            finally
            {
                BlockingCommand = false;
            }
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        public bool Send(string asciiCommand)
        {
            if (!Link.IsOpen)
            {
                Say("not connected.");
                return false;
            }
            try
            {
                LogLine?.Invoke("> " + asciiCommand);
                Link.SendLine(asciiCommand);
                return true;
            }
            catch (Exception ex)
            {
                Say("send failed: " + ex.Message);
                return false;
            }
        }

        public bool Home()
        {
            if (!Link.IsOpen) return false;
            try
            {
                Link.Recalibrate();
                Say("homing.");
                return true;
            }
            catch (Exception ex)
            {
                Say("send failed: " + ex.Message);
                return false;
            }
        }

        public bool Park()
        {
            if (!Link.IsOpen) return false;
            Tester.Stop(park: false);
            try
            {
                Link.ParkNow();
                Say("parking.");
                return true;
            }
            catch (Exception ex)
            {
                Say("send failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Requests the status (CMD 16), with old firmware only the servo load (CMD 11).</summary>
        public void PollStatus()
        {
            if (!Link.IsOpen || BlockingCommand) return;
            try
            {
                if (StateSupported)
                {
                    Link.DumpAxisState(0);
                    if (++stateRequestsWithoutReply > 8 && State == null)
                    {
                        StateSupported = false;
                        Say("this firmware has no status query (CMD 16): only the servo load is shown.");
                    }
                }
                else
                {
                    Link.DumpSensor(0);
                }
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // Display helpers
        // ------------------------------------------------------------------

        /// <summary>Short status text, e.g. "ready", "homing...", "not connected".</summary>
        public string StatusText
        {
            get
            {
                if (!Link.IsOpen) return "not connected";
                if (State != null) return State.StatusText;
                if (!ConfigRead) return "connecting...";
                return "connected";
            }
        }

        /// <summary>0 = ok, 1 = attention, 2 = error, -1 = not connected.</summary>
        public int Severity
        {
            get
            {
                if (!Link.IsOpen) return -1;
                if (State != null) return State.Severity;
                return ConfigRead ? 0 : 1;
            }
        }

        /// <summary>"Load 12 % · 24.1 V · pos 55.0 %"</summary>
        public string TelemetryText
        {
            get
            {
                if (!Link.IsOpen) return "";
                var parts = new List<string>();
                if (LastLoad.HasValue) parts.Add("load " + LastLoad.Value + " %");
                if (State != null)
                {
                    parts.Add(State.Voltage.ToString("0.0", CultureInfo.InvariantCulture) + " V");
                    if (State.Ready || State.IsHoming) parts.Add("pos " + State.PositionPercent.ToString("0.0", CultureInfo.InvariantCulture) + " %");
                }
                return string.Join("  ·  ", parts);
            }
        }

        /// <summary>Measured carriage position in % of the working range, NaN if unknown.</summary>
        public double ActualPositionPercent => State != null && (State.Ready || State.IsHoming) ? State.PositionPercent : double.NaN;
    }
}
