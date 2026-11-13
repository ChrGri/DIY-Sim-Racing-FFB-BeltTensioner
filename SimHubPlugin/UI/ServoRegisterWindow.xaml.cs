using DiyBeltTensioner.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DiyBeltTensioner.UI
{
    /// <summary>
    /// Lists the 305 iSV57 registers of a servo (DUMP_SERVO) next to the tuned table of the
    /// firmware, like the servo register window of the DIY FFB Pedal plugin. Values can be
    /// edited (SERVO_WRITE), stored in the servo EEPROM (SERVO_SAVE), exported and imported.
    /// </summary>
    public partial class ServoRegisterWindow : Window
    {
        private readonly BeltTensionerLink link;
        private readonly Action<string> log;
        private readonly ObservableCollection<ServoRegisterEntry> entries;
        private readonly ICollectionView view;

        private bool busy;

        /// <summary>A command is running that blocks the controller (status polling pauses).</summary>
        public bool IsBusy => busy;
        private volatile bool loadActive;
        private int loadedCount;

        public ServoRegisterWindow(BeltTensionerLink link, int servo, Action<string> log)
        {
            this.link = link;
            this.log = log ?? (s => { });

            InitializeComponent();

            entries = new ObservableCollection<ServoRegisterEntry>(ServoRegisters.CreateTable());
            view = CollectionViewSource.GetDefaultView(entries);
            view.Filter = FilterEntry;
            RegisterGrid.ItemsSource = view;
            ComboServo.SelectedIndex = servo == 2 ? 1 : 0;

            link.LineReceived += Link_LineReceived;
            Closed += (s, e) => link.LineReceived -= Link_LineReceived;
            UpdateSummary();
        }

        private int Servo => ComboServo.SelectedIndex == 1 ? 2 : 1;

        private void SetState(string text)
        {
            TextState.Text = text;
            log("[servo registers] " + text);
        }

        private bool EnsureConnected()
        {
            if (link.IsOpen) return true;
            SetState("Not connected. Connect to the controller in the plugin first.");
            return false;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            foreach (var button in new[] { BtnLoad, BtnWriteTuned, BtnSave, BtnExport, BtnImport })
            {
                button.IsEnabled = !value;
            }
            ComboServo.IsEnabled = !value;
        }

        private void UpdateSummary()
        {
            int read = entries.Count(x => x.LiveValue.HasValue);
            int differ = entries.Count(x => x.Differs);
            int errors = entries.Count(x => x.ReadError);
            TextSummary.Text = read == 0
                ? ServoRegisters.Count + " registers, not read yet"
                : $"{ServoRegisters.Count} registers  |  {differ} differ  |  {errors} read errors";
        }

        private void ClearLiveValues()
        {
            foreach (var entry in entries)
            {
                entry.LiveValue = null;
                entry.ReadError = false;
            }
            UpdateSummary();
            view.Refresh();
        }

        private void ComboServo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (entries == null) return; // during InitializeComponent
            ClearLiveValues();
            SetState("Servo " + Servo + " selected. Click 'Load from servo'.");
        }

        // ------------------------------------------------------------------
        // Reading
        // ------------------------------------------------------------------

        private void Link_LineReceived(string line)
        {
            if (!loadActive) return;
            int index;
            int? actual, tuned;
            bool readError;
            if (!ServoRegisters.TryParseDumpLine(line, out index, out actual, out tuned, out readError)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (index < 0 || index >= entries.Count) return;
                var entry = entries[index];
                entry.ReadError = readError;
                if (!readError)
                {
                    entry.LiveValue = actual;
                    if (tuned.HasValue) entry.TunedValue = tuned;
                }
                loadedCount++;
                Progress.Value = Math.Min(Progress.Maximum, loadedCount);
                TextState.Text = "Reading servo " + Servo + "... " + loadedCount + " / " + ServoRegisters.Count;
            }));
        }

        private async void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        public async Task LoadAsync()
        {
            if (busy || !EnsureConnected()) return;
            SetBusy(true);
            ClearLiveValues();
            loadedCount = 0;
            Progress.Value = 0;
            Progress.Visibility = Visibility.Visible;
            try
            {
                loadActive = true;
                string cmd = "DUMP_SERVO " + Servo;
                log("> " + cmd);
                var reply = await link.RequestAsync(cmd, l => l.StartsWith("Summary:") || l.Contains("is not enabled"), 120000, 10000);
                await Task.Delay(200); // let the queued table updates run
                loadActive = false;

                if (reply.Any(l => l.Contains("is not enabled")))
                {
                    SetState("Servo " + Servo + " is not enabled in this firmware (NUM_ACTUATORS = 1).");
                }
                else if (reply.Count == 0)
                {
                    SetState("No answer from the controller.");
                }
                else
                {
                    string summary = reply.LastOrDefault(l => l.StartsWith("Summary:"));
                    SetState(summary != null ? "Servo " + Servo + ": " + summary.Substring("Summary:".Length).Trim() : "Reading stopped before the end (see Serial Logs).");
                }
            }
            finally
            {
                loadActive = false;
                Progress.Visibility = Visibility.Collapsed;
                SetBusy(false);
                UpdateSummary();
                view.Refresh();
            }
        }

        // ------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------

        private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var entry = e.Row.Item as ServoRegisterEntry;
            if (busy || entry == null || !link.IsOpen || entry.Writing)
            {
                e.Cancel = true;
                if (!link.IsOpen) SetState("Not connected.");
                return;
            }
            entry.EditText = entry.LiveValue.HasValue ? entry.LiveValue.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private void EditBox_Loaded(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            box.Focus();
            box.SelectAll();
        }

        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var entry = e.Row.Item as ServoRegisterEntry;
            if (entry == null) return;

            int value;
            if (!int.TryParse((entry.EditText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < -32768 || value > 32767)
            {
                SetState(entry.Parameter + ": enter a whole number between -32768 and 32767.");
                return;
            }
            if (entry.LiveValue.HasValue && entry.LiveValue.Value == value) return;

            // Write after the edit has finished
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (busy) return;
                SetBusy(true);
                try
                {
                    var result = await WriteRegisterAsync(entry, value);
                    if (result == true)
                    {
                        SetState(entry.Parameter + " = " + value + " written to servo " + Servo + " (RAM). 'Save to EEPROM' makes it permanent.");
                    }
                }
                finally
                {
                    SetBusy(false);
                    UpdateSummary();
                }
            }));
        }

        /// <summary>true = written, false = servo rejected it, null = firmware has no SERVO_WRITE.</summary>
        private async Task<bool?> WriteRegisterAsync(ServoRegisterEntry entry, int value)
        {
            if (!EnsureConnected()) return false;
            entry.Writing = true;
            try
            {
                string cmd = string.Format(CultureInfo.InvariantCulture, "SERVO_WRITE {0} 0x{1:X4} {2}", Servo, entry.Index, value);
                log("> " + cmd);
                var reply = await link.RequestAsync(cmd, l => l.StartsWith("SERVO_WRITE ") || l.StartsWith("Usage") || l.Contains("is not enabled"), 3000, 0);

                foreach (var line in reply)
                {
                    int index;
                    bool ok;
                    int? readBack;
                    if (ServoRegisters.TryParseWriteReply(line, out index, out ok, out readBack) && index == entry.Index)
                    {
                        if (readBack.HasValue) entry.LiveValue = readBack;
                        if (!ok) SetState(entry.Parameter + ": the servo did not accept " + value + (readBack.HasValue ? " (still " + readBack + ")" : "") + ".");
                        return ok;
                    }
                }

                if (reply.Count == 0)
                {
                    SetState("No answer to SERVO_WRITE. Flash the latest firmware to edit servo registers.");
                    return null;
                }
                SetState(reply.Last());
                return false;
            }
            finally
            {
                entry.Writing = false;
            }
        }

        // ------------------------------------------------------------------
        // Tuned set / EEPROM
        // ------------------------------------------------------------------

        private async void BtnWriteTuned_Click(object sender, RoutedEventArgs e)
        {
            if (busy || !EnsureConnected()) return;
            var answer = MessageBox.Show(this,
                $"Write the tuned parameter set of the firmware into the EEPROM of servo {Servo}?\n\nThe servo must be powered. Power cycle it afterwards.",
                "Write tuned set", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            SetBusy(true);
            bool done;
            try
            {
                string cmd = "FLASH_SERVO " + Servo;
                log("> " + cmd);
                SetState("Writing the tuned set to servo " + Servo + "...");
                var reply = await link.RequestAsync(cmd, l => l.Contains("[Flash] Done!") || l.Contains("[Flash] Warning") || l.Contains("is not enabled"), 120000, 15000);
                done = reply.Any(l => l.Contains("[Flash] Done!"));
                SetState(done ? "Tuned set saved in servo " + Servo + ". Power cycle the servo." : "Writing the tuned set did not report success (see Serial Logs).");
            }
            finally
            {
                SetBusy(false);
            }
            if (done) await LoadAsync();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (busy || !EnsureConnected()) return;
            var answer = MessageBox.Show(this,
                $"Store the current values of servo {Servo} permanently in its EEPROM?\n\nPower cycle the servo afterwards.",
                "Save to EEPROM", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
            await SaveToEepromAsync();
        }

        private async Task SaveToEepromAsync()
        {
            SetBusy(true);
            try
            {
                string cmd = "SERVO_SAVE " + Servo;
                log("> " + cmd);
                var reply = await link.RequestAsync(cmd, l => l.StartsWith("SERVO_SAVE ") || l.StartsWith("Usage") || l.Contains("is not enabled"), 5000, 0);
                if (reply.Count == 0)
                {
                    SetState("No answer to SERVO_SAVE. Flash the latest firmware to store edited registers.");
                }
                else
                {
                    string line = reply.Last();
                    SetState(line.Contains(" OK") ? "Servo " + Servo + " values stored in the EEPROM. Power cycle the servo." : line);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ------------------------------------------------------------------
        // Export / import
        // ------------------------------------------------------------------

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (!entries.Any(x => x.LiveValue.HasValue))
            {
                SetState("Load the registers from the servo first.");
                return;
            }
            var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = "iSV57_servo" + Servo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv",
                Title = "Export servo registers",
            };
            if (dialog.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Register;Address;Name;Live;Tuned");
            foreach (var entry in entries)
            {
                sb.AppendLine(string.Join(";",
                    entry.Parameter,
                    entry.Address,
                    (entry.Name ?? "").Replace(";", ","),
                    entry.LiveValue.HasValue ? entry.LiveValue.Value.ToString(CultureInfo.InvariantCulture) : "",
                    entry.TunedValue.HasValue ? entry.TunedValue.Value.ToString(CultureInfo.InvariantCulture) : ""));
            }
            try
            {
                File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false));
                SetState("Exported to " + dialog.FileName);
            }
            catch (Exception ex)
            {
                SetState("Export failed: " + ex.Message);
            }
        }

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (busy || !EnsureConnected()) return;
            if (!entries.Any(x => x.LiveValue.HasValue))
            {
                SetState("Load the registers from the servo first, so only differing values are written.");
                return;
            }
            var dialog = new OpenFileDialog { Filter = "CSV file (*.csv)|*.csv|All files|*.*", Title = "Import servo registers" };
            if (dialog.ShowDialog(this) != true) return;

            Dictionary<int, int> values;
            try
            {
                values = ParseCsv(File.ReadAllLines(dialog.FileName));
            }
            catch (Exception ex)
            {
                SetState("Import failed: " + ex.Message);
                return;
            }

            var changes = values
                .Where(kv => kv.Key >= 0 && kv.Key < entries.Count && !entries[kv.Key].UnitSpecific)
                .Where(kv => !entries[kv.Key].LiveValue.HasValue || entries[kv.Key].LiveValue.Value != kv.Value)
                .OrderBy(kv => kv.Key)
                .ToList();
            if (changes.Count == 0)
            {
                SetState("The servo already has all values of the file.");
                return;
            }

            var answer = MessageBox.Show(this,
                $"Write {changes.Count} differing register value{(changes.Count == 1 ? "" : "s")} to servo {Servo}?\n\n" +
                string.Join("\n", changes.Take(12).Select(kv => $"{entries[kv.Key].Parameter}: {entries[kv.Key].LiveDisplay} → {kv.Value}")) +
                (changes.Count > 12 ? "\n..." : "") +
                "\n\nUnit-specific calibration (Pr7.18) is never imported.",
                "Import servo registers", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            SetBusy(true);
            int written = 0, failed = 0;
            try
            {
                foreach (var kv in changes)
                {
                    var result = await WriteRegisterAsync(entries[kv.Key], kv.Value);
                    if (result == null) return; // firmware without SERVO_WRITE
                    if (result == true) written++; else failed++;
                    TextState.Text = $"Importing... {written + failed} / {changes.Count}";
                }
                SetState($"Import: {written} written, {failed} failed.");
            }
            finally
            {
                SetBusy(false);
                UpdateSummary();
                view.Refresh();
            }

            if (written > 0)
            {
                var save = MessageBox.Show(this, "Store the imported values permanently in the servo EEPROM now?", "Import servo registers",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (save == MessageBoxResult.Yes) await SaveToEepromAsync();
            }
        }

        /// <summary>
        /// Reads "register -> value" from a CSV of this window (Register;Address;Name;Live;Tuned) or of
        /// any file with a PrX.YY or 0x address column and a value column named Live/Value.
        /// </summary>
        private static Dictionary<int, int> ParseCsv(string[] lines)
        {
            var result = new Dictionary<int, int>();
            int valueColumn = 3;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var cells = raw.Split(raw.Contains(";") ? ';' : ',').Select(c => c.Trim().Trim('"')).ToArray();

                int header = Array.FindIndex(cells, c => c.Equals("Live", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Live ", StringComparison.OrdinalIgnoreCase) || c.Equals("Value", StringComparison.OrdinalIgnoreCase));
                if (header >= 0 && !cells.Any(c => c.StartsWith("Pr", StringComparison.OrdinalIgnoreCase) && c.Contains(".")))
                {
                    valueColumn = header;
                    continue;
                }

                int index = -1;
                foreach (var cell in cells)
                {
                    index = ParseRegister(cell);
                    if (index >= 0) break;
                }
                if (index < 0 || valueColumn >= cells.Length) continue;

                int value;
                if (int.TryParse(cells[valueColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= -32768 && value <= 32767)
                {
                    result[index] = value;
                }
            }
            if (result.Count == 0) throw new InvalidDataException("no register values found");
            return result;
        }

        private static int ParseRegister(string cell)
        {
            if (cell.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                int hex;
                if (int.TryParse(cell.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex) && hex < ServoRegisters.Count) return hex;
                return -1;
            }
            if (cell.StartsWith("Pr", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < ServoRegisters.Count; i++)
                {
                    if (string.Equals(ServoRegisters.ParameterName(i), cell, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Filter
        // ------------------------------------------------------------------

        private bool FilterEntry(object item)
        {
            var entry = item as ServoRegisterEntry;
            if (entry == null) return false;
            if (CheckDiffOnly.IsChecked == true && !entry.Differs && !entry.ReadError) return false;
            string search = (TextSearch.Text ?? "").Trim();
            if (search.Length == 0) return true;
            return entry.Parameter.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.Address.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || (entry.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TextSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            view?.Refresh();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TextSearch.Text = "";
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            view?.Refresh();
        }
    }
}
