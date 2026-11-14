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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DiyBeltTensioner.UI
{
    /// <summary>
    /// Main view of the plugin:
    ///  SETUP    - rig layout (single / dual) with the renderings, one card per belt channel
    ///             (COM port, status, pitch, working range) and the SimHub Motion preset
    ///  CONFIG   - per channel: stroke view, positions, idle &amp; homing, motion test, servo
    ///  FIRMWARE - flash the controllers
    ///  SYSTEM   - plugin settings, COM port identification, status and serial logs
    /// Every channel has its own connection (TensionerChannel), so both belts of a dual
    /// setup are connected at the same time.
    /// </summary>
    public partial class BeltTensionerControl : UserControl
    {
        private const int MaxLogChars = 200000;
        private const int MaxStatusLines = 60;

        private readonly BeltTensionerPlugin plugin;
        private readonly BeltTensionerSettings settings;

        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private readonly DispatcherTimer logTimer;
        private readonly DispatcherTimer pollTimer;
        private readonly DispatcherTimer liveTimer;
        private readonly List<string> statusLines = new List<string>();

        private readonly LabeledSlider[] settingSliders;
        private readonly SHToggleButton[] settingToggles;

        private bool loading = true;
        private bool applying;
        private bool busy;
        private List<ComPortInfo> currentPorts = new List<ComPortInfo>();
        private bool refreshingPorts;
        private FirmwareRelease latestRelease;
        private FirmwareRelease pendingPluginUpdate;
        private bool loadingReleases;
        private ServoRegisterWindow registerWindow;
        private TensionerChannel registerWindowChannel;

        // Channel shown on the CONFIG tab
        private TensionerChannel cfgChannel;

        public BeltTensionerControl(BeltTensionerPlugin plugin)
        {
            this.plugin = plugin;
            settings = plugin.Settings;

            InitializeComponent();

            settingSliders = new[] { SliderIdle, SliderMin, SliderMax, SliderIdleDelay, SliderHomingThreshold, SliderBackoff };
            settingToggles = new[] { ToggleInvert, ToggleCenter, ToggleHomeAtBoot, ToggleWebGraphs };

            logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            logTimer.Tick += (s, e) => FlushLog();
            logTimer.Start();

            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            pollTimer.Tick += PollTimer_Tick;
            pollTimer.Start();

            liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            liveTimer.Tick += (s, e) =>
            {
                Travel.LivePct = cfgChannel != null && cfgChannel.Tester.IsRunning ? cfgChannel.Tester.CurrentPercent : double.NaN;
                Travel.Refresh();
            };

            foreach (var slider in settingSliders)
            {
                slider.ValueCommitted += SettingSlider_ValueCommitted;
            }
            foreach (var slider in new[] { SliderHold, SliderSineCenter, SliderSineAmplitude, SliderSineFrequency, SliderPulseBase, SliderPulsePeak, SliderPulseDuration, SliderPulseInterval })
            {
                slider.ValueCommitted += (s, v) => ApplyTestParameters();
            }

            foreach (var channel in plugin.AllChannels)
            {
                var c = channel;
                string prefix = "[" + (c.Key == "Single" ? "B" : c.Key.Substring(0, 1)) + "] ";
                c.Changed += () => Dispatcher.BeginInvoke(new Action(() => OnChannelChanged(c)));
                c.StateUpdated += () => Dispatcher.BeginInvoke(new Action(() => OnChannelState(c)), DispatcherPriority.Background);
                c.Message += text => Dispatcher.BeginInvoke(new Action(() => Status(text)));
                c.LogLine += line => Log(prefix + line);
                c.Tester.Failed += msg => Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status(c.Title + ": test stopped: " + msg);
                    UpdateTestUi();
                }));
            }

            CardA.PortChanged += OnCardPortChanged;
            CardB.PortChanged += OnCardPortChanged;
            CardA.RefreshPortsRequested += () => { var ignored = RefreshPortsAsync(); };
            CardB.RefreshPortsRequested += () => { var ignored = RefreshPortsAsync(); };

            LoadSettingsIntoUi();
            loading = false;

            UpdateLayoutUi();
            UpdateFirmwareSourceUi();
            UpdateTestModeUi();
            TextPluginVersion.Text = "Plugin version: " + PluginUpdater.CurrentVersion;

            var ignoredPorts = RefreshPortsAsync();
            var ignoredReleases = LoadReleasesAsync(userInitiated: false);

            // Channels connected at SimHub start (auto-connect) still need their configuration
            foreach (var channel in plugin.ActiveChannels.Where(c => c.IsOpen && !c.ConfigRead))
            {
                var ignored = channel.AfterConnectAsync(waitForGreeting: true);
            }
        }

        private TensionerChannel ChannelA => settings.Dual ? plugin.LeftChannel : plugin.SingleChannel;
        private TensionerChannel ChannelB => settings.Dual ? plugin.RightChannel : null;

        // ==================================================================
        // Settings <-> UI
        // ==================================================================

        private void LoadSettingsIntoUi()
        {
            if (settings.Dual) RadioDual.IsChecked = true; else RadioSingle.IsChecked = true;

            ToggleAutoConnect.IsChecked = settings.AutoConnect;
            ToggleLiveStatus.IsChecked = settings.LiveStatus;
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

            TextMotionFrequency.Text = settings.StepperFrequency.ToString(CultureInfo.InvariantCulture);
            TextMotionAcceleration.Text = settings.StepperAcceleration.ToString(CultureInfo.InvariantCulture);
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

        /// <summary>Message for the status list (SYSTEM tab) and the log.</summary>
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

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TextLog.Clear();
        }

        private TensionerChannel LogChannel => ComboLogChannel.SelectedItem as TensionerChannel ?? ChannelA;

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            SendConsoleCommand();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            LogChannel.Send("HELP");
        }

        private void BtnSettingsCmd_Click(object sender, RoutedEventArgs e)
        {
            LogChannel.Send("SETTINGS");
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
            if (LogChannel.Send(cmd)) TextCommand.Clear();
        }

        // ==================================================================
        // Layout (single / dual)
        // ==================================================================

        private void Layout_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            bool dual = RadioDual.IsChecked == true;
            if (dual == settings.Dual) return;

            // Close the channels of the other layout
            foreach (var channel in plugin.ActiveChannels)
            {
                if (channel.IsOpen) channel.Disconnect();
            }
            settings.Dual = dual;
            SaveSettings();
            UpdateLayoutUi();
            var ignored = RefreshPortsAsync();
        }

        private void UpdateLayoutUi()
        {
            bool dual = settings.Dual;
            CardA.Channel = ChannelA;
            CardB.Channel = plugin.RightChannel;
            CardB.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
            ColumnRightCard.Width = new GridLength(dual ? 310 : 0);

            string image = dual ? "setup_dual.png" : "setup_single.png";
            SetupImage.Source = new BitmapImage(new Uri("pack://application:,,,/DiyBeltTensioner;component/Resources/" + image));
            LabelPictureLeft.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
            LabelPictureRight.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;

            // CONFIG channel switcher
            TextCfgA.Text = dual ? "LEFT" : "BELT";
            RadioCfgB.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
            if (!dual || RadioCfgA.IsChecked == true || cfgChannel == null)
            {
                RadioCfgA.IsChecked = true;
                SelectCfgChannel(ChannelA);
            }
            else
            {
                SelectCfgChannel(plugin.RightChannel);
            }

            // FIRMWARE rows
            TextFlashTitleA.Text = ChannelA.Title;
            var rowB = dual ? Visibility.Visible : Visibility.Collapsed;
            TextFlashTitleB.Visibility = rowB;
            TextFlashPortB.Visibility = rowB;
            ComboBoardB.Visibility = rowB;
            BtnFlashB.Visibility = rowB;
            PopulateBoards();

            // SimHub Motion
            LabelReverseA.Text = dual ? "Reverse left" : "Reverse direction";
            PanelReverseB.Visibility = rowB;
            ToggleReverseA.IsChecked = ChannelA.Settings.MotionReverse;
            ToggleReverseB.IsChecked = plugin.RightChannel.Settings.MotionReverse;

            // Console target
            ComboLogChannel.ItemsSource = plugin.ActiveChannels.ToList();
            ComboLogChannel.DisplayMemberPath = "Title";
            ComboLogChannel.SelectedIndex = 0;

            RefreshAll();
        }

        private void RefreshAll()
        {
            CardA.Refresh();
            if (settings.Dual) CardB.Refresh();
            UpdateConnectAllButton();
            UpdateSaveStates();
            UpdateFlashPortTexts();
            ShowCfgChannel();
            UpdateTestUi();
        }

        // ==================================================================
        // Channel events
        // ==================================================================

        private void OnChannelChanged(TensionerChannel channel)
        {
            if (!plugin.ActiveChannels.Contains(channel)) return;
            if (channel == ChannelA) CardA.Refresh();
            if (channel == ChannelB) CardB.Refresh();
            if (channel == cfgChannel) ShowCfgChannel();
            if (!channel.IsOpen && channel.Tester.IsRunning == false) UpdateTestUi();
            UpdateConnectAllButton();
            UpdateSaveStates();
            UpdateFlashPortTexts();
        }

        private void OnChannelState(TensionerChannel channel)
        {
            if (channel == ChannelA) CardA.RefreshStatus();
            if (channel == ChannelB) CardB.RefreshStatus();
            if (channel == cfgChannel) ShowCfgStatus();
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (!settings.LiveStatus) return;
            foreach (var channel in plugin.ActiveChannels)
            {
                if (!channel.IsOpen) continue;
                if (registerWindow != null && registerWindowChannel == channel && registerWindow.IsBusy) continue;
                channel.PollStatus();
            }
        }

        // ==================================================================
        // Ports & connection
        // ==================================================================

        private async Task RefreshPortsAsync()
        {
            if (refreshingPorts) return;
            refreshingPorts = true;
            try
            {
                currentPorts = await Task.Run(() => ComPortInfo.GetPresentPorts(true));

                // Assign detected belt tensioners to channels without a port
                var used = new HashSet<string>(plugin.ActiveChannels.Select(c => c.Settings.ComPort).Where(p => !string.IsNullOrEmpty(p)), StringComparer.OrdinalIgnoreCase);
                bool assigned = false;
                foreach (var channel in plugin.ActiveChannels.Where(c => string.IsNullOrEmpty(c.Settings.ComPort)))
                {
                    var free = currentPorts.FirstOrDefault(p => p.Kind == DeviceKind.BeltTensioner && !used.Contains(p.PortName));
                    if (free == null) break;
                    channel.Settings.ComPort = free.PortName;
                    used.Add(free.PortName);
                    assigned = true;
                }
                if (assigned) SaveSettings();

                CardA.SetPorts(currentPorts);
                CardB.SetPorts(currentPorts);
                RefreshAll();
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

        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            var ignored = RefreshPortsAsync();
        }

        private void OnCardPortChanged()
        {
            if (settings.Dual && !string.IsNullOrEmpty(plugin.LeftChannel.Settings.ComPort) &&
                string.Equals(plugin.LeftChannel.Settings.ComPort, plugin.RightChannel.Settings.ComPort, StringComparison.OrdinalIgnoreCase))
            {
                Status("Left and right belt use the same COM port. Every belt needs its own controller board.");
            }
            SaveSettings();
            UpdateFlashPortTexts();
        }

        private void UpdateConnectAllButton()
        {
            var channels = plugin.ActiveChannels;
            bool allOpen = channels.All(c => c.IsOpen);
            TextConnectAll.Text = allOpen ? (channels.Count > 1 ? "Disconnect all" : "Disconnect") : (channels.Count > 1 ? "Connect all" : "Connect");
        }

        private async void BtnConnectAll_Click(object sender, RoutedEventArgs e)
        {
            var channels = plugin.ActiveChannels;
            if (channels.All(c => c.IsOpen))
            {
                foreach (var c in channels) c.Disconnect();
                return;
            }
            BtnConnectAll.IsEnabled = false;
            try
            {
                foreach (var c in channels.Where(c => !c.IsOpen))
                {
                    if (string.IsNullOrEmpty(c.Settings.ComPort))
                    {
                        Status(c.Title + ": select a COM port first.");
                        continue;
                    }
                    await c.ConnectAsync();
                }
            }
            finally
            {
                BtnConnectAll.IsEnabled = true;
                RefreshAll();
            }
        }

        private async void BtnScanPorts_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            var openPorts = new HashSet<string>(plugin.AllChannels.Where(c => c.IsOpen).Select(c => c.Link.PortName), StringComparer.OrdinalIgnoreCase);
            var candidates = currentPorts.Where(p =>
                    (p.Kind == DeviceKind.UsbUart || p.Kind == DeviceKind.Unknown || p.Kind == DeviceKind.EspNativeUsb) && !openPorts.Contains(p.PortName))
                .ToList();
            if (candidates.Count == 0)
            {
                Status("No unidentified USB-UART ports.");
                return;
            }

            var answer = MessageBox.Show(
                "Open " + string.Join(", ", candidates.Select(p => p.PortName)) + " briefly to look for the belt tensioner firmware?\n\n" +
                "Opening a CP2102 port resets the ESP32 behind it. Ports used by SimHub or other programs are skipped.",
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
                    ComPortInfo.RememberKind(port.PortName, kind);
                    Status(port.PortName + ": " + (kind == DeviceKind.BeltTensioner ? "belt tensioner firmware found" : "no belt tensioner firmware"));
                }
                await RefreshPortsAsync();
            }
            finally
            {
                busy = false;
                BtnScanPorts.IsEnabled = true;
            }
        }

        // ==================================================================
        // Save / discard (SETUP: all channels, CONFIG: shown channel)
        // ==================================================================

        private IEnumerable<TensionerChannel> ChannelsFor(object sender)
        {
            string scope = (sender as FrameworkElement)?.Tag as string;
            if (scope == "cfg") return cfgChannel != null ? new[] { cfgChannel } : new TensionerChannel[0];
            return plugin.ActiveChannels;
        }

        private static string SaveStateText(TensionerChannel c)
        {
            if (!c.IsOpen) return "not connected";
            if (!c.ConfigRead) return "reading...";
            if (c.PendingCount > 0) return c.PendingCount + " unsaved change" + (c.PendingCount == 1 ? "" : "s");
            if (c.RestartPending) return "restart to apply saved values";
            return c.SettingsSupported ? "in sync" : "in sync (update the firmware for all settings)";
        }

        private void UpdateSaveStates()
        {
            var channels = plugin.ActiveChannels;
            TextSetupSaveState.Text = channels.Count == 1
                ? SaveStateText(channels[0])
                : string.Join("   ·   ", channels.Select(c => c.Title + ": " + SaveStateText(c)));
            if (cfgChannel != null) TextCfgSaveState.Text = cfgChannel.Title + ": " + SaveStateText(cfgChannel);
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            var channels = ChannelsFor(sender).Where(c => c.IsOpen && c.ConfigRead).ToList();
            if (channels.Count == 0)
            {
                Status("Connect to the controller first.");
                return;
            }
            CardA.CommitTypedValues();
            if (settings.Dual) CardB.CommitTypedValues();

            var toSave = channels.Where(c => c.PendingCount > 0).ToList();
            if (toSave.Count == 0 && !channels.Any(c => c.RestartPending))
            {
                Status("Nothing to save.");
                return;
            }

            busy = true;
            try
            {
                foreach (var c in toSave)
                {
                    if (c.EffectiveSetting("IDLE_POSITION") < c.EffectiveSetting("MIN_LIMIT"))
                    {
                        Status(c.Title + ": note, the park position is below the min. limit.");
                    }
                    c.Busy = true;
                    try
                    {
                        if (!await c.SaveAsync()) return;
                    }
                    finally
                    {
                        c.Busy = false;
                    }
                }

                // Saved values are applied by a restart of the controller
                var restart = channels.Where(c => c.RestartPending).ToList();
                if (restart.Count > 0)
                {
                    var answer = MessageBox.Show(
                        "The new values are saved and applied after a restart of the controller" + (restart.Count > 1 ? "s" : "") + ".\n\nRestart now?",
                        "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Yes)
                    {
                        foreach (var c in restart)
                        {
                            c.Busy = true;
                            try
                            {
                                await c.RestartAsync();
                            }
                            finally
                            {
                                c.Busy = false;
                            }
                        }
                    }
                }
            }
            finally
            {
                busy = false;
                RefreshAll();
            }
        }

        private void BtnDiscard_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in ChannelsFor(sender)) c.DiscardEdits();
            RefreshAll();
        }

        private async void BtnResetConfig_Click(object sender, RoutedEventArgs e)
        {
            var c = cfgChannel;
            if (c == null || !c.IsOpen || busy) return;
            var answer = MessageBox.Show(
                "Delete all saved values of the " + c.Title.ToLowerInvariant() + " controller (pitch, range, direction, positions, idle, homing)?\nThe firmware defaults (Config.h) apply after a restart.",
                "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            busy = true;
            try
            {
                await c.ResetConfigAsync();
            }
            finally
            {
                busy = false;
            }
        }

        // ==================================================================
        // CONFIG tab
        // ==================================================================

        private void CfgChannel_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            SelectCfgChannel(RadioCfgB.IsChecked == true && settings.Dual ? plugin.RightChannel : ChannelA);
        }

        private void SelectCfgChannel(TensionerChannel channel)
        {
            if (channel == cfgChannel) return;
            if (cfgChannel != null && cfgChannel.Tester.IsRunning)
            {
                cfgChannel.Tester.Stop(park: true);
                Status(cfgChannel.Title + ": test stopped (other channel selected).");
            }
            cfgChannel = channel;
            ShowCfgChannel();
            UpdateTestUi();
        }

        /// <summary>Puts the effective values of the CONFIG channel into the controls.</summary>
        private void ShowCfgChannel()
        {
            var c = cfgChannel;
            if (c == null) return;
            TextCfgTitle.Text = c.Title.ToUpperInvariant();
            ShowCfgStatus();

            applying = true;
            try
            {
                foreach (var slider in settingSliders)
                {
                    slider.Value = c.EffectiveSetting((string)slider.Tag);
                }
                foreach (var toggle in settingToggles)
                {
                    toggle.IsChecked = c.EffectiveSetting((string)toggle.Tag) >= 0.5;
                }
            }
            finally
            {
                applying = false;
            }

            bool canEdit = c.IsOpen && c.ConfigRead && c.SettingsSupported && !c.Busy;
            foreach (var slider in settingSliders) slider.IsEnabled = canEdit;
            foreach (var toggle in settingToggles) toggle.IsEnabled = canEdit;

            Travel.RangeMm = c.Range;
            Travel.MinPct = c.EffectiveSetting("MIN_LIMIT");
            Travel.MaxPct = c.EffectiveSetting("MAX_LIMIT");
            Travel.IdlePct = c.EffectiveSetting("IDLE_POSITION");
            Travel.Inverted = c.EffectiveSetting("INVERT_TENSION") >= 0.5;
            Travel.CenterAfterHoming = c.EffectiveSetting("CENTER_AFTER_HOMING") >= 0.5;
            Travel.ActualPct = c.ActualPositionPercent;
            Travel.Refresh();
            UpdateSaveStates();
        }

        private void ShowCfgStatus()
        {
            var c = cfgChannel;
            if (c == null) return;
            TextCfgStatus.Text = c.StatusText;
            TextCfgTelemetry.Text = c.TelemetryText;
            int severity = c.Severity;
            CfgDot.Fill = new SolidColorBrush(severity < 0 ? Color.FromRgb(0x66, 0x66, 0x66)
                : severity == 0 ? Color.FromRgb(0x4C, 0xAF, 0x50)
                : severity == 1 ? Color.FromRgb(0xFB, 0xBF, 0x24)
                : Color.FromRgb(0xE5, 0x39, 0x35));
            double actual = c.ActualPositionPercent;
            if (!Equals(Travel.ActualPct, actual))
            {
                Travel.ActualPct = actual;
                Travel.Refresh();
            }
        }

        private void SettingSlider_ValueCommitted(object sender, double value)
        {
            if (applying || loading || cfgChannel == null) return;
            cfgChannel.SetSetting((string)((LabeledSlider)sender).Tag, value);
        }

        private void SettingToggle_Click(object sender, RoutedEventArgs e)
        {
            if (applying || loading || cfgChannel == null) return;
            var toggle = (SHToggleButton)sender;
            cfgChannel.SetSetting((string)toggle.Tag, toggle.IsChecked == true ? 1 : 0);
        }

        private bool EnsureCfgConnected()
        {
            if (cfgChannel != null && cfgChannel.IsOpen) return true;
            Status((cfgChannel != null ? cfgChannel.Title : "Belt") + ": not connected. Connect on the SETUP tab.");
            return false;
        }

        private void BtnCfgHome_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureCfgConnected()) cfgChannel.Home();
        }

        private void BtnCfgPark_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected()) return;
            cfgChannel.Park();
            UpdateTestUi();
        }

        private async void BtnCfgRestart_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected() || busy) return;
            busy = true;
            cfgChannel.Busy = true;
            try
            {
                await cfgChannel.RestartAsync();
            }
            finally
            {
                cfgChannel.Busy = false;
                busy = false;
                RefreshAll();
            }
        }

        private async void BtnCfgRead_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected()) return;
            await cfgChannel.ReadAllAsync();
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

        private void ApplyTestParameters()
        {
            if (loading) return;
            int mode = Math.Max(0, ComboTestMode.SelectedIndex);
            settings.TestMode = mode;
            settings.TestPositionPct = SliderHold.Value;
            settings.TestSineCenterPct = SliderSineCenter.Value;
            settings.TestSineAmplitudePct = SliderSineAmplitude.Value;
            settings.TestSineFrequencyHz = SliderSineFrequency.Value;
            settings.TestPulseBasePct = SliderPulseBase.Value;
            settings.TestPulsePeakPct = SliderPulsePeak.Value;
            settings.TestPulseDurationMs = (int)SliderPulseDuration.Value;
            settings.TestPulseIntervalMs = (int)SliderPulseInterval.Value;

            if (cfgChannel == null) return;
            var t = cfgChannel.Tester;
            t.Mode = mode == 1 ? TestMode.Sine : mode == 2 ? TestMode.BrakePulse : TestMode.Hold;
            t.HoldPercent = settings.TestPositionPct;
            t.SineCenter = settings.TestSineCenterPct;
            t.SineAmplitude = settings.TestSineAmplitudePct;
            t.SineFrequencyHz = settings.TestSineFrequencyHz;
            t.PulseBase = settings.TestPulseBasePct;
            t.PulsePeak = settings.TestPulsePeakPct;
            t.PulseDurationMs = settings.TestPulseDurationMs;
            t.PulseIntervalMs = settings.TestPulseIntervalMs;
            t.Axis1 = true;
            t.Axis2 = false;
            t.IdleInputPercent = cfgChannel.IdleInputPercent;
        }

        private static bool TryParseInt(string text, int min, int max, out int value)
        {
            return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        /// <summary>SimHub Motion limits of the SETUP tab (also used by the test).</summary>
        private bool ReadMotionLimits(out int frequency, out int acceleration)
        {
            acceleration = 0;
            if (!TryParseInt(TextMotionFrequency.Text, 1, 36000, out frequency))
            {
                Status("Max. pulse frequency must be 1 - 36000 (SETUP tab).");
                return false;
            }
            if (!TryParseInt(TextMotionAcceleration.Text, 1, 100000, out acceleration))
            {
                Status("Max. acceleration must be 1 - 100000 (SETUP tab).");
                return false;
            }
            settings.StepperFrequency = frequency;
            settings.StepperAcceleration = acceleration;
            return true;
        }

        private void BtnTestStart_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected()) return;
            int frequency, acceleration;
            if (!ReadMotionLimits(out frequency, out acceleration)) return;
            ApplyTestParameters();
            SaveSettings();
            try
            {
                cfgChannel.Tester.Start(frequency, acceleration);
                Status(cfgChannel.Title + ": test started (" + ComboTestMode.Text + ").");
            }
            catch (Exception ex)
            {
                Status(cfgChannel.Title + ": test start failed: " + ex.Message);
            }
            UpdateTestUi();
        }

        private void BtnTestStop_Click(object sender, RoutedEventArgs e)
        {
            cfgChannel?.Tester.Stop(park: true);
            SaveSettings();
            UpdateTestUi();
        }

        private void UpdateTestUi()
        {
            bool running = cfgChannel != null && cfgChannel.Tester.IsRunning;
            BtnTestStart.IsEnabled = !running && cfgChannel != null && cfgChannel.IsOpen;
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
        // Servo
        // ==================================================================

        private async void BtnFlashServo_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected() || busy) return;
            var c = cfgChannel;
            var answer = MessageBox.Show(
                "Write the tuned parameters into the EEPROM of the " + c.Title.ToLowerInvariant() + " servo?\n\n" +
                "- The servo must be powered and connected to the controller.\n" +
                "- SimHub Motion must not use the controller.\n" +
                "- Power cycle the servo afterwards.",
                "Flash servo parameters", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            busy = true;
            BtnFlashServo.IsEnabled = false;
            try
            {
                UpdateTestUi();
                if (await c.FlashServoAsync())
                {
                    MessageBox.Show("Servo parameters saved.\n\nPower cycle the servo (switch its power supply off and on) to activate them.",
                        "Flash servo parameters", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (!EnsureCfgConnected() || busy) return;
            busy = true;
            BtnDiffServo.IsEnabled = false;
            try
            {
                await cfgChannel.DiffServoAsync();
            }
            finally
            {
                busy = false;
                BtnDiffServo.IsEnabled = true;
            }
        }

        private void BtnServoRegisters_Click(object sender, RoutedEventArgs e)
        {
            if (registerWindow != null)
            {
                if (registerWindow.WindowState == WindowState.Minimized) registerWindow.WindowState = WindowState.Normal;
                registerWindow.Activate();
                return;
            }
            if (!EnsureCfgConnected()) return;
            if (busy)
            {
                Status("Wait until the current action has finished.");
                return;
            }
            cfgChannel.Tester.Stop(park: false);
            UpdateTestUi();

            registerWindowChannel = cfgChannel;
            registerWindow = new ServoRegisterWindow(cfgChannel.Link, 1, Log);
            registerWindow.Title = "Servo Register Editor - iSV57 - " + cfgChannel.Title;
            registerWindow.Owner = Window.GetWindow(this);
            registerWindow.Closed += (s, args) =>
            {
                registerWindow = null;
                registerWindowChannel = null;
            };
            registerWindow.Show();
            var ignored = registerWindow.LoadAsync();
        }

        private void BtnEnableServo_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureCfgConnected()) cfgChannel.Send("ENABLE_SERVO 1");
        }

        private void BtnDisableServo_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected()) return;
            cfgChannel.Tester.Stop(park: false);
            UpdateTestUi();
            cfgChannel.Send("DISABLE_SERVO");
        }

        // ==================================================================
        // Web graphs
        // ==================================================================

        private static readonly Regex UrlPattern = new Regex(@"http://[^\s""]+?/");

        /// <summary>
        /// Starts the web graphs of the CONFIG channel if needed (WEBGRAPHS ON), waits for the WiFi
        /// connection (WEBGRAPHS) and opens the page in the browser.
        /// </summary>
        private async void BtnWebGraphs_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCfgConnected() || busy) return;
            var c = cfgChannel;
            busy = true;
            BtnWebGraphs.IsEnabled = false;
            try
            {
                var status = await QueryWebGraphsAsync(c);
                if (status.Count == 0)
                {
                    Status(c.Title + ": no answer to WEBGRAPHS.");
                    return;
                }
                if (status.Any(l => l.Contains("disabled in this firmware")))
                {
                    Status(c.Title + ": the web graphs are not compiled into this firmware (ENABLE_WEBGRAPHS false in Config.h).");
                    return;
                }

                if (status.Any(l => l.Contains("off (")))
                {
                    var answer = MessageBox.Show(
                        "Enable the web graphs of the " + c.Title.ToLowerInvariant() + " controller?\n\nIt starts WiFi now and at every boot: it joins the WiFi saved on its web page or opens the access point 'diy_belt_tensioner'.",
                        "Web graphs", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (answer != MessageBoxResult.OK) return;
                    Log("> WEBGRAPHS ON");
                    var reply = await c.Link.RequestAsync("WEBGRAPHS ON", l => l.StartsWith("Web graphs"), 2000, 0);
                    if (reply.Count == 0)
                    {
                        Status(c.Title + ": no answer to WEBGRAPHS ON. Flash the latest firmware to switch the web graphs from the plugin.");
                        return;
                    }
                    Status(c.Title + ": " + reply.Last());
                    var ignored = c.ReadAllAsync();
                }

                // WiFi bring-up takes a few seconds (joining a network up to 15 s, then access point)
                Status(c.Title + ": waiting for the controller's WiFi...");
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 25 && !status.Any(l => UrlPattern.IsMatch(l)))
                {
                    await Task.Delay(1500);
                    if (!c.IsOpen) return;
                    status = await QueryWebGraphsAsync(c);
                }

                string sta = status.FirstOrDefault(l => l.Contains("connected to WiFi") && UrlPattern.IsMatch(l));
                string ap = status.FirstOrDefault(l => l.Contains("access point") && UrlPattern.IsMatch(l));
                if (sta == null && ap == null)
                {
                    Status(c.Title + ": the web graphs did not report an address yet. Try again in a few seconds.");
                    return;
                }

                string url = UrlPattern.Match(sta ?? ap).Value;
                string text = sta != null
                    ? "The controller is in your WiFi:\n" + sta.Replace("Web graphs: ", "") + "\n\nOpen " + url + " in the browser?"
                    : "The controller opened its own access point:\n" + ap.Replace("Web graphs: ", "") +
                      "\n\nConnect this PC (or a phone) to that WiFi, then open " + url + ".\nOpen it in the browser now?";
                Status(c.Title + ": web graphs at " + url);
                if (MessageBox.Show(text, "Web graphs", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    OpenUrl(url);
                }
            }
            finally
            {
                busy = false;
                BtnWebGraphs.IsEnabled = true;
            }
        }

        private async Task<List<string>> QueryWebGraphsAsync(TensionerChannel c)
        {
            Log("> WEBGRAPHS");
            // WiFi and access point may print one line each
            var lines = await c.Link.RequestAsync("WEBGRAPHS", l => false, 1500, 300);
            return lines.Where(l => l.StartsWith("Web graphs")).ToList();
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                Status("Could not open the browser: " + ex.Message);
            }
        }

        // ==================================================================
        // SimHub Motion
        // ==================================================================

        private void ToggleReverse_Click(object sender, RoutedEventArgs e)
        {
            ChannelA.Settings.MotionReverse = ToggleReverseA.IsChecked == true;
            plugin.RightChannel.Settings.MotionReverse = ToggleReverseB.IsChecked == true;
            SaveSettings();
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            int frequency, acceleration;
            if (!ReadMotionLimits(out frequency, out acceleration)) return;
            SaveSettings();

            MotionControllerSetup first, right = null;
            if (settings.Dual)
            {
                first = new MotionControllerSetup { ComPort = settings.Left.ComPort, Reverse = settings.Left.MotionReverse };
                right = new MotionControllerSetup { ComPort = settings.Right.ComPort, Reverse = settings.Right.MotionReverse };
                if (string.IsNullOrEmpty(first.ComPort) || string.IsNullOrEmpty(right.ComPort))
                {
                    MessageBox.Show("Select the COM ports of the left and the right belt on the SETUP tab first.", "SimHub Motion preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (string.Equals(first.ComPort, right.ComPort, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Left and right belt need different COM ports (one controller board per belt).", "SimHub Motion preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else
            {
                first = new MotionControllerSetup { ComPort = settings.Single.ComPort, Reverse = settings.Single.MotionReverse };
                if (string.IsNullOrEmpty(first.ComPort))
                {
                    MessageBox.Show("Select the COM port of the belt tensioner on the SETUP tab first.", "SimHub Motion preset", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

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
                FileName = settings.Dual ? "DIY_BeltTensioner_Dual.shmotionoutput" : "DIY_BeltTensioner_Single.shmotionoutput",
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

        private void BtnReleasePorts_Click(object sender, RoutedEventArgs e)
        {
            int closed = 0;
            foreach (var c in plugin.AllChannels.Where(c => c.IsOpen))
            {
                c.Disconnect();
                closed++;
            }
            UpdateTestUi();
            Status(closed > 0 ? "Ports released. You can enable SimHub Motion now." : "The plugin does not use any port, SimHub Motion can use them.");
        }

        private void BtnOpenMotionDocs_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/" + settings.GitHubRepository + "/blob/main/docs/SimHub_Motion_Setup.md");
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

        private void UpdateFlashPortTexts()
        {
            TextFlashPortA.Text = PortText(ChannelA);
            TextFlashPortB.Text = PortText(plugin.RightChannel);
        }

        private string PortText(TensionerChannel c)
        {
            if (string.IsNullOrEmpty(c.Settings.ComPort)) return "- (select it on the SETUP tab)";
            var port = currentPorts.FirstOrDefault(p => string.Equals(p.PortName, c.Settings.ComPort, StringComparison.OrdinalIgnoreCase));
            string text = port != null ? port.DisplayName : c.Settings.ComPort + "  (not present)";
            if (c.IsOpen && c.Link.FirmwareVersion.Length > 0) text += "  ·  protocol " + c.Link.FirmwareVersion;
            return text;
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
            bool local = settings.FirmwareSource != 0;
            TextLocalFirmware.IsEnabled = local;
            BtnBrowseFirmware.IsEnabled = local;
            BtnCheckRelease.IsEnabled = !local;
            PopulateBoards();
        }

        /// <summary>
        /// Board lists: the variants of the latest release for the GitHub source, all known boards
        /// for local firmware (or while the release is not loaded).
        /// </summary>
        private void PopulateBoards()
        {
            List<BoardInfo> boards = null;
            if (settings.FirmwareSource == 0 && latestRelease != null) boards = BoardInfo.FromRelease(latestRelease);
            if (boards == null || boards.Count == 0) boards = BoardInfo.All.ToList();

            bool wasLoading = loading;
            loading = true; // keep the saved boards while the lists are replaced
            try
            {
                FillBoardCombo(ComboBoardA, boards, ChannelA.Settings.BoardEnvironment);
                FillBoardCombo(ComboBoardB, boards, plugin.RightChannel.Settings.BoardEnvironment);
            }
            finally
            {
                loading = wasLoading;
            }
            ShowReleaseInfo();
        }

        private static void FillBoardCombo(ComboBox combo, List<BoardInfo> boards, string environment)
        {
            combo.ItemsSource = boards;
            combo.SelectedItem = boards.FirstOrDefault(b => string.Equals(b.Environment, environment, StringComparison.OrdinalIgnoreCase)) ?? boards.FirstOrDefault();
        }

        private void ComboBoard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) return;
            var combo = (ComboBox)sender;
            var board = combo.SelectedItem as BoardInfo;
            if (board == null) return;
            var channel = combo == ComboBoardB ? plugin.RightChannel : ChannelA;
            channel.Settings.BoardEnvironment = board.Environment;
            SaveSettings();
            ShowReleaseInfo();
        }

        private void ShowReleaseInfo()
        {
            if (settings.FirmwareSource != 0)
            {
                TextReleaseInfo.Text = "Make sure the local firmware was built for the selected board.";
                return;
            }
            if (latestRelease == null)
            {
                TextReleaseInfo.Text = "";
                return;
            }
            var missing = new[] { ComboBoardA, ComboBoardB }
                .Where(cb => cb.Visibility == Visibility.Visible)
                .Select(cb => cb.SelectedItem as BoardInfo)
                .Where(b => b != null && latestRelease.FindAsset(b.Environment) == null)
                .Select(b => b.Environment)
                .ToList();
            TextReleaseInfo.Text = missing.Count == 0
                ? "Release " + latestRelease.Tag + " contains firmware for the selected boards."
                : "Release " + latestRelease.Tag + " has no firmware for " + string.Join(", ", missing) + ".";
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

        private void BtnOpenRelease_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(latestRelease?.HtmlUrl ?? "https://github.com/" + settings.GitHubRepository + "/releases/latest");
        }

        /// <summary>
        /// Reads the latest release: shows it, fills the board variants and checks for a plugin
        /// update (at start only if enabled; userInitiated = the user asked for it).
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
                BtnCheckRelease.IsEnabled = settings.FirmwareSource == 0;
            }
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
                        if (!Path.GetFileNameWithoutExtension(zip).EndsWith("_" + board.Environment, StringComparison.OrdinalIgnoreCase))
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
                        FlashLog("Release " + latestRelease.Tag);
                        string folder = await FirmwareReleaseService.DownloadAndExtractAsync(latestRelease, board.Environment, FlashLog);
                        return FirmwareImage.FromPath(folder);
                    }
            }
        }

        private async void BtnFlash_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            bool b = (string)((FrameworkElement)sender).Tag == "B";
            var channel = b ? plugin.RightChannel : ChannelA;
            var board = (b ? ComboBoardB : ComboBoardA).SelectedItem as BoardInfo;
            string portName = channel.Settings.ComPort;
            if (board == null || string.IsNullOrEmpty(portName))
            {
                FlashLog(channel.Title + ": select the COM port (SETUP tab) and the board first.");
                return;
            }
            var port = currentPorts.FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
            if (port != null && port.IsPedalDevice)
            {
                var answer = MessageBox.Show(portName + " is a DIY FFB Pedal device (" + port.KindText + "). Flash the belt tensioner firmware onto it anyway?",
                    "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }
            TextLocalFirmware_LostFocus(null, null);

            busy = true;
            channel.Busy = true;
            BtnFlashA.IsEnabled = false;
            BtnFlashB.IsEnabled = false;
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

                if (channel.IsOpen)
                {
                    channel.Disconnect();
                    UpdateTestUi();
                    await Task.Delay(400);
                }

                FlashLog("");
                FlashLog($"=== {channel.Title}: flashing {board.Environment} on {portName} ===");
                var flasher = new EspFlasher();
                flasher.Output += FlashLog;
                bool ok = await flasher.FlashAsync(portName, board, image);
                Status(channel.Title + (ok ? ": firmware flashed." : ": flashing failed (see FIRMWARE tab)."));

                if (ok && ToggleReconnectAfterFlash.IsChecked == true)
                {
                    FlashLog("Waiting for the controller to restart...");
                    await channel.ReconnectAsync(15000);
                }
            }
            finally
            {
                channel.Busy = false;
                busy = false;
                BtnFlashA.IsEnabled = true;
                BtnFlashB.IsEnabled = true;
                RefreshAll();
            }
        }

        // ==================================================================
        // SYSTEM: plugin settings & updates
        // ==================================================================

        private void ToggleAutoConnect_Click(object sender, RoutedEventArgs e)
        {
            settings.AutoConnect = ToggleAutoConnect.IsChecked == true;
            SaveSettings();
        }

        private void ToggleLiveStatus_Click(object sender, RoutedEventArgs e)
        {
            settings.LiveStatus = ToggleLiveStatus.IsChecked == true;
            SaveSettings();
        }

        private void ToggleCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            settings.CheckPluginUpdates = ToggleCheckUpdates.IsChecked == true;
            SaveSettings();
        }

        private async void BtnCheckPluginUpdate_Click(object sender, RoutedEventArgs e)
        {
            await LoadReleasesAsync(userInitiated: true);
        }

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

        private void BtnReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            if (pendingPluginUpdate != null) OpenUrl(pendingPluginUpdate.HtmlUrl);
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

            // Release the controllers before SimHub closes
            foreach (var c in plugin.AllChannels) c.Disconnect();
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
    }
}
