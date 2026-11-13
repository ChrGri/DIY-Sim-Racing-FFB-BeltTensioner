using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DiyBeltTensioner.Core
{
    /// <summary>Configuration of one axis as reported by the CONFIG command.</summary>
    public class AxisConfigInfo
    {
        public int Axis { get; set; }                // 1 or 2
        public double PitchMm { get; set; }
        public bool PitchSaved { get; set; }         // false = Config.h default
        public double RangeMm { get; set; }
        public bool RangeSaved { get; set; }
        public long RangeSteps { get; set; }
        public double MaxSpeedMs { get; set; }
        public long MaxSpeedSteps { get; set; }
        public double AccelerationMs2 { get; set; }
        public long AccelerationSteps { get; set; }
        public bool RestartPending { get; set; }
    }

    /// <summary>One behaviour setting as reported by the SETTINGS command.</summary>
    public class ControllerSetting
    {
        public string Key { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; }
        public bool Saved { get; set; }      // false = Config.h default
        public double Min { get; set; }
        public double Max { get; set; }
        public bool RestartPending { get; set; }
    }

    /// <summary>
    /// Parses the output of the CONFIG command (BeltTensionerProtocol::printAxisConfig):
    ///   Axis 1: spindle pitch 10.00 mm (Config.h), working range 60.00 mm (saved) = 19200 steps
    ///     motion: max speed 0.297 m/s (95000 steps/s), acceleration 9.38 m/s^2 (3000000 steps/s^2)
    ///     -> changed, restart the controller to apply (active: pitch 5.00 mm, range 60.00 mm)
    /// </summary>
    public static class AxisConfigParser
    {
        private static readonly Regex AxisLine = new Regex(
            @"^Axis (\d): spindle pitch ([\d.]+) mm \((saved|Config\.h)\), working range ([\d.]+) mm \((saved|Config\.h)\) = (-?\d+) steps",
            RegexOptions.Compiled);

        private static readonly Regex MotionLine = new Regex(
            @"motion: max speed ([\d.]+) m/s \((\d+) steps/s\), acceleration ([\d.]+) m/s\^2 \((\d+) steps/s\^2\)",
            RegexOptions.Compiled);

        public static List<AxisConfigInfo> Parse(IEnumerable<string> lines)
        {
            var result = new List<AxisConfigInfo>();
            AxisConfigInfo current = null;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                var m = AxisLine.Match(line);
                if (m.Success)
                {
                    current = new AxisConfigInfo
                    {
                        Axis = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                        PitchMm = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        PitchSaved = m.Groups[3].Value == "saved",
                        RangeMm = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                        RangeSaved = m.Groups[5].Value == "saved",
                        RangeSteps = long.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture),
                    };
                    result.Add(current);
                    continue;
                }

                if (current == null) continue;

                var mm = MotionLine.Match(line);
                if (mm.Success)
                {
                    current.MaxSpeedMs = double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
                    current.MaxSpeedSteps = long.Parse(mm.Groups[2].Value, CultureInfo.InvariantCulture);
                    current.AccelerationMs2 = double.Parse(mm.Groups[3].Value, CultureInfo.InvariantCulture);
                    current.AccelerationSteps = long.Parse(mm.Groups[4].Value, CultureInfo.InvariantCulture);
                }
                else if (line.StartsWith("-> changed"))
                {
                    current.RestartPending = true;
                }
            }
            return result;
        }

        // Setting IDLE_POSITION = 10.00 % (default, 0.00..50.00) -> restart to apply
        private static readonly Regex SettingLine = new Regex(
            @"^Setting (\w+) = (-?[\d.]+)(?: (\S+))? \((saved|default), (-?[\d.]+)\.\.(-?[\d.]+)\)(.*)$",
            RegexOptions.Compiled);

        public static List<ControllerSetting> ParseSettings(IEnumerable<string> lines)
        {
            var result = new List<ControllerSetting>();
            foreach (var raw in lines)
            {
                var m = SettingLine.Match(raw.Trim());
                if (!m.Success) continue;
                result.Add(new ControllerSetting
                {
                    Key = m.Groups[1].Value,
                    Value = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    Unit = m.Groups[3].Success ? m.Groups[3].Value : "",
                    Saved = m.Groups[4].Value == "saved",
                    Min = double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                    Max = double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture),
                    RestartPending = m.Groups[7].Value.Contains("restart"),
                });
            }
            return result;
        }

        private static readonly Regex SensorLine = new Regex(@"^Sensor #(\d+):(-?\d+):Trigger level:(\d+):Triggered:([01])", RegexOptions.Compiled);

        /// <summary>Parses "Sensor #0:45:Trigger level:15:Triggered:0" (CMD 11 / STATUS).</summary>
        public static bool TryParseSensor(string line, out int axisIndex, out int loadPercent, out int triggerLevel, out bool triggered)
        {
            axisIndex = loadPercent = triggerLevel = 0;
            triggered = false;
            var m = SensorLine.Match(line.Trim());
            if (!m.Success) return false;
            axisIndex = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            loadPercent = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            triggerLevel = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            triggered = m.Groups[4].Value == "1";
            return true;
        }
    }
}
