using DiyBeltTensioner.Core;
using Microsoft.Win32;
using SimHub.Plugins.Styles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DiyBeltTensioner.UI
{
    public partial class BeltTensionerControl : UserControl
    {
        private const int MaxLogChars = 200000;
        private const int MaxStatusLines = 30;

        // Defaults of Config.h, shown until the controller has been read
        private const double DefaultPitchMm = 10;
        private const double DefaultRangeMm = 60;

        private static readonly string[] CommonPitches = { "4", "5", "8", "10", "16", "20", "25" };

        private readonly BeltTensionerPlugin plugin;
        private readonly BeltTensionerSettings settings;
        private readonly BeltTensionerLink link;
        private readonly TestRunner tester;

        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private readonly DispatcherTimer logTimer;
        private readonly DispatcherTimer loadTimer;
        private readonly DispatcherTimer liveTimer;
        private readonly List<string> statusLines = new List<string>();

        private bool loading = true;
        private bool applying;
        private bool busy;
        private volatile bool liveLoadActive;
        private FirmwareRelease latestRelease;
        private FirmwareRelease pendingPluginUpdate;
        private bool loadingReleases;

        // Values read from the controller
        private List<AxisConfigInfo> deviceAxes = new List<AxisConfigInfo>();
        private Dictionary<string, ControllerSetting> deviceSettings = new Dictionary<string, ControllerSetting>();
        private bool configRead;
        private bool settingsSupported;

        // Edits not saved to the controller yet
        private readonly double?[] pendingPitch = new double?[2];
        private readonly double?[] pendingRange = new double?[2];
        private readonly Dictionary<string, double> pendingSettings = new Dictionary<string, double>();

        private readonly int[] lastLoad = { int.MinValue, int.MinValue };

        private readonly LabeledSlider[] settingSliders;
        private readonly SHToggleButton[] settingToggles;

        public BeltTensionerControl(BeltTensionerPlugin plugin)
        {
            this.plugin = plugin;
            settings = plugin.Settings;
            link = plugin.Link;
            tester = plugin.Tester;

            InitializeComponent();

            settingSliders = new[] { SliderIdle, SliderMin, SliderMax, SliderIdleDelay, SliderHomingThreshold, SliderBackoff };
            settingToggles = new[] { ToggleInvert, ToggleCenter, ToggleHomeAtBoot };

            logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            logTimer.Tick += (s, e) => FlushLog();
            logTimer.Start();

            loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            loadTimer.Tick += LoadTimer_Tick;

            liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            liveTimer.Tick += (s, e) =>
            {
                Travel.LivePct = tester.IsRunning ? tester.CurrentPercent : double.NaN;
                Travel.Refresh();
            };

            foreach (var slider in settingSliders)
            {
                slider.ValueCommitted += SettingSlider_ValueCommitted;
            }
            SliderRange1.ValueCommitted += (s, v) => OnRangeEdited(0, v);
            SliderRange2.ValueCommitted += (s, v) => OnRangeEdited(1, v);
            foreach (var slider in new[] { SliderHold, SliderSineCenter, SliderSineAmplitude, SliderSineFrequency, SliderPulseBase, SliderPulsePeak, SliderPulseDuration, SliderPulseInterval })
            {
                slider.ValueCommitted += (s, v) => ApplyTestParameters();
            }
            ComboPitch1.ItemsSource = CommonPitches;
            ComboPitch2.ItemsSource = CommonPitches;
            ComboPort.SelectionChanged += (s, e) => UpdateFlashPortText();

            LoadSettingsIntoUi();
            ShowDeviceValues();
            loading = false;

            link.LineReceived += Link_LineReceived;
            link.ConnectionChanged += open => Dispatcher.BeginInvoke(new Action(() => OnConnectionChanged(open)));
            tester.Failed += msg => Dispatcher.BeginInvoke(new Action(() =>
            {
                Status("Test stopped: " + msg);
                UpdateTestUi();
            }));

            var ignoredRefresh = RefreshPortsAsync();
            UpdateConnectionUi();
            UpdateMotionLayoutUi();
            UpdateFirmwareSourceUi();
            UpdateTestModeUi();
            UpdateTestUi();
            UpdateEnabledState();

            TextPluginVersion.Text = "Plugin version: " + PluginUpdater.CurrentVersion;
            var ignoredReleases = LoadReleasesAsync(userInitiated: false);

            if (link.IsOpen)
            {
                var ignored = AfterConnectAsync(waitForGreeting: false);
            }
        }

        // ==================================================================
        // Plugin settings <-> UI
        // ==================================================================

        private void LoadSettingsIntoUi()
        {
            ToggleAutoConnect.IsChecked = settings.AutoConnect;
            ToggleCheckUpdates.IsChecked = settings.CheckPluginUpdates;

            TextRepository.Text = settings.GitHubRepository;
            TextLocalFirmware.Text = settings.LocalFirmwarePath;
            switch (settings.FirmwareSource)
            {
                case 1: RadioSourceZip.IsChecked = true; break;
                case 2: RadioSourceFolder.IsChecked = true; break;
                default: RadioSourceGitHub.IsChecked = true; break;
            }

            ComboTestMode.SelectedIndex = Math.Max(0, Math.Min(2, settings.TestMode));
            SliderHold.Value = settings.TestPositionPct;
            SliderSineCenter.Value = settings.TestSineCenterPct;
            SliderSineAmplitude.Value = settings.TestSineAmplitudePct;
            SliderSineFrequency.Value = settings.TestSineFrequencyHz;
            SliderPulseBase.Value = settings.TestPulseBasePct;
            SliderPulsePeak.Value = settings.TestPulsePeakPct;
            SliderPulseDuration.Value = settings.TestPulseDurationMs;
            SliderPulseInterval.Value = settings.TestPulseIntervalMs;
            ToggleTestAxis1.IsChecked = settings.TestAxis1;
            ToggleTestAxis2.IsChecked = settings.TestAxis2;
            TextTestFrequency.Text = settings.StepperFrequency.ToString(CultureInfo.InvariantCulture);
            TextTestAcceleration.Text = settings.StepperAcceleration.ToString(CultureInfo.InvariantCulture);
            TextMotionFrequency.Text = settings.StepperFrequency.ToString(CultureInfo.InvariantCulture);
            TextMotionAcceleration.Text = settings.StepperAcceleration.ToString(CultureInfo.InvariantCulture);

            if (settings.MotionDual) RadioMotionDual.IsChecked = true; else RadioMotionSingle.IsChecked = true;
            ComboMotionPort1.Text = settings.MotionDual ? settings.MotionPortLeft : settings.MotionPortSingle;
            ComboMotionPort2.Text = settings.MotionPortRight;
            ToggleMotionReverse1.IsChecked = settings.MotionDual ? settings.MotionReverseLeft : settings.MotionReverseSingle;
            ToggleMotionReverse2.IsChecked = settings.MotionReverseRight;
        }

        private void SaveSettings()
        {
            if (loading) return;
            plugin.SaveSettings();
        }

        // ==================================================================
        // Log & status
        // ==================================================================

        private void Log(string text)
        {
            logQueue.Enqueue(text);
        }

        /// <summary>Message for the status card (and the log).</summary>
        private void Status(string text)
        {
            Log("[plugin] " + text);
            statusLines.Add(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text);
            while (statusLines.Count > MaxStatusLines) statusLines.RemoveAt(0);
            TextStatus.Text = string.Join(Environment.NewLine, statusLines);
            TextStatus.ScrollToEnd();
        }

        private void FlushLog()
        {
            if (logQueue.IsEmpty) return;
            var sb = new StringBuilder();
            string line;
            while (logQueue.TryDequeue(out line))
            {
                sb.AppendLine(line);
            }
            TextLog.AppendText(sb.ToString());
            if (TextLog.Text.Length > MaxLogChars)
            {
                TextLog.Text = TextLog.Text.Substring(TextLog.Text.Length - MaxLogChars / 2);
            }
            TextLog.ScrollToEnd();
        }

        private void Link_LineReceived(string line)
        {
            int axis, load, level;
            bool triggered;
            if (AxisConfigParser.TryParseSensor(line, out axis, out load, out level, out triggered))
            {
                Dispatcher.BeginInvoke(new Action(() => ShowLoad(axis, load, level, triggered)));
                if (liveLoadActive) return; // don't flood the log while polling
            }
            Log(line);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TextLog.Clear();
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            SendConsoleCommand();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            SendAscii("HELP");
        }

        private void BtnSettingsCmd_Click(object sender, RoutedEventArgs e)
        {
            SendAscii("SETTINGS");
        }

        private void BtnShowLog_Click(object sender, RoutedEventArgs e)
        {
            MainTabs.SelectedItem = TabLog;
        }

        private void TextCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendConsoleCommand();
                e.Handled = true;
            }
        }

        private void SendConsoleCommand()
        {
            string cmd = TextCommand.Text.Trim();
            if (cmd.Length == 0) return;
            if (SendAscii(cmd)) TextCommand.Clear();
        }

        private bool SendAscii(string cmd)
        {
            if (!EnsureConnected()) return false;
            try
            {
                Log("> " + cmd);
                link.SendLine(cmd);
                return true;
            }
            catch (Exception ex)
            {
                Status("Send failed: " + ex.Message);
                return false;
            }
        }

        private bool SendBinary(Action action, string description)
        {
            if (!EnsureConnected()) return false;
            try
            {
                Log("> " + description);
                action();
                return true;
            }
            catch (Exception ex)
            {
                Status("Send failed: " + ex.Message);
                return false;
            }
        }

        private bool EnsureConnected()
        {
            if (link.IsOpen) return true;
            Status("Not connected. Select the COM port and click Connect.");
            return false;
        }

        // ==================================================================
        // Ports & connection
        // ==================================================================

        private List<ComPortInfo> currentPorts = new List<ComPortInfo>();
        private bool refreshingPorts;

        private async Task RefreshPortsAsync()
        {
            if (refreshingPorts) return;
            refreshingPorts = true;
            try
            {
                var ports = await Task.Run(() => ComPortInfo.GetPresentPorts(true));
                currentPorts = ports;

                string selected = (ComboPort.SelectedItem as ComPortInfo)?.PortName;
                if (string.IsNullOrEmpty(selected)) selected = link.IsOpen ? link.PortName : settings.ComPort;

                ComboPort.ItemsSource = ports;
                ComboPort.SelectedItem = ports.FirstOrDefault(p => string.Equals(p.PortName, selected, StringComparison.OrdinalIgnoreCase))
                                         ?? ports.FirstOrDefault(p => p.Kind == DeviceKind.BeltTensioner)
                                         ?? ports.FirstOrDefault();

                // Motion tab: belt tensioner ports first
                var names = ports.OrderBy(p => p.Kind == DeviceKind.BeltTensioner ? 0 : 1).Select(p => p.PortName).ToList();
                string m1 = ComboMotionPort1.Text;
                string m2 = ComboMotionPort2.Text;
                ComboMotionPort1.ItemsSource = names;
                ComboMotionPort2.ItemsSource = names;
                var belts = ports.Where(p => p.Kind == DeviceKind.BeltTensioner).Select(p => p.PortName).ToList();
                ComboMotionPort1.Text = !string.IsNullOrEmpty(m1) ? m1 : (belts.FirstOrDefault() ?? selected ?? "");
                ComboMotionPort2.Text = !string.IsNullOrEmpty(m2) ? m2 : (belts.Skip(1).FirstOrDefault() ?? "");

                UpdateFlashPortText();
            }
            catch (Exception ex)
            {
                Log("Port scan failed: " + ex.Message);
            }
            finally
            {
                refreshingPorts = false;
            }
        }

        private void ComboPort_DropDownOpened(object sender, EventArgs e)
        {
            if (!link.IsOpen)
            {
                var ignored = RefreshPortsAsync();
            }
        }

        private void UpdateFlashPortText()
        {
            var port = ComboPort.SelectedItem as ComPortInfo;
            TextFlashPort.Text = port != null ? port.DisplayName : "- (select it on the TENSIONER tab)";
        }

        private async void BtnScanPorts_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            var candidates = currentPorts.Where(p =>
                    (p.Kind == DeviceKind.UsbUart || p.Kind == DeviceKind.Unknown || p.Kind == DeviceKind.EspNativeUsb) &&
                    !(link.IsOpen && string.Equals(link.PortName, p.PortName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 0)
            {
                Status("No unidentified USB-UART ports.");
                return;
            }

            var answer = MessageBox.Show(
                "Open " + string.Join(", ", candidates.Select(p => p.PortName)) + " briefly to look for the belt tensioner firmware?\n\n" +
                "Opening a CP2102 port resets the ESP32 behind it (e.g. a pedal restarts). Ports used by SimHub or other programs are skipped.",
                "Identify USB-UART boards", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            busy = true;
            BtnScanPorts.IsEnabled = false;
            try
            {
                foreach (var port in candidates)
                {
                    Status("Probing " + port.PortName + "...");
                    var kind = await DeviceScanner.ProbeAsync(port.PortName);
                    ComPortInfo.RememberKind(port.PortName, kind == DeviceKind.Unknown ? DeviceKind.Unknown : kind);
                    Status(port.PortName + ": " + (kind == DeviceKind.BeltTensioner ? "belt tensioner firmware found" : kind == DeviceKind.FfbPedal ? "FFB pedal" : "no belt tensioner firmware"));
                }
                await RefreshPortsAsync();
            }
            finally
            {
                busy = false;
                BtnScanPorts.IsEnabled = true;
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (link.IsOpen)
            {
                tester.Stop(park: true);
                link.Close();
                return;
            }
            await ConnectAsync();
        }

        private async Task<bool> ConnectAsync()
        {
            var port = ComboPort.SelectedItem as ComPortInfo;
            if (port == null)
            {
                Status("No COM port selected.");
                return false;
            }
            if (port.IsPedalDevice)
            {
                var answer = MessageBox.Show(port.PortName + " is a DIY FFB Pedal device (" + port.KindText + "). Connect anyway?", "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return false;
            }

            try
            {
                link.Open(port.PortName);
            }
            catch (Exception ex)
            {
                Status("Connect failed: " + ex.Message);
                return false;
            }

            settings.ComPort = port.PortName;
            SaveSettings();
            Status("Connected to " + port.PortName);
            await AfterConnectAsync(true);
            return true;
        }

        private async Task AfterConnectAsync(bool waitForGreeting)
        {
            try
            {
                if (waitForGreeting && !link.GreetingReceived)
                {
                    // CP2102 boards reset when the port opens, native USB boards repeat their greeting.
                    await link.CollectAsync(() => { }, l => l.EndsWith("steppers enabled"), 3000, 0);
                }
                if (!link.IsOpen) return;
                await link.CollectAsync(() => link.QueryFirmwareVersion(), l => link.FirmwareVersion.Length > 0, 800, 0);
                await link.CollectAsync(() => link.QueryEnabledMotors(), l => l.StartsWith("Enabled motors:"), 800, 0);
                if (!string.IsNullOrEmpty(link.PortName) && link.FirmwareVersion.Length > 0)
                {
                    ComPortInfo.RememberKind(link.PortName, DeviceKind.BeltTensioner);
                }
                UpdateConnectionUi();
                await ReadAllAsync();
            }
            catch (Exception ex)
            {
                Status("Query failed: " + ex.Message);
            }
        }

        private void OnConnectionChanged(bool open)
        {
            if (!open)
            {
                Status("Disconnected.");
                ToggleLiveLoad.IsChecked = false;
                liveLoadActive = false;
                loadTimer.Stop();
                configRead = false;
            }
            UpdateConnectionUi();
            UpdateTestUi();
            UpdateEnabledState();
        }

        private void UpdateConnectionUi()
        {
            bool open = link.IsOpen;
            BtnConnect.Content = open ? "Disconnect" : "Connect";
            ComboPort.IsEnabled = !open;
            BtnScanPorts.IsEnabled = !busy;
            InfoConnection.Text = open ? "Connected (" + link.PortName + ")" : "Disconnected";
            InfoProtocol.Text = open && link.FirmwareVersion.Length > 0 ? link.FirmwareVersion : "-";
            InfoActuators.Text = open && link.EnabledMotors > 0 ? link.EnabledMotors.ToString(CultureInfo.InvariantCulture) : "-";
            if (!open) InfoSettings.Text = "-";
            if (!open) InfoLoad.Text = "-";

            bool twoAxes = link.EnabledMotors >= 2;
            PanelAxis2.Visibility = twoAxes ? Visibility.Visible : Visibility.Hidden;
            ToggleTestAxis2.IsEnabled = twoAxes;
        }

        private void ToggleAutoConnect_Click(object sender, RoutedEventArgs e)
        {
            settings.AutoConnect = ToggleAutoConnect.IsChecked == true;
            SaveSettings();
        }

        // ==================================================================
        // Read configuration
        // ==================================================================

        private async void BtnReadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            await ReadAllAsync();
        }

        /// <summary>Reads pitch / range (CONFIG) and the behaviour settings (SETTINGS).</summary>
        private async Task ReadAllAsync()
        {
            if (!link.IsOpen) return;

            Log("> CONFIG");
            var lines = await link.RequestAsync("CONFIG", l => l.Contains("RESET_CONFIG"), 2500, 600);
            var axes = AxisConfigParser.Parse(lines);
            if (axes.Count == 0)
            {
                Status("No configuration received. Is this the belt tensioner firmware?");
                return;
            }
            deviceAxes = axes;

            Log("> SETTINGS");
            var settingLines = await link.RequestAsync("SETTINGS", l => l.StartsWith("End of settings"), 1500, 500);
            var parsed = AxisConfigParser.ParseSettings(settingLines);
            settingsSupported = parsed.Count > 0;
            deviceSettings = parsed.ToDictionary(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase);
            InfoSettings.Text = settingsSupported ? "supported" : "pitch & range only";
            if (!settingsSupported)
            {
                Status("This firmware only supports pitch & range. Flash the latest firmware for direction, positions, idle and homing settings.");
            }

            configRead = true;
            ClearPending();
            ShowDeviceValues();
            UpdateEnabledState();
            Status("Configuration read.");
        }

        private void ClearPending()
        {
            pendingPitch[0] = pendingPitch[1] = null;
            pendingRange[0] = pendingRange[1] = null;
            pendingSettings.Clear();
        }

        private double DeviceSetting(string key, double fallback)
        {
            ControllerSetting s;
            return deviceSettings.TryGetValue(key, out s) ? s.Value : fallback;
        }

        /// <summary>Value shown in the UI: pending edit, else controller value, else Config.h default.</summary>
        private double EffectiveSetting(string key, double fallback)
        {
            double v;
            return pendingSettings.TryGetValue(key, out v) ? v : DeviceSetting(key, fallback);
        }

        private static double DefaultOf(string key)
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
                default: return 0;
            }
        }

        private double AxisPitch(int index)
        {
            if (pendingPitch[index].HasValue) return pendingPitch[index].Value;
            var a = deviceAxes.FirstOrDefault(x => x.Axis == index + 1);
            return a != null ? a.PitchMm : DefaultPitchMm;
        }

        private double AxisRange(int index)
        {
            if (pendingRange[index].HasValue) return pendingRange[index].Value;
            var a = deviceAxes.FirstOrDefault(x => x.Axis == index + 1);
            return a != null ? a.RangeMm : DefaultRangeMm;
        }

        /// <summary>Puts the effective values (controller + pending edits) into the controls.</summary>
        private void ShowDeviceValues()
        {
            applying = true;
            try
            {
                ComboPitch1.Text = AxisPitch(0).ToString("0.##", CultureInfo.InvariantCulture);
                ComboPitch2.Text = AxisPitch(1).ToString("0.##", CultureInfo.InvariantCulture);
                SliderRange1.Value = AxisRange(0);
                SliderRange2.Value = AxisRange(1);

                foreach (var slider in settingSliders)
                {
                    string key = (string)slider.Tag;
                    slider.Value = EffectiveSetting(key, DefaultOf(key));
                }
                foreach (var toggle in settingToggles)
                {
                    string key = (string)toggle.Tag;
                    toggle.IsChecked = EffectiveSetting(key, DefaultOf(key)) >= 0.5;
                }
            }
            finally
            {
                applying = false;
            }
            UpdateTravel();
            UpdateSaveState();
        }

        private void UpdateEnabledState()
        {
            bool canEdit = link.IsOpen && configRead;
            foreach (var c in new UIElement[] { ComboPitch1, ComboPitch2, SliderRange1, SliderRange2, BtnSave, BtnDiscard, BtnResetConfig })
            {
                c.IsEnabled = canEdit;
            }
            bool canEditSettings = canEdit && settingsSupported;
            foreach (var slider in settingSliders) slider.IsEnabled = canEditSettings;
            foreach (var toggle in settingToggles) toggle.IsEnabled = canEditSettings;
            UpdateSaveState();
        }

        private int PendingCount
        {
            get
            {
                return pendingSettings.Count + pendingPitch.Count(p => p.HasValue) + pendingRange.Count(p => p.HasValue);
            }
        }

        private void UpdateSaveState()
        {
            if (!link.IsOpen)
            {
                TextSaveState.Text = "Connect to read the configuration from the controller.";
                return;
            }
            if (!configRead)
            {
                TextSaveState.Text = "Reading the configuration...";
                return;
            }
            int pending = PendingCount;
            bool restartPending = deviceAxes.Any(a => a.RestartPending) || deviceSettings.Values.Any(s => s.RestartPending);
            if (pending > 0)
            {
                TextSaveState.Text = pending + " unsaved change" + (pending == 1 ? "" : "s") + ".";
            }
            else if (restartPending)
            {
                TextSaveState.Text = "Saved values are applied after a restart of the controller (Restart).";
            }
            else
            {
                TextSaveState.Text = settingsSupported ? "In sync with the controller." : "In sync. Update the firmware for more settings.";
            }
        }

        // ==================================================================
        // Edits
        // ==================================================================

        private void SettingSlider_ValueCommitted(object sender, double value)
        {
            if (applying || loading) return;
            var slider = (LabeledSlider)sender;
            SetPending((string)slider.Tag, value);
        }

        private void SettingToggle_Click(object sender, RoutedEventArgs e)
        {
            if (applying || loading) return;
            var toggle = (SHToggleButton)sender;
            SetPending((string)toggle.Tag, toggle.IsChecked == true ? 1 : 0);
        }

        private void SetPending(string key, double value)
        {
            double device = DeviceSetting(key, DefaultOf(key));
            if (Math.Abs(device - value) < 0.0005 && deviceSettings.ContainsKey(key))
            {
                pendingSettings.Remove(key);
            }
            else
            {
                pendingSettings[key] = value;
            }
            UpdateTravel();
            UpdateSaveState();
        }

        private void OnRangeEdited(int index, double value)
        {
            if (applying || loading) return;
            var a = deviceAxes.FirstOrDefault(x => x.Axis == index + 1);
            pendingRange[index] = (a != null && Math.Abs(a.RangeMm - value) < 0.0005) ? (double?)null : value;
            UpdateTravel();
            UpdateSaveState();
        }

        private void Pitch_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (applying || loading) return;
            var combo = (ComboBox)sender;
            var item = combo.SelectedItem as string;
            if (item != null) CommitPitch(combo, item);
        }

        private void Pitch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (applying || loading) return;
            var combo = (ComboBox)sender;
            CommitPitch(combo, combo.Text);
        }

        private void Pitch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || applying || loading) return;
            var combo = (ComboBox)sender;
            CommitPitch(combo, combo.Text);
        }

        private void CommitPitch(ComboBox combo, string text)
        {
            int index = combo == ComboPitch2 ? 1 : 0;
            double value;
            if (!TryParseNumber(text, 0.5, 50, out value))
            {
                Status("Spindle pitch must be 0.5 - 50 mm.");
                applying = true;
                combo.Text = AxisPitch(index).ToString("0.##", CultureInfo.InvariantCulture);
                applying = false;
                return;
            }
            var a = deviceAxes.FirstOrDefault(x => x.Axis == index + 1);
            pendingPitch[index] = (a != null && Math.Abs(a.PitchMm - value) < 0.0005) ? (double?)null : value;
            UpdateSaveState();
        }

        private static bool TryParseNumber(string text, double min, double max, out double value)
        {
            text = (text ?? "").Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        private void BtnDiscard_Click(object sender, RoutedEventArgs e)
        {
            ClearPending();
            ShowDeviceValues();
        }

        private void UpdateTravel()
        {
            Travel.RangeMm = AxisRange(0);
            Travel.MinPct = EffectiveSetting("MIN_LIMIT", 5);
            Travel.MaxPct = EffectiveSetting("MAX_LIMIT", 95);
            Travel.IdlePct = EffectiveSetting("IDLE_POSITION", 10);
            Travel.Inverted = EffectiveSetting("INVERT_TENSION", 0) >= 0.5;
            Travel.CenterAfterHoming = EffectiveSetting("CENTER_AFTER_HOMING", 0) >= 0.5;
            Travel.Refresh();
            UpdateTestIdleInput();
        }

        /// <summary>Game input that keeps an untested axis at the park position of the running controller.</summary>
        private void UpdateTestIdleInput()
        {
            double min = DeviceSetting("MIN_LIMIT", 5), max = DeviceSetting("MAX_LIMIT", 95), idle = DeviceSetting("IDLE_POSITION", 10);
            bool inverted = DeviceSetting("INVERT_TENSION", 0) >= 0.5;
            double span = Math.Max(1, max - min);
            double input = inverted ? (max - (100 - idle)) / span * 100 : (idle - min) / span * 100;
            tester.IdleInputPercent = Math.Max(0, Math.Min(100, input));
        }

        // ==================================================================
        // Save / restart / reset
        // ==================================================================

        private static string FormatValue(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected() || busy) return;

            // Commit a pitch that is still being typed
            Pitch_LostFocus(ComboPitch1, null);
            if (link.EnabledMotors >= 2) Pitch_LostFocus(ComboPitch2, null);

            var commands = new List<string>();
            int axes = Math.Max(1, Math.Min(2, link.EnabledMotors));
            for (int i = 0; i < axes; i++)
            {
                if (pendingPitch[i].HasValue) commands.Add("SET_PITCH " + (i + 1) + " " + FormatValue(pendingPitch[i].Value));
                if (pendingRange[i].HasValue) commands.Add("SET_RANGE " + (i + 1) + " " + FormatValue(pendingRange[i].Value));
            }
            if (settingsSupported)
            {
                foreach (var kv in pendingSettings)
                {
                    commands.Add("SET " + kv.Key + " " + FormatValue(kv.Value));
                }
            }

            if (commands.Count == 0)
            {
                Status("Nothing to save.");
                return;
            }

            if (EffectiveSetting("IDLE_POSITION", 10) < EffectiveSetting("MIN_LIMIT", 5))
            {
                Status("Note: the park position is below the min. limit.");
            }

            busy = true;
            BtnSave.IsEnabled = false;
            try
            {
                foreach (var cmd in commands)
                {
                    Log("> " + cmd);
                    var reply = await link.RequestAsync(cmd, l => l.Contains("saved") || l.StartsWith("Invalid") || l.StartsWith("Usage") || l.StartsWith("Error"), 1500, 300);
                    if (!reply.Any(l => l.Contains("saved")))
                    {
                        Status("Controller rejected: " + cmd + (reply.Count > 0 ? " (" + reply.Last() + ")" : ""));
                        return;
                    }
                }
                Status("Saved " + commands.Count + " value" + (commands.Count == 1 ? "" : "s") + ".");
                await ReadAllAsync();

                bool restartPending = deviceAxes.Any(a => a.RestartPending) || deviceSettings.Values.Any(s => s.RestartPending);
                if (restartPending)
                {
                    var answer = MessageBox.Show("The new values are saved and applied after a restart of the controller.\n\nRestart the controller now?",
                        "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Yes)
                    {
                        await RestartControllerAsync();
                    }
                }
            }
            finally
            {
                busy = false;
                UpdateEnabledState();
            }
        }

        private async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            await RestartControllerAsync();
        }

        /// <summary>
        /// Restarts the controller: RESTART command, then the port is reopened.
        /// Reopening alone already resets CP2102 boards (DTR/RTS auto reset).
        /// </summary>
        private async Task RestartControllerAsync()
        {
            string portName = link.PortName;
            tester.Stop(park: false);
            UpdateTestUi();
            try
            {
                Log("> RESTART");
                link.SendLine("RESTART");
                await Task.Delay(300);
            }
            catch (Exception)
            {
            }
            link.Close();
            Status("Restarting the controller...");
            await ReconnectAsync(portName, 12000);

            bool restartPending = deviceAxes.Any(a => a.RestartPending) || deviceSettings.Values.Any(s => s.RestartPending);
            if (link.IsOpen && restartPending)
            {
                Status("The controller did not restart. Press RST or unplug USB, then connect again.");
                MessageBox.Show("The controller did not restart (firmware without the RESTART command).\n\nPress RST on the board or unplug USB, then connect again.",
                    "DIY FFB Belt Tensioner", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnResetConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var answer = MessageBox.Show("Delete all saved values in the controller (pitch, range, direction, positions, idle, homing)?\nThe firmware defaults (Config.h) apply after a restart.",
                "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            Log("> RESET_CONFIG");
            await link.RequestAsync("RESET_CONFIG", null, 1500, 400);
            await ReadAllAsync();
        }

        /// <summary>Waits until the port is present again and connects (after flashing or a restart).</summary>
        private async Task<bool> ReconnectAsync(string portName, int timeoutMs)
        {
            await Task.Delay(1500);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (ComPortInfo.Find(portName) != null)
                {
                    try
                    {
                        link.Open(portName);
                        Status("Reconnected to " + portName);
                        await RefreshPortsAsync();
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
            Status("Could not reconnect to " + portName + ". Connect manually.");
            await RefreshPortsAsync();
            return false;
        }

        // ==================================================================
        // Servo
        // ==================================================================

        private int SelectedServo => ComboServo.SelectedIndex == 1 ? 2 : 1;

        private async void BtnFlashServo_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected() || busy) return;
            int servo = SelectedServo;
            var answer = MessageBox.Show(
                $"Write the tuned parameters into the EEPROM of servo {servo}?\n\n" +
                "- The servo must be powered and connected to the controller.\n" +
                "- SimHub Motion must not use the controller.\n" +
                "- Power cycle the servo afterwards.",
                "Flash servo parameters", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            busy = true;
            BtnFlashServo.IsEnabled = false;
            try
            {
                tester.Stop(park: false);
                UpdateTestUi();
                string cmd = "FLASH_SERVO " + servo;
                Log("> " + cmd);
                Status("Flashing servo " + servo + " (see Serial Logs)...");
                var reply = await link.RequestAsync(cmd,
                    l => l.Contains("[Flash] Done!") || l.Contains("[Flash] Warning") || l.Contains("is not enabled"),
                    120000, 15000);
                if (reply.Any(l => l.Contains("[Flash] Done!")))
                {
                    Status("Servo parameters saved. Power cycle the servo.");
                    MessageBox.Show("Servo parameters saved.\n\nPower cycle the servo (switch its power supply off and on) to activate them.",
                        "Flash servo parameters", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Status("Servo flash did not report success. Check the servo power and the RS232 wiring.");
                }
            }
            finally
            {
                busy = false;
                BtnFlashServo.IsEnabled = true;
            }
        }

        private async void BtnDiffServo_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected() || busy) return;
            busy = true;
            BtnDiffServo.IsEnabled = false;
            try
            {
                string cmd = "DIFF_SERVO " + SelectedServo;
                Log("> " + cmd);
                var reply = await link.RequestAsync(cmd, l => l.Contains(" read errors.") || l.Contains("is not enabled"), 60000, 8000);
                string summary = reply.LastOrDefault(l => l.StartsWith("Summary:"));
                Status(summary ?? "Servo check finished (see Serial Logs).");
            }
            finally
            {
                busy = false;
                BtnDiffServo.IsEnabled = true;
            }
        }

        private ServoRegisterWindow registerWindow;

        private void BtnServoRegisters_Click(object sender, RoutedEventArgs e)
        {
            if (registerWindow != null)
            {
                if (registerWindow.WindowState == WindowState.Minimized) registerWindow.WindowState = WindowState.Normal;
                registerWindow.Activate();
                return;
            }
            if (!EnsureConnected()) return;
            if (busy)
            {
                Status("Wait until the current action has finished.");
                return;
            }
            tester.Stop(park: false);
            UpdateTestUi();

            registerWindow = new ServoRegisterWindow(link, SelectedServo, Log);
            registerWindow.Owner = Window.GetWindow(this);
            registerWindow.Closed += (s, args) => registerWindow = null;
            registerWindow.Show();
            var ignored = registerWindow.LoadAsync();
        }

        private void BtnEnableServo_Click(object sender, RoutedEventArgs e)
        {
            SendAscii("ENABLE_SERVO " + SelectedServo);
        }

        private void BtnDisableServo_Click(object sender, RoutedEventArgs e)
        {
            tester.Stop(park: false);
            UpdateTestUi();
            SendAscii("DISABLE_SERVO");
        }

        // ==================================================================
        // Actions & live load
        // ==================================================================

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            if (SendBinary(() => link.Recalibrate(), "Recalibrate (CMD 13)")) Status("Homing started.");
        }

        private void BtnPark_Click(object sender, RoutedEventArgs e)
        {
            tester.Stop(park: false);
            UpdateTestUi();
            if (SendBinary(() => link.ParkNow(), "Park (CMD 12)")) Status("Parking.");
        }

        private void BtnStatus_Click(object sender, RoutedEventArgs e)
        {
            SendAscii("STATUS");
        }

        private void ToggleLiveLoad_Click(object sender, RoutedEventArgs e)
        {
            if (ToggleLiveLoad.IsChecked == true && link.IsOpen)
            {
                liveLoadActive = true;
                loadTimer.Start();
            }
            else
            {
                liveLoadActive = false;
                loadTimer.Stop();
                ToggleLiveLoad.IsChecked = false;
            }
        }

        private void LoadTimer_Tick(object sender, EventArgs e)
        {
            if (!link.IsOpen)
            {
                ToggleLiveLoad.IsChecked = false;
                liveLoadActive = false;
                loadTimer.Stop();
                return;
            }
            try
            {
                link.DumpSensor(0);
                if (link.EnabledMotors >= 2) link.DumpSensor(1);
            }
            catch (Exception)
            {
            }
        }

        private void ShowLoad(int axis, int load, int level, bool triggered)
        {
            if (axis >= 0 && axis < 2) lastLoad[axis] = load;
            string text = lastLoad[0] == int.MinValue ? "-" : lastLoad[0] + " %";
            if (link.EnabledMotors >= 2 && lastLoad[1] != int.MinValue) text += "  |  " + lastLoad[1] + " %";
            InfoLoad.Text = text + (triggered ? "  (limit " + level + " %)" : "");
        }

        // ==================================================================
        // Motion test
        // ==================================================================

        private void ComboTestMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTestModeUi();
            if (loading) return;
            ApplyTestParameters();
        }

        private void UpdateTestModeUi()
        {
            int mode = ComboTestMode.SelectedIndex;
            PanelTestHold.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelTestSine.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelTestPulse.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TestParam_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            ApplyTestParameters();
        }

        private void ApplyTestParameters()
        {
            if (loading) return;
            int mode = Math.Max(0, ComboTestMode.SelectedIndex);
            tester.Mode = mode == 1 ? TestMode.Sine : mode == 2 ? TestMode.BrakePulse : TestMode.Hold;
            tester.HoldPercent = SliderHold.Value;
            tester.SineCenter = SliderSineCenter.Value;
            tester.SineAmplitude = SliderSineAmplitude.Value;
            tester.SineFrequencyHz = SliderSineFrequency.Value;
            tester.PulseBase = SliderPulseBase.Value;
            tester.PulsePeak = SliderPulsePeak.Value;
            tester.PulseDurationMs = (int)SliderPulseDuration.Value;
            tester.PulseIntervalMs = (int)SliderPulseInterval.Value;
            tester.Axis1 = ToggleTestAxis1.IsChecked == true;
            tester.Axis2 = ToggleTestAxis2.IsChecked == true && link.EnabledMotors >= 2;

            settings.TestMode = mode;
            settings.TestPositionPct = tester.HoldPercent;
            settings.TestSineCenterPct = tester.SineCenter;
            settings.TestSineAmplitudePct = tester.SineAmplitude;
            settings.TestSineFrequencyHz = tester.SineFrequencyHz;
            settings.TestPulseBasePct = tester.PulseBase;
            settings.TestPulsePeakPct = tester.PulsePeak;
            settings.TestPulseDurationMs = tester.PulseDurationMs;
            settings.TestPulseIntervalMs = tester.PulseIntervalMs;
            settings.TestAxis1 = ToggleTestAxis1.IsChecked == true;
            settings.TestAxis2 = ToggleTestAxis2.IsChecked == true;
        }

        private static bool TryParseInt(string text, int min, int max, out int value)
        {
            return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        private void BtnTestStart_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            int frequency, acceleration;
            if (!TryParseInt(TextTestFrequency.Text, 1, 36000, out frequency))
            {
                Status("Max. frequency must be 1 - 36000.");
                return;
            }
            if (!TryParseInt(TextTestAcceleration.Text, 1, 100000, out acceleration))
            {
                Status("Max. acceleration must be 1 - 100000.");
                return;
            }
            ApplyTestParameters();
            if (!tester.Axis1 && !tester.Axis2)
            {
                Status("Select at least one axis.");
                return;
            }

            settings.StepperFrequency = frequency;
            settings.StepperAcceleration = acceleration;
            SaveSettings();

            try
            {
                UpdateTestIdleInput();
                tester.Start(frequency, acceleration);
                Status("Test started (" + ComboTestMode.Text + ").");
            }
            catch (Exception ex)
            {
                Status("Test start failed: " + ex.Message);
            }
            UpdateTestUi();
        }

        private void BtnTestStop_Click(object sender, RoutedEventArgs e)
        {
            tester.Stop(park: true);
            SaveSettings();
            Status("Test stopped, parking.");
            UpdateTestUi();
        }

        private void UpdateTestUi()
        {
            bool running = tester.IsRunning;
            BtnTestStart.IsEnabled = !running && link.IsOpen;
            BtnTestStop.IsEnabled = running;
            TextTestState.Text = running ? "Running..." : "";
            if (running)
            {
                liveTimer.Start();
            }
            else
            {
                liveTimer.Stop();
                Travel.LivePct = double.NaN;
                Travel.Refresh();
            }
        }

        // ==================================================================
        // Firmware
        // ==================================================================

        private void FlashLog(string text)
        {
            Log(text);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TextFlashLog.AppendText(text + Environment.NewLine);
                TextFlashLog.ScrollToEnd();
            }));
        }

        private void ComboBoard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) return;
            var board = ComboBoard.SelectedItem as BoardInfo;
            if (board == null) return;
            settings.BoardEnvironment = board.Environment;
            SaveSettings();
            ShowReleaseInfo();
        }

        private void FirmwareSource_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            settings.FirmwareSource = RadioSourceZip.IsChecked == true ? 1 : RadioSourceFolder.IsChecked == true ? 2 : 0;
            SaveSettings();
            UpdateFirmwareSourceUi();
            if (settings.FirmwareSource == 0 && latestRelease == null)
            {
                var ignored = LoadReleasesAsync(userInitiated: true);
            }
        }

        private void UpdateFirmwareSourceUi()
        {
            bool local = RadioSourceGitHub.IsChecked != true;
            TextLocalFirmware.IsEnabled = local;
            BtnBrowseFirmware.IsEnabled = local;
            BtnCheckRelease.IsEnabled = !local;
            PopulateBoards();
        }

        private void TextRepository_LostFocus(object sender, RoutedEventArgs e)
        {
            string repo = TextRepository.Text.Trim();
            if (repo.Length == 0) repo = new BeltTensionerSettings().GitHubRepository;
            TextRepository.Text = repo;
            if (repo != settings.GitHubRepository)
            {
                settings.GitHubRepository = repo;
                SaveSettings();
                var ignored = LoadReleasesAsync(userInitiated: true);
            }
        }

        private void TextLocalFirmware_LostFocus(object sender, RoutedEventArgs e)
        {
            settings.LocalFirmwarePath = TextLocalFirmware.Text.Trim();
            SaveSettings();
        }

        private void BtnBrowseFirmware_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            if (RadioSourceZip.IsChecked == true)
            {
                dialog.Filter = "Release archive (*.zip)|*.zip";
                dialog.Title = "Select DIY-Sim-Racing-FFB-BeltTensioner_<board>.zip";
            }
            else
            {
                dialog.Filter = "Firmware (firmware.bin)|firmware.bin;firmware.factory.bin|All .bin files|*.bin";
                dialog.Title = "Select firmware.bin (bootloader.bin, partitions.bin and boot_app0.bin must be in the same folder)";
            }
            if (File.Exists(settings.LocalFirmwarePath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(settings.LocalFirmwarePath);
            }
            if (dialog.ShowDialog() == true)
            {
                TextLocalFirmware.Text = dialog.FileName;
                settings.LocalFirmwarePath = dialog.FileName;
                SaveSettings();
            }
        }

        private async void BtnCheckRelease_Click(object sender, RoutedEventArgs e)
        {
            await LoadReleasesAsync(userInitiated: true);
        }

        /// <summary>
        /// Reads the latest release of the repository: shows it, fills the board variants and checks
        /// for a plugin update (at start only if enabled; userInitiated = the user asked for it).
        /// </summary>
        private async Task LoadReleasesAsync(bool userInitiated)
        {
            if (loadingReleases) return;
            if (!userInitiated && !settings.CheckPluginUpdates && settings.FirmwareSource != 0) return;
            loadingReleases = true;
            BtnCheckRelease.IsEnabled = false;
            try
            {
                TextLatestRelease.Text = "reading from GitHub...";
                latestRelease = await FirmwareReleaseService.GetLatestAsync(settings.GitHubRepository);
                TextLatestRelease.Text = latestRelease.DisplayName;
                PopulateBoards();

                if (userInitiated || settings.CheckPluginUpdates)
                {
                    CheckPluginUpdate(userInitiated);
                }
            }
            catch (Exception ex)
            {
                TextLatestRelease.Text = "not available";
                TextReleaseInfo.Text = "Could not read the latest release: " + ex.Message;
                if (userInitiated) Status("Could not read the latest release: " + ex.Message);
            }
            finally
            {
                loadingReleases = false;
                BtnCheckRelease.IsEnabled = true;
            }
        }

        /// <summary>
        /// Board list: the variants of the latest release for the GitHub source, all known boards
        /// for local firmware (or while the release is not loaded).
        /// </summary>
        private void PopulateBoards()
        {
            List<BoardInfo> boards = null;
            if (settings.FirmwareSource == 0 && latestRelease != null)
            {
                boards = BoardInfo.FromRelease(latestRelease);
            }
            if (boards == null || boards.Count == 0)
            {
                boards = BoardInfo.All.ToList();
            }

            string selected = (ComboBoard.SelectedItem as BoardInfo)?.Environment ?? settings.BoardEnvironment;
            bool wasLoading = loading;
            loading = true; // keep the saved board while the list is replaced
            ComboBoard.ItemsSource = boards;
            ComboBoard.SelectedItem = boards.FirstOrDefault(b => string.Equals(b.Environment, selected, StringComparison.OrdinalIgnoreCase)) ?? boards.FirstOrDefault();
            loading = wasLoading;
            ShowReleaseInfo();
        }

        private void BtnOpenRelease_Click(object sender, RoutedEventArgs e)
        {
            string url = latestRelease?.HtmlUrl ?? "https://github.com/" + settings.GitHubRepository + "/releases/latest";
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                Status("Could not open the browser: " + ex.Message);
            }
        }

        private void ShowReleaseInfo()
        {
            var board = ComboBoard.SelectedItem as BoardInfo;
            if (settings.FirmwareSource != 0)
            {
                TextReleaseInfo.Text = "Make sure the local firmware was built for this board.";
                return;
            }
            if (latestRelease == null || board == null)
            {
                TextReleaseInfo.Text = "";
                return;
            }
            var asset = latestRelease.FindAsset(board.Environment);
            TextReleaseInfo.Text = asset != null
                ? $"Flashes {asset.Name} ({asset.Size / 1024} kB) of release {latestRelease.Tag}."
                : $"Release {latestRelease.Tag} has no firmware for {board.Environment}.";
        }

        private async Task<FirmwareImage> ResolveFirmwareAsync(BoardInfo board)
        {
            switch (settings.FirmwareSource)
            {
                case 1:
                    {
                        string zip = settings.LocalFirmwarePath;
                        if (!File.Exists(zip) || !zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            FlashLog("Select a release .zip first.");
                            return null;
                        }
                        string folder = FirmwareReleaseService.ExtractLocalZip(zip);
                        FlashLog("Extracted " + Path.GetFileName(zip) + " to " + folder);
                        string fileName = Path.GetFileNameWithoutExtension(zip);
                        if (!fileName.EndsWith("_" + board.Environment, StringComparison.OrdinalIgnoreCase))
                        {
                            FlashLog("WARNING: the archive name does not end with _" + board.Environment + ". Make sure it matches your board.");
                        }
                        return FirmwareImage.FromPath(folder);
                    }
                case 2:
                    return FirmwareImage.FromPath(settings.LocalFirmwarePath);
                default:
                    {
                        if (latestRelease == null)
                        {
                            FlashLog("Reading the latest release of " + settings.GitHubRepository + "...");
                            await LoadReleasesAsync(userInitiated: true);
                            if (latestRelease == null)
                            {
                                FlashLog("No release found.");
                                return null;
                            }
                        }
                        var release = latestRelease;
                        FlashLog("Release " + release.Tag);
                        string folder = await FirmwareReleaseService.DownloadAndExtractAsync(release, board.Environment, FlashLog);
                        return FirmwareImage.FromPath(folder);
                    }
            }
        }

        private async void BtnFlash_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            var board = ComboBoard.SelectedItem as BoardInfo;
            var port = ComboPort.SelectedItem as ComPortInfo;
            if (board == null || port == null)
            {
                FlashLog("Select the COM port (TENSIONER tab) and the controller board first.");
                return;
            }
            if (port.IsPedalDevice)
            {
                var answer = MessageBox.Show(port.PortName + " is a DIY FFB Pedal device (" + port.KindText + "). Flash the belt tensioner firmware onto it anyway?",
                    "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }
            TextLocalFirmware_LostFocus(null, null);

            busy = true;
            BtnFlash.IsEnabled = false;
            TextFlashLog.Clear();
            try
            {
                FirmwareImage image;
                try
                {
                    image = await ResolveFirmwareAsync(board);
                }
                catch (Exception ex)
                {
                    FlashLog("Firmware download failed: " + ex.Message);
                    return;
                }
                if (image == null || !image.IsValid)
                {
                    FlashLog("No valid firmware found (bootloader.bin, partitions.bin, boot_app0.bin, firmware.bin or firmware.factory.bin).");
                    return;
                }

                if (link.IsOpen)
                {
                    tester.Stop(park: true);
                    UpdateTestUi();
                    await Task.Delay(100);
                    link.Close();
                    await Task.Delay(300);
                }

                FlashLog("");
                FlashLog($"=== Flashing {board.Environment} on {port.PortName} ===");
                var flasher = new EspFlasher();
                flasher.Output += FlashLog;
                bool ok = await flasher.FlashAsync(port.PortName, board, image);
                Status(ok ? "Firmware flashed." : "Flashing failed (see FIRMWARE tab).");

                if (ok && ToggleReconnectAfterFlash.IsChecked == true)
                {
                    FlashLog("Waiting for the controller to restart...");
                    await ReconnectAsync(port.PortName, 15000);
                }
            }
            finally
            {
                busy = false;
                BtnFlash.IsEnabled = true;
            }
        }

        // ==================================================================
        // Plugin update
        // ==================================================================

        private void CheckPluginUpdate(bool userInitiated)
        {
            if (latestRelease == null) return;
            var update = PluginUpdater.FindUpdate(new[] { latestRelease }, includeForDevBuild: userInitiated);
            if (update != null && (userInitiated || update.Tag != settings.SkippedPluginVersion))
            {
                pendingPluginUpdate = update;
                TextUpdate.Text = PluginUpdater.IsDevBuild
                    ? $"Plugin release {update.Tag} is available (you are running a local dev build)."
                    : $"Plugin update available: {update.Tag} (installed: {PluginUpdater.CurrentVersion}).";
                BannerUpdate.Visibility = Visibility.Visible;
                if (userInitiated) Status("Plugin update " + update.Tag + " available.");
            }
            else if (userInitiated)
            {
                Status(latestRelease.PluginAsset != null
                    ? "The plugin is up to date (" + PluginUpdater.CurrentVersion + ")."
                    : "The latest release " + latestRelease.Tag + " does not contain the plugin yet.");
            }
        }

        private async void BtnCheckPluginUpdate_Click(object sender, RoutedEventArgs e)
        {
            await LoadReleasesAsync(userInitiated: true);
        }

        private void ToggleCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            settings.CheckPluginUpdates = ToggleCheckUpdates.IsChecked == true;
            SaveSettings();
        }

        private void BtnReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            if (pendingPluginUpdate == null) return;
            try
            {
                Process.Start(pendingPluginUpdate.HtmlUrl);
            }
            catch (Exception ex)
            {
                Status("Could not open the browser: " + ex.Message);
            }
        }

        private void BtnSkipUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (pendingPluginUpdate != null)
            {
                settings.SkippedPluginVersion = pendingPluginUpdate.Tag;
                SaveSettings();
            }
            BannerUpdate.Visibility = Visibility.Collapsed;
        }

        private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            var update = pendingPluginUpdate;
            if (update == null || busy) return;

            var answer = MessageBox.Show(
                $"Update the DIY FFB Belt Tensioner plugin to {update.Tag}?\n\n" +
                "The new plugin is downloaded now. Then SimHub closes, the plugin is replaced (admin rights are needed for the SimHub folder) and SimHub starts again.",
                "Plugin update", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            busy = true;
            BtnInstallUpdate.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                Status("Downloading plugin " + update.Tag + "...");
                await PluginUpdater.DownloadAsync(update);
            }
            catch (Exception ex)
            {
                Status("Plugin download failed: " + ex.Message);
                MessageBox.Show("The update could not be downloaded:\n" + ex.Message, "Plugin update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
                busy = false;
                BtnInstallUpdate.IsEnabled = true;
            }

            // Release the controller before SimHub closes
            tester.Stop(park: true);
            link.Close();
            SaveSettings();

            try
            {
                PluginUpdater.InstallAndRestart();
            }
            catch (Exception ex)
            {
                Status("Plugin update not started: " + ex.Message);
                MessageBox.Show("The update could not be started:\n" + ex.Message + "\n\nThe installed plugin was not changed.", "Plugin update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==================================================================
        // SimHub Motion
        // ==================================================================

        private void MotionLayout_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            StoreMotionUi();
            settings.MotionDual = RadioMotionDual.IsChecked == true;
            ComboMotionPort1.Text = settings.MotionDual ? settings.MotionPortLeft : settings.MotionPortSingle;
            ToggleMotionReverse1.IsChecked = settings.MotionDual ? settings.MotionReverseLeft : settings.MotionReverseSingle;
            if (string.IsNullOrEmpty(ComboMotionPort1.Text))
            {
                ComboMotionPort1.Text = currentPorts.Where(p => p.Kind == DeviceKind.BeltTensioner).Select(p => p.PortName).FirstOrDefault() ?? settings.ComPort;
            }
            SaveSettings();
            UpdateMotionLayoutUi();
        }

        /// <summary>Copies the port / direction fields of the layout that is shown into the settings.</summary>
        private void StoreMotionUi()
        {
            if (settings.MotionDual)
            {
                settings.MotionPortLeft = ComboMotionPort1.Text.Trim();
                settings.MotionReverseLeft = ToggleMotionReverse1.IsChecked == true;
                settings.MotionPortRight = ComboMotionPort2.Text.Trim();
                settings.MotionReverseRight = ToggleMotionReverse2.IsChecked == true;
            }
            else
            {
                settings.MotionPortSingle = ComboMotionPort1.Text.Trim();
                settings.MotionReverseSingle = ToggleMotionReverse1.IsChecked == true;
            }
        }

        private void UpdateMotionLayoutUi()
        {
            bool dual = RadioMotionDual.IsChecked == true;
            LabelMotionPort1.Text = dual ? "Left belt COM port:" : "COM port:";
            var visibility = dual ? Visibility.Visible : Visibility.Collapsed;
            LabelMotionPort2.Visibility = visibility;
            ComboMotionPort2.Visibility = visibility;
            PanelMotionReverse2.Visibility = visibility;
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            StoreMotionUi();
            bool dual = RadioMotionDual.IsChecked == true;

            int frequency, acceleration;
            if (!TryParseInt(TextMotionFrequency.Text, 1, 36000, out frequency))
            {
                Status("Max. steppers pulse frequency must be 1 - 36000.");
                return;
            }
            if (!TryParseInt(TextMotionAcceleration.Text, 1, 100000, out acceleration))
            {
                Status("Max. steppers acceleration must be 1 - 100000.");
                return;
            }

            MotionControllerSetup first, right = null;
            if (dual)
            {
                first = new MotionControllerSetup { ComPort = settings.MotionPortLeft, Reverse = settings.MotionReverseLeft };
                right = new MotionControllerSetup { ComPort = settings.MotionPortRight, Reverse = settings.MotionReverseRight };
                if (string.IsNullOrEmpty(first.ComPort) || string.IsNullOrEmpty(right.ComPort))
                {
                    MessageBox.Show("Select the COM ports of the left and the right belt.", "SimHub Motion preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (string.Equals(first.ComPort, right.ComPort, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Left and right belt need different COM ports (one controller board per actuator).", "SimHub Motion preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else
            {
                first = new MotionControllerSetup { ComPort = settings.MotionPortSingle, Reverse = settings.MotionReverseSingle };
                if (string.IsNullOrEmpty(first.ComPort))
                {
                    MessageBox.Show("Select the COM port of the belt tensioner.", "SimHub Motion preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            settings.StepperFrequency = frequency;
            settings.StepperAcceleration = acceleration;
            SaveSettings();

            string json;
            try
            {
                json = MotionPresetBuilder.Build(first, right, frequency, acceleration);
            }
            catch (Exception ex)
            {
                Status("Creating the preset failed: " + ex.Message);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "SimHub Motion settings (*.shmotionoutput)|*.shmotionoutput",
                FileName = dual ? "DIY_BeltTensioner_Dual.shmotionoutput" : "DIY_BeltTensioner_Single.shmotionoutput",
                Title = "Save SimHub Motion preset",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
                Status("Preset saved: " + dialog.FileName);
                Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
            }
            catch (Exception ex)
            {
                Status("Saving failed: " + ex.Message);
            }
        }

        private void BtnReleasePort_Click(object sender, RoutedEventArgs e)
        {
            if (!link.IsOpen)
            {
                Status("The port is not used by this plugin, SimHub Motion can use it.");
                return;
            }
            tester.Stop(park: true);
            UpdateTestUi();
            link.Close();
            Status("Port released. You can enable SimHub Motion now.");
        }

        private void BtnOpenMotionDocs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("https://github.com/" + settings.GitHubRepository + "/blob/main/docs/SimHub_Motion_Setup.md");
            }
            catch (Exception ex)
            {
                Status("Could not open the browser: " + ex.Message);
            }
        }
    }
}
