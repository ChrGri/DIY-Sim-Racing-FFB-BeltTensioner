using DiyBeltTensioner.Core;
using Microsoft.Win32;
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

        private readonly BeltTensionerPlugin plugin;
        private readonly BeltTensionerSettings settings;
        private readonly BeltTensionerLink link;
        private readonly TestRunner tester;

        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private readonly DispatcherTimer logTimer;
        private readonly DispatcherTimer loadTimer;

        private bool loading = true;
        private volatile bool liveLoadActive;
        private bool busy;
        private FirmwareRelease latestRelease;
        private List<AxisConfigInfo> lastConfig = new List<AxisConfigInfo>();

        public BeltTensionerControl(BeltTensionerPlugin plugin)
        {
            this.plugin = plugin;
            settings = plugin.Settings;
            link = plugin.Link;
            tester = plugin.Tester;

            InitializeComponent();

            logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            logTimer.Tick += (s, e) => FlushLog();
            logTimer.Start();

            loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            loadTimer.Tick += LoadTimer_Tick;

            LoadSettingsIntoUi();
            RefreshPorts();
            loading = false;

            link.LineReceived += Link_LineReceived;
            link.ConnectionChanged += open => Dispatcher.BeginInvoke(new Action(() => OnConnectionChanged(open)));
            tester.Failed += msg => Dispatcher.BeginInvoke(new Action(() =>
            {
                Log("Test stopped: " + msg);
                UpdateTestUi();
            }));

            UpdateConnectionUi();
            UpdateMotionLayoutUi();
            UpdateFirmwareSourceUi();
            UpdateTestUi();

            if (link.IsOpen)
            {
                var ignored = AfterConnectAsync(waitForGreeting: false);
            }
        }

        // ==================================================================
        // Settings <-> UI
        // ==================================================================

        private void LoadSettingsIntoUi()
        {
            CheckAutoConnect.IsChecked = settings.AutoConnect;

            ComboBoard.ItemsSource = BoardInfo.All;
            ComboBoard.SelectedItem = BoardInfo.All.FirstOrDefault(b => b.Environment == settings.BoardEnvironment) ?? BoardInfo.All[2];
            TextRepository.Text = settings.GitHubRepository;
            TextLocalFirmware.Text = settings.LocalFirmwarePath;
            switch (settings.FirmwareSource)
            {
                case 1: RadioSourceZip.IsChecked = true; break;
                case 2: RadioSourceFolder.IsChecked = true; break;
                default: RadioSourceGitHub.IsChecked = true; break;
            }

            switch (settings.TestMode)
            {
                case 1: RadioTestSine.IsChecked = true; break;
                case 2: RadioTestPulse.IsChecked = true; break;
                default: RadioTestHold.IsChecked = true; break;
            }
            SliderHold.Value = settings.TestPositionPct;
            SliderSineCenter.Value = settings.TestSineCenterPct;
            SliderSineAmplitude.Value = settings.TestSineAmplitudePct;
            SliderSineFrequency.Value = settings.TestSineFrequencyHz;
            SliderPulseBase.Value = settings.TestPulseBasePct;
            SliderPulsePeak.Value = settings.TestPulsePeakPct;
            SliderPulseDuration.Value = settings.TestPulseDurationMs;
            SliderPulseInterval.Value = settings.TestPulseIntervalMs;
            CheckTestAxis1.IsChecked = settings.TestAxis1;
            CheckTestAxis2.IsChecked = settings.TestAxis2;
            TextTestFrequency.Text = settings.StepperFrequency.ToString(CultureInfo.InvariantCulture);
            TextTestAcceleration.Text = settings.StepperAcceleration.ToString(CultureInfo.InvariantCulture);
            TextMotionFrequency.Text = settings.StepperFrequency.ToString(CultureInfo.InvariantCulture);
            TextMotionAcceleration.Text = settings.StepperAcceleration.ToString(CultureInfo.InvariantCulture);

            if (settings.MotionDual) RadioMotionDual.IsChecked = true; else RadioMotionSingle.IsChecked = true;
            CheckMotionReverse1.IsChecked = settings.MotionDual ? settings.MotionReverseLeft : settings.MotionReverseSingle;
            CheckMotionReverse2.IsChecked = settings.MotionReverseRight;
        }

        private void SaveSettings()
        {
            if (loading) return;
            plugin.SaveSettings();
        }

        // ==================================================================
        // Log
        // ==================================================================

        private void Log(string text)
        {
            logQueue.Enqueue(text);
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
                Log("Send failed: " + ex.Message);
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
                Log("Send failed: " + ex.Message);
                return false;
            }
        }

        private bool EnsureConnected()
        {
            if (link.IsOpen) return true;
            Log("Not connected. Select the COM port and click Connect.");
            return false;
        }

        // ==================================================================
        // Connection
        // ==================================================================

        private void RefreshPorts()
        {
            var ports = ComPortInfo.GetPresentPorts();
            string selected = (ComboPort.SelectedItem as ComPortInfo)?.PortName ?? settings.ComPort;
            ComboPort.ItemsSource = ports;
            ComboPort.SelectedItem = ports.FirstOrDefault(p => p.PortName == selected) ?? ports.FirstOrDefault();

            var names = ports.Select(p => p.PortName).ToList();
            string m1 = string.IsNullOrEmpty(ComboMotionPort1.Text) ? (settings.MotionDual ? settings.MotionPortLeft : settings.MotionPortSingle) : ComboMotionPort1.Text;
            string m2 = string.IsNullOrEmpty(ComboMotionPort2.Text) ? settings.MotionPortRight : ComboMotionPort2.Text;
            ComboMotionPort1.ItemsSource = names;
            ComboMotionPort2.ItemsSource = names;
            ComboMotionPort1.Text = string.IsNullOrEmpty(m1) ? (selected ?? "") : m1;
            ComboMotionPort2.Text = m2 ?? "";
        }

        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (link.IsOpen)
            {
                tester.Stop(park: true);
                link.Close();
                return;
            }
            await ConnectAsync(true);
        }

        private async Task<bool> ConnectAsync(bool waitForGreeting)
        {
            var port = ComboPort.SelectedItem as ComPortInfo;
            if (port == null)
            {
                Log("No COM port selected.");
                return false;
            }

            try
            {
                link.Open(port.PortName);
            }
            catch (Exception ex)
            {
                Log("Connect failed: " + ex.Message);
                return false;
            }

            settings.ComPort = port.PortName;
            SaveSettings();
            Log("Connected to " + port.PortName + " (" + BeltTensionerLink.BaudRate + " baud)");
            await AfterConnectAsync(waitForGreeting);
            return true;
        }

        private async Task AfterConnectAsync(bool waitForGreeting)
        {
            try
            {
                if (waitForGreeting)
                {
                    // CP2102 boards reset when the port opens, native USB boards repeat their greeting.
                    await link.CollectAsync(() => { }, l => l.EndsWith("steppers enabled"), 3000, 0);
                }
                if (!link.IsOpen) return;
                await link.CollectAsync(() => link.QueryFirmwareVersion(), l => link.FirmwareVersion.Length > 0, 800, 0);
                await link.CollectAsync(() => link.QueryEnabledMotors(), l => l.StartsWith("Enabled motors:"), 800, 0);
                UpdateConnectionUi();
                await ReadConfigAsync();
            }
            catch (Exception ex)
            {
                Log("Query failed: " + ex.Message);
            }
        }

        private void OnConnectionChanged(bool open)
        {
            if (!open)
            {
                Log("Disconnected.");
                CheckLiveLoad.IsChecked = false;
            }
            UpdateConnectionUi();
            UpdateTestUi();
        }

        private void UpdateConnectionUi()
        {
            bool open = link.IsOpen;
            BtnConnect.Content = open ? "Disconnect" : "Connect";
            ComboPort.IsEnabled = !open;
            if (open)
            {
                string version = string.IsNullOrEmpty(link.FirmwareVersion) ? "?" : link.FirmwareVersion;
                string motors = link.EnabledMotors > 0 ? link.EnabledMotors.ToString(CultureInfo.InvariantCulture) : "?";
                TextConnectionState.Text = $"Connected to {link.PortName}  |  protocol version {version}  |  enabled actuators: {motors}";
            }
            else
            {
                TextConnectionState.Text = "Not connected";
            }

            bool twoAxes = link.EnabledMotors >= 2;
            TextPitch2.IsEnabled = twoAxes;
            TextRange2.IsEnabled = twoAxes;
        }

        private void CheckAutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            settings.AutoConnect = CheckAutoConnect.IsChecked == true;
            SaveSettings();
        }

        // ==================================================================
        // 1. Firmware
        // ==================================================================

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
        }

        private void UpdateFirmwareSourceUi()
        {
            bool local = RadioSourceGitHub.IsChecked != true;
            TextLocalFirmware.IsEnabled = local;
            BtnBrowseFirmware.IsEnabled = local;
            TextRepository.IsEnabled = !local;
            BtnCheckRelease.IsEnabled = !local;
        }

        private void TextRepository_LostFocus(object sender, RoutedEventArgs e)
        {
            string repo = TextRepository.Text.Trim();
            if (repo.Length == 0) repo = new BeltTensionerSettings().GitHubRepository;
            if (repo != settings.GitHubRepository)
            {
                settings.GitHubRepository = repo;
                latestRelease = null;
                SaveSettings();
            }
            TextRepository.Text = repo;
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
            TextRepository_LostFocus(null, null);
            BtnCheckRelease.IsEnabled = false;
            try
            {
                TextReleaseInfo.Text = "Checking GitHub...";
                latestRelease = await FirmwareReleaseService.GetLatestAsync(settings.GitHubRepository);
                ShowReleaseInfo();
            }
            catch (Exception ex)
            {
                TextReleaseInfo.Text = "Could not read the latest release: " + ex.Message;
            }
            finally
            {
                BtnCheckRelease.IsEnabled = RadioSourceGitHub.IsChecked == true;
            }
        }

        private void ShowReleaseInfo()
        {
            if (latestRelease == null) return;
            var board = ComboBoard.SelectedItem as BoardInfo;
            var asset = board != null ? latestRelease.FindAsset(board.Environment) : null;
            string date = latestRelease.PublishedAt == default(DateTime) ? "" : " (" + latestRelease.PublishedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) + ")";
            TextReleaseInfo.Text = asset != null
                ? $"Latest release: {latestRelease.Tag}{date}  -  {asset.Name}"
                : $"Latest release: {latestRelease.Tag}{date}  -  no archive for {board?.Environment}!";
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
                            Log("Select a release .zip first.");
                            return null;
                        }
                        string folder = FirmwareReleaseService.ExtractLocalZip(zip);
                        Log("Extracted " + Path.GetFileName(zip) + " to " + folder);
                        string fileName = Path.GetFileNameWithoutExtension(zip);
                        if (!fileName.EndsWith("_" + board.Environment, StringComparison.OrdinalIgnoreCase))
                        {
                            Log("WARNING: the archive name does not end with _" + board.Environment + ". Make sure it matches your board.");
                        }
                        return FirmwareImage.FromPath(folder);
                    }
                case 2:
                    return FirmwareImage.FromPath(settings.LocalFirmwarePath);
                default:
                    {
                        if (latestRelease == null)
                        {
                            Log("Reading the latest release of " + settings.GitHubRepository + "...");
                            latestRelease = await FirmwareReleaseService.GetLatestAsync(settings.GitHubRepository);
                            ShowReleaseInfo();
                        }
                        string folder = await FirmwareReleaseService.DownloadAndExtractAsync(latestRelease, board.Environment, Log);
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
                Log("Select the COM port and the controller board first.");
                return;
            }
            TextLocalFirmware_LostFocus(null, null);

            busy = true;
            BtnFlash.IsEnabled = false;
            try
            {
                FirmwareImage image;
                try
                {
                    image = await ResolveFirmwareAsync(board);
                }
                catch (Exception ex)
                {
                    Log("Firmware download failed: " + ex.Message);
                    return;
                }
                if (image == null || !image.IsValid)
                {
                    Log("No valid firmware found (bootloader.bin, partitions.bin, boot_app0.bin, firmware.bin or firmware.factory.bin).");
                    return;
                }

                if (link.IsOpen)
                {
                    tester.Stop(park: true);
                    await Task.Delay(100);
                    link.Close();
                    await Task.Delay(300);
                }

                Log("");
                Log($"=== Flashing {board.Environment} on {port.PortName} ===");
                var flasher = new EspFlasher();
                flasher.Output += Log;
                bool ok = await flasher.FlashAsync(port.PortName, board, image);

                if (ok && CheckReconnectAfterFlash.IsChecked == true)
                {
                    Log("Waiting for the controller to restart...");
                    await ReconnectAsync(port.PortName, 15000);
                }
            }
            finally
            {
                busy = false;
                BtnFlash.IsEnabled = true;
            }
        }

        /// <summary>Waits until the port is present again and connects (after flashing or a restart).</summary>
        private async Task<bool> ReconnectAsync(string portName, int timeoutMs)
        {
            await Task.Delay(1500);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var present = ComPortInfo.Find(portName);
                if (present != null)
                {
                    RefreshPorts();
                    ComboPort.SelectedItem = (ComboPort.ItemsSource as IEnumerable<ComPortInfo>)?.FirstOrDefault(p => p.PortName == portName);
                    try
                    {
                        link.Open(portName);
                        Log("Reconnected to " + portName);
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
            Log($"Could not reconnect to {portName}. Click Refresh and Connect manually.");
            RefreshPorts();
            return false;
        }

        // ==================================================================
        // 2. Configuration
        // ==================================================================

        private async void BtnReadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            await ReadConfigAsync();
        }

        private async Task<List<AxisConfigInfo>> ReadConfigAsync()
        {
            if (!link.IsOpen) return lastConfig;
            Log("> CONFIG");
            var lines = await link.RequestAsync("CONFIG", l => l.Contains("RESET_CONFIG"), 2500, 600);
            var config = AxisConfigParser.Parse(lines);
            if (config.Count == 0)
            {
                Log("No configuration received. Is this the belt tensioner firmware?");
                return lastConfig;
            }
            lastConfig = config;
            ShowConfig(config);
            return config;
        }

        private void ShowConfig(List<AxisConfigInfo> config)
        {
            var a1 = config.FirstOrDefault(c => c.Axis == 1);
            var a2 = config.FirstOrDefault(c => c.Axis == 2);
            ShowAxis(a1, TextPitch1, TextRange1, TextAxis1Info, TextAxis1Motion);
            ShowAxis(a2, TextPitch2, TextRange2, TextAxis2Info, TextAxis2Motion);
            if (a2 == null) TextAxis2Info.Text = "not enabled in this firmware (NUM_ACTUATORS = 1)";
            TextRestartPending.Visibility = config.Any(c => c.RestartPending) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ShowAxis(AxisConfigInfo info, TextBox pitch, TextBox range, TextBlock text, TextBlock motion)
        {
            if (info == null)
            {
                text.Text = "-";
                motion.Text = "";
                return;
            }
            pitch.Text = info.PitchMm.ToString("0.##", CultureInfo.InvariantCulture);
            range.Text = info.RangeMm.ToString("0.##", CultureInfo.InvariantCulture);
            text.Text = string.Format(CultureInfo.InvariantCulture, "pitch {0:0.##} mm ({1}), range {2:0.##} mm ({3}) = {4} steps",
                info.PitchMm, info.PitchSaved ? "saved" : "firmware default",
                info.RangeMm, info.RangeSaved ? "saved" : "firmware default", info.RangeSteps);
            motion.Text = info.MaxSpeedSteps > 0
                ? string.Format(CultureInfo.InvariantCulture, "active: max {0:0.000} m/s ({1} steps/s), {2:0.00} m/s² ({3} steps/s²){4}",
                    info.MaxSpeedMs, info.MaxSpeedSteps, info.AccelerationMs2, info.AccelerationSteps, info.RestartPending ? "  -  restart pending" : "")
                : "";
        }

        private static bool TryParseMm(string text, double min, double max, out double value)
        {
            text = (text ?? "").Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        private async void BtnWriteConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var commands = new List<string>();
            int axes = Math.Max(1, Math.Min(2, link.EnabledMotors));
            var boxes = new[] { Tuple.Create(TextPitch1, TextRange1), Tuple.Create(TextPitch2, TextRange2) };
            for (int axis = 1; axis <= axes; axis++)
            {
                double pitch, range;
                if (!TryParseMm(boxes[axis - 1].Item1.Text, 0.5, 50, out pitch))
                {
                    Log($"Axis {axis}: spindle pitch must be 0.5 - 50 mm.");
                    return;
                }
                if (!TryParseMm(boxes[axis - 1].Item2.Text, 5, 500, out range))
                {
                    Log($"Axis {axis}: working range must be 5 - 500 mm.");
                    return;
                }
                var current = lastConfig.FirstOrDefault(c => c.Axis == axis);
                if (current == null || Math.Abs(current.PitchMm - pitch) > 0.001 || !current.PitchSaved)
                {
                    commands.Add(string.Format(CultureInfo.InvariantCulture, "SET_PITCH {0} {1:0.###}", axis, pitch));
                }
                if (current == null || Math.Abs(current.RangeMm - range) > 0.001 || !current.RangeSaved)
                {
                    commands.Add(string.Format(CultureInfo.InvariantCulture, "SET_RANGE {0} {1:0.###}", axis, range));
                }
            }

            if (commands.Count == 0)
            {
                Log("Nothing changed.");
                return;
            }

            foreach (var cmd in commands)
            {
                Log("> " + cmd);
                var reply = await link.RequestAsync(cmd, l => l.Contains("saved.") || l.StartsWith("Invalid") || l.StartsWith("Usage"), 1500, 300);
                if (reply.Any(l => l.StartsWith("Invalid") || l.StartsWith("Usage")))
                {
                    Log("Controller rejected: " + cmd);
                    return;
                }
            }

            var config = await ReadConfigAsync();
            if (config.Any(c => c.RestartPending))
            {
                var answer = MessageBox.Show("The new values are saved and applied after a restart of the controller.\n\nRestart the controller now?",
                    "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    await RestartControllerAsync();
                }
            }
        }

        private async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            await RestartControllerAsync();
        }

        /// <summary>
        /// Restarts the controller: RESTART command (firmware with RESTART support), then the port
        /// is reopened. Reopening alone already resets CP2102 boards (DTR/RTS auto reset).
        /// </summary>
        private async Task RestartControllerAsync()
        {
            string portName = link.PortName;
            tester.Stop(park: false);
            CheckLiveLoad.IsChecked = false;
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
            await ReconnectAsync(portName, 12000);

            if (link.IsOpen && lastConfig.Any(c => c.RestartPending))
            {
                Log("The controller did not restart. Power cycle it (unplug USB / press RST) and connect again.");
                MessageBox.Show("The controller did not restart (older firmware without the RESTART command).\n\nPress RST on the board or unplug USB, then connect again.",
                    "DIY FFB Belt Tensioner", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnResetConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var answer = MessageBox.Show("Delete the saved spindle pitch and working range of all axes?\nThe firmware defaults (Config.h) apply after a restart.",
                "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            Log("> RESET_CONFIG");
            await link.RequestAsync("RESET_CONFIG", null, 1500, 400);
            await ReadConfigAsync();
        }

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
                string cmd = "FLASH_SERVO " + servo;
                Log("> " + cmd);
                var reply = await link.RequestAsync(cmd,
                    l => l.Contains("[Flash] Done!") || l.Contains("[Flash] Warning") || l.Contains("is not enabled"),
                    120000, 15000);
                if (reply.Any(l => l.Contains("[Flash] Done!")))
                {
                    MessageBox.Show("Servo parameters saved.\n\nPower cycle the servo (switch its power supply off and on) to activate them.",
                        "Flash servo parameters", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log("Servo flash did not report success. Check the servo power and the RS232 wiring.");
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
                await link.RequestAsync(cmd, l => l.Contains(" read errors.") || l.Contains("is not enabled"), 60000, 8000);
            }
            finally
            {
                busy = false;
                BtnDiffServo.IsEnabled = true;
            }
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
        // 3. Test
        // ==================================================================

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            SendBinary(() => link.Recalibrate(), "Recalibrate (CMD 13)");
        }

        private void BtnPark_Click(object sender, RoutedEventArgs e)
        {
            tester.Stop(park: false);
            UpdateTestUi();
            SendBinary(() => link.ParkNow(), "Park (CMD 12)");
        }

        private void BtnStatus_Click(object sender, RoutedEventArgs e)
        {
            SendAscii("STATUS");
        }

        private void CheckLiveLoad_Changed(object sender, RoutedEventArgs e)
        {
            if (CheckLiveLoad.IsChecked == true && link.IsOpen)
            {
                liveLoadActive = true;
                loadTimer.Start();
            }
            else
            {
                liveLoadActive = false;
                loadTimer.Stop();
                if (CheckLiveLoad.IsChecked == true) CheckLiveLoad.IsChecked = false;
            }
        }

        private void LoadTimer_Tick(object sender, EventArgs e)
        {
            if (!link.IsOpen)
            {
                CheckLiveLoad.IsChecked = false;
                return;
            }
            try
            {
                link.DumpSensor(0);
                if (link.EnabledMotors >= 2) link.DumpSensor(1);
            }
            catch (Exception)
            {
                CheckLiveLoad.IsChecked = false;
            }
        }

        private void ShowLoad(int axis, int load, int level, bool triggered)
        {
            var bar = axis == 0 ? BarLoad1 : BarLoad2;
            var text = axis == 0 ? TextLoad1 : TextLoad2;
            bar.Value = Math.Min(100, Math.Abs(load));
            text.Text = $"{load} % (limit {level}){(triggered ? " !" : "")}";
        }

        private void TestParam_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            ApplyTestParameters();
        }

        private void TestSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (loading) return;
            ApplyTestParameters();
        }

        private void ApplyTestParameters()
        {
            tester.Mode = RadioTestSine.IsChecked == true ? TestMode.Sine : RadioTestPulse.IsChecked == true ? TestMode.BrakePulse : TestMode.Hold;
            tester.HoldPercent = SliderHold.Value;
            tester.SineCenter = SliderSineCenter.Value;
            tester.SineAmplitude = SliderSineAmplitude.Value;
            tester.SineFrequencyHz = SliderSineFrequency.Value;
            tester.PulseBase = SliderPulseBase.Value;
            tester.PulsePeak = SliderPulsePeak.Value;
            tester.PulseDurationMs = (int)SliderPulseDuration.Value;
            tester.PulseIntervalMs = (int)SliderPulseInterval.Value;
            tester.Axis1 = CheckTestAxis1.IsChecked == true;
            tester.Axis2 = CheckTestAxis2.IsChecked == true;

            settings.TestMode = (int)tester.Mode;
            settings.TestPositionPct = tester.HoldPercent;
            settings.TestSineCenterPct = tester.SineCenter;
            settings.TestSineAmplitudePct = tester.SineAmplitude;
            settings.TestSineFrequencyHz = tester.SineFrequencyHz;
            settings.TestPulseBasePct = tester.PulseBase;
            settings.TestPulsePeakPct = tester.PulsePeak;
            settings.TestPulseDurationMs = tester.PulseDurationMs;
            settings.TestPulseIntervalMs = tester.PulseIntervalMs;
            settings.TestAxis1 = tester.Axis1;
            settings.TestAxis2 = tester.Axis2;
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
                Log("Max. pulse frequency must be 1 - 36000.");
                return;
            }
            if (!TryParseInt(TextTestAcceleration.Text, 1, 100000, out acceleration))
            {
                Log("Max. acceleration must be 1 - 100000.");
                return;
            }
            ApplyTestParameters();
            if (!tester.Axis1 && !tester.Axis2)
            {
                Log("Select at least one axis.");
                return;
            }

            settings.StepperFrequency = frequency;
            settings.StepperAcceleration = acceleration;
            SaveSettings();

            try
            {
                tester.Start(frequency, acceleration);
                Log($"Test started ({tester.Mode}, frequency {frequency}, acceleration {acceleration}).");
            }
            catch (Exception ex)
            {
                Log("Test start failed: " + ex.Message);
            }
            UpdateTestUi();
        }

        private void BtnTestStop_Click(object sender, RoutedEventArgs e)
        {
            tester.Stop(park: true);
            SaveSettings();
            Log("Test stopped, parking.");
            UpdateTestUi();
        }

        private void UpdateTestUi()
        {
            bool running = tester.IsRunning;
            BtnTestStart.IsEnabled = !running && link.IsOpen;
            BtnTestStop.IsEnabled = running;
            TextTestState.Text = running ? "Running..." : "";
        }

        // ==================================================================
        // 4. SimHub Motion
        // ==================================================================

        private void MotionLayout_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            StoreMotionUi();
            settings.MotionDual = RadioMotionDual.IsChecked == true;
            ComboMotionPort1.Text = settings.MotionDual ? settings.MotionPortLeft : settings.MotionPortSingle;
            CheckMotionReverse1.IsChecked = settings.MotionDual ? settings.MotionReverseLeft : settings.MotionReverseSingle;
            if (string.IsNullOrEmpty(ComboMotionPort1.Text)) ComboMotionPort1.Text = settings.ComPort;
            SaveSettings();
            UpdateMotionLayoutUi();
        }

        /// <summary>Copies the port / direction fields of the layout that is shown into the settings.</summary>
        private void StoreMotionUi()
        {
            if (settings.MotionDual)
            {
                settings.MotionPortLeft = ComboMotionPort1.Text.Trim();
                settings.MotionReverseLeft = CheckMotionReverse1.IsChecked == true;
                settings.MotionPortRight = ComboMotionPort2.Text.Trim();
                settings.MotionReverseRight = CheckMotionReverse2.IsChecked == true;
            }
            else
            {
                settings.MotionPortSingle = ComboMotionPort1.Text.Trim();
                settings.MotionReverseSingle = CheckMotionReverse1.IsChecked == true;
            }
        }

        private void UpdateMotionLayoutUi()
        {
            bool dual = RadioMotionDual.IsChecked == true;
            LabelMotionPort1.Text = dual ? "Left belt COM port" : "COM port";
            var visibility = dual ? Visibility.Visible : Visibility.Collapsed;
            LabelMotionPort2.Visibility = visibility;
            ComboMotionPort2.Visibility = visibility;
            CheckMotionReverse2.Visibility = visibility;
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            StoreMotionUi();
            bool dual = RadioMotionDual.IsChecked == true;

            int frequency, acceleration;
            if (!TryParseInt(TextMotionFrequency.Text, 1, 36000, out frequency))
            {
                Log("Max. steppers pulse frequency must be 1 - 36000.");
                return;
            }
            if (!TryParseInt(TextMotionAcceleration.Text, 1, 100000, out acceleration))
            {
                Log("Max. steppers acceleration must be 1 - 100000.");
                return;
            }

            MotionControllerSetup first, right = null;
            if (dual)
            {
                first = new MotionControllerSetup { ComPort = settings.MotionPortLeft, Reverse = settings.MotionReverseLeft };
                right = new MotionControllerSetup { ComPort = settings.MotionPortRight, Reverse = settings.MotionReverseRight };
                if (string.IsNullOrEmpty(first.ComPort) || string.IsNullOrEmpty(right.ComPort))
                {
                    Log("Select the COM ports of the left and the right belt.");
                    return;
                }
                if (string.Equals(first.ComPort, right.ComPort, StringComparison.OrdinalIgnoreCase))
                {
                    Log("Left and right belt need different COM ports (one controller board per actuator).");
                    return;
                }
            }
            else
            {
                first = new MotionControllerSetup { ComPort = settings.MotionPortSingle, Reverse = settings.MotionReverseSingle };
                if (string.IsNullOrEmpty(first.ComPort))
                {
                    Log("Select the COM port of the belt tensioner.");
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
                Log("Creating the preset failed: " + ex.Message);
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
                Log("Preset saved: " + dialog.FileName);
                Log("Import it in SimHub: Motion > Platform Config. > Settings Import and export > Import all platform settings.");
                Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
            }
            catch (Exception ex)
            {
                Log("Saving failed: " + ex.Message);
            }
        }

        private void BtnReleasePort_Click(object sender, RoutedEventArgs e)
        {
            if (!link.IsOpen)
            {
                Log("Port is not in use by this plugin, SimHub Motion can use it.");
                return;
            }
            tester.Stop(park: true);
            CheckLiveLoad.IsChecked = false;
            link.Close();
            Log("Port released. You can enable SimHub Motion now.");
        }

        private void BtnOpenMotionDocs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("https://github.com/" + settings.GitHubRepository + "/blob/main/docs/SimHub_Motion_Setup.md");
            }
            catch (Exception ex)
            {
                Log("Could not open the browser: " + ex.Message);
            }
        }
    }
}
