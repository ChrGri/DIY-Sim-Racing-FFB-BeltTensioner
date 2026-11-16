using DiyBeltTensioner.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DiyBeltTensioner.UI
{
    /// <summary>
    /// Card of one belt channel on the SETUP tab: COM port and connection, status, spindle pitch,
    /// working range (with auto-detect) and quick actions.
    /// </summary>
    public partial class ChannelCard : UserControl
    {
        private static readonly string[] CommonPitches = { "4", "5", "8", "10", "16", "20", "25" };

        private TensionerChannel channel;
        private bool applying;

        /// <summary>The user picked another COM port (the settings need to be saved).</summary>
        public event Action PortChanged;

        /// <summary>The port list should be refreshed (drop-down opened).</summary>
        public event Action RefreshPortsRequested;

        public ChannelCard()
        {
            InitializeComponent();
            ComboPitch.ItemsSource = CommonPitches;
            SliderRange.ValueCommitted += (s, v) =>
            {
                if (!applying && channel != null) channel.SetRange(v);
            };
        }

        public TensionerChannel Channel
        {
            get { return channel; }
            set
            {
                channel = value;
                Refresh();
            }
        }

        // ------------------------------------------------------------------
        // Display
        // ------------------------------------------------------------------

        public void SetPorts(IList<ComPortInfo> ports)
        {
            applying = true;
            try
            {
                ComboPort.ItemsSource = ports;
                string configured = channel?.Settings.ComPort;
                ComboPort.SelectedItem = ports.FirstOrDefault(p => string.Equals(p.PortName, configured, StringComparison.OrdinalIgnoreCase));
                if (ComboPort.SelectedItem == null && !string.IsNullOrEmpty(configured))
                {
                    // Configured port is not present: show it anyway
                    var missing = new List<ComPortInfo>(ports) { new ComPortInfo { PortName = configured, Description = "not present", Kind = DeviceKind.Other } };
                    ComboPort.ItemsSource = missing;
                    ComboPort.SelectedItem = missing.Last();
                }
            }
            finally
            {
                applying = false;
            }
        }

        /// <summary>Updates all fields from the channel (UI thread).</summary>
        public void Refresh()
        {
            if (channel == null) return;
            applying = true;
            try
            {
                TextTitle.Text = channel.Title.ToUpperInvariant();
                bool open = channel.IsOpen;
                BtnConnect.Content = open ? "Disconnect" : "Connect";
                ComboPort.IsEnabled = !open;
                RefreshStatus();

                TextFirmware.Text = open
                    ? "Protocol " + (channel.Link.FirmwareVersion.Length > 0 ? channel.Link.FirmwareVersion : "?") +
                      (channel.ConfigRead ? (channel.SettingsSupported ? "" : "  ·  update the firmware for all settings") : "")
                    : "";

                // Do not overwrite a value the user is typing
                if (!ComboPitch.IsKeyboardFocusWithin)
                {
                    ComboPitch.Text = channel.Pitch.ToString("0.##", CultureInfo.InvariantCulture);
                }
                SliderRange.Value = channel.Range;

                bool canEdit = open && channel.ConfigRead && !channel.Busy;
                PanelMechanics.IsEnabled = canEdit;
                BtnHome.IsEnabled = canEdit;
                BtnPark.IsEnabled = canEdit;
                BtnRestart.IsEnabled = open && !channel.Busy;
            }
            finally
            {
                applying = false;
            }
        }

        /// <summary>Only the status line (frequent updates from polling).</summary>
        public void RefreshStatus()
        {
            if (channel == null) return;
            TextStatus.Text = channel.StatusText;
            TextTelemetry.Text = channel.TelemetryText;
            int severity = channel.Severity;
            Dot.Fill = new SolidColorBrush(severity < 0 ? Color.FromRgb(0x66, 0x66, 0x66)
                : severity == 0 ? Color.FromRgb(0x4C, 0xAF, 0x50)
                : severity == 1 ? Color.FromRgb(0xFB, 0xBF, 0x24)
                : Color.FromRgb(0xE5, 0x39, 0x35));
        }

        // ------------------------------------------------------------------
        // Connection
        // ------------------------------------------------------------------

        private void ComboPort_DropDownOpened(object sender, EventArgs e)
        {
            RefreshPortsRequested?.Invoke();
        }

        private void ComboPort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (applying || channel == null) return;
            var port = ComboPort.SelectedItem as ComPortInfo;
            if (port == null) return;
            channel.Settings.ComPort = port.PortName;
            PortChanged?.Invoke();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (channel == null) return;
            if (channel.IsOpen)
            {
                channel.Disconnect();
                return;
            }
            var port = ComboPort.SelectedItem as ComPortInfo;
            if (port == null)
            {
                MessageBox.Show("Select the COM port of the " + channel.Title.ToLowerInvariant() + " first.", "DIY FFB Belt Tensioner", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (port.IsPedalDevice)
            {
                var answer = MessageBox.Show(port.PortName + " is a DIY FFB Pedal device (" + port.KindText + "). Connect anyway?",
                    "DIY FFB Belt Tensioner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }
            channel.Settings.ComPort = port.PortName;
            PortChanged?.Invoke();
            BtnConnect.IsEnabled = false;
            try
            {
                await channel.ConnectAsync();
            }
            finally
            {
                BtnConnect.IsEnabled = true;
                Refresh();
            }
        }

        // ------------------------------------------------------------------
        // Mechanics
        // ------------------------------------------------------------------

        private void ComboPitch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (applying) return;
            var item = ComboPitch.SelectedItem as string;
            if (item != null) CommitPitch(item);
        }

        private void ComboPitch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (applying) return;
            CommitPitch(ComboPitch.Text);
        }

        private void ComboPitch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !applying) CommitPitch(ComboPitch.Text);
        }

        /// <summary>Takes a pitch that is still being typed (before saving).</summary>
        public void CommitTypedValues()
        {
            if (channel != null && channel.ConfigRead) CommitPitch(ComboPitch.Text);
        }

        private void CommitPitch(string text)
        {
            if (channel == null || !channel.ConfigRead) return;
            double value;
            text = (text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0.5 || value > 50)
            {
                applying = true;
                ComboPitch.Text = channel.Pitch.ToString("0.##", CultureInfo.InvariantCulture);
                applying = false;
                return;
            }
            if (Math.Abs(value - channel.Pitch) > 0.0005 || channel.PendingPitch.HasValue) channel.SetPitch(value);
        }

        private async void BtnMeasure_Click(object sender, RoutedEventArgs e)
        {
            if (channel == null || !channel.IsOpen || channel.Busy) return;
            var answer = MessageBox.Show(
                $"Measure the working range of the {channel.Title.ToLowerInvariant()}?\n\n" +
                "The carriage moves slowly to the MIN end stop, then to the MAX end stop (sensorless homing), and back to the park position.\n\n" +
                "- Nobody in the harness, nothing pulling on the belt.\n" +
                "- SimHub Motion must not use the controller.",
                "Auto-detect working range", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            channel.Busy = true;
            Refresh();
            double? measured;
            try
            {
                measured = await channel.MeasureRangeAsync();
            }
            finally
            {
                channel.Busy = false;
                Refresh();
            }
            if (!measured.HasValue) return;
            if (measured.Value < 5 || measured.Value > 500)
            {
                MessageBox.Show($"The measured range ({measured.Value:0.0} mm) is outside 5 - 500 mm and was not taken.", "Auto-detect working range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var take = MessageBox.Show(
                $"Measured working range: {measured.Value:0.0} mm\n(currently configured: {channel.Range:0.0} mm)\n\nUse the measured value? It is written with 'Save'.",
                "Auto-detect working range", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (take != MessageBoxResult.Yes) return;
            channel.SetRange(Math.Floor(measured.Value * 2) / 2); // 0.5 mm steps, never longer than measured
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            channel?.Home();
        }

        private void BtnPark_Click(object sender, RoutedEventArgs e)
        {
            channel?.Park();
        }

        private async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            if (channel == null || !channel.IsOpen || channel.Busy) return;
            channel.Busy = true;
            Refresh();
            try
            {
                await channel.RestartAsync();
            }
            finally
            {
                channel.Busy = false;
                Refresh();
            }
        }
    }
}
