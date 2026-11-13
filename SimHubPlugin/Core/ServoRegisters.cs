using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DiyBeltTensioner.Core
{
    /// <summary>One iSV57 register in the register window.</summary>
    public class ServoRegisterEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Register index 0..304 (= Modbus address).</summary>
        public int Index { get; set; }

        /// <summary>"Pr0.08"</summary>
        public string Parameter { get; set; }

        /// <summary>"0x0008"</summary>
        public string Address => "0x" + Index.ToString("X4", CultureInfo.InvariantCulture);

        public string Name { get; set; }

        /// <summary>Per-unit factory calibration, not compared with the tuned table.</summary>
        public bool UnitSpecific { get; set; }

        private int? tunedValue;
        /// <summary>Value of the tuned table of the firmware (from DUMP_SERVO).</summary>
        public int? TunedValue
        {
            get { return tunedValue; }
            set { tunedValue = value; Notify(nameof(TunedValue)); Notify(nameof(TunedDisplay)); NotifyState(); }
        }

        private int? liveValue;
        /// <summary>Value read from the servo. null = not read yet.</summary>
        public int? LiveValue
        {
            get { return liveValue; }
            set { liveValue = value; Notify(nameof(LiveValue)); Notify(nameof(LiveDisplay)); NotifyState(); }
        }

        private bool readError;
        public bool ReadError
        {
            get { return readError; }
            set { readError = value; Notify(nameof(LiveDisplay)); NotifyState(); }
        }

        private bool writing;
        public bool Writing
        {
            get { return writing; }
            set { writing = value; NotifyState(); }
        }

        /// <summary>Text of the editing box of the live value.</summary>
        public string EditText { get; set; }

        public string LiveDisplay => ReadError ? "read error" : liveValue.HasValue ? liveValue.Value.ToString(CultureInfo.InvariantCulture) : "-";
        public string TunedDisplay => UnitSpecific ? "(unit-specific)" : tunedValue.HasValue ? tunedValue.Value.ToString(CultureInfo.InvariantCulture) : "-";

        public bool Differs => !UnitSpecific && liveValue.HasValue && tunedValue.HasValue && liveValue.Value != tunedValue.Value;

        public string State
        {
            get
            {
                if (Writing) return "writing...";
                if (ReadError) return "read error";
                if (!liveValue.HasValue) return "";
                if (UnitSpecific) return "unit-specific";
                if (!tunedValue.HasValue) return "";
                return Differs ? "differs" : "OK";
            }
        }

        /// <summary>Green = as tuned, red = differs, grey = not compared, orange = read error.</summary>
        public string LiveColor
        {
            get
            {
                if (ReadError) return "#FFFBBF24";
                if (!liveValue.HasValue || UnitSpecific || !tunedValue.HasValue) return "#FFDADADA";
                return Differs ? "#FFE53935" : "#FF4CAF50";
            }
        }

        private void NotifyState()
        {
            Notify(nameof(State));
            Notify(nameof(LiveColor));
            Notify(nameof(Differs));
        }
    }

    /// <summary>
    /// Register table of the iSV57 (305 registers, groups Pr0..Pr7) and the parser of the
    /// DUMP_SERVO / DIFF_SERVO output of the firmware. Names as in the DIY FFB Pedal plugin.
    /// </summary>
    public static class ServoRegisters
    {
        public const int Count = 305;

        // Start index of the parameter groups Pr0.xx ... Pr7.xx (isv57communication.cpp)
        private static readonly int[] GroupStart = { 0x0000, 0x0019, 0x0041, 0x005F, 0x007D, 0x00AF, 0x00D7, 0x00FF };

        // Pr7.18 encoder index angle compensation is calibrated per servo
        public const int UnitSpecificIndex = 0x00FF + 18;

        private static readonly string[] Names =
        {
            "Reserved parameters",
            "Control mode",
            "Real-time auto-gain tuning mode",
            "Selection of machine stiffness at real-time auto-gain tuning",
            "Ratio of inertia",
            "Command pulse input selection",
            "Motor rotational direction setup",
            "Reserved parameters",
            "Microstep",
            "1st numerator of electronic gear",
            "Denominator of electronic gear",
            "Reserved parameters",
            "Reserved parameters",
            "1st torque limit",
            "Position deviation setup",
            "Absolute encoder setup",
            "External regenerative resistor setup",
            "Regeneration discharge resistance power",
            "Vibration suppression - N after Stop",
            "Microseismic inhibition",
            "Activated pulse edge",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "1st position loop gain",
            "1st velocity loop gain",
            "1st time constant of velocity loop integration",
            "1st filter of velocity detection",
            "1st torque filter",
            "2nd position loop gain",
            "2nd velocity loop gain",
            "2nd time constant of velocity loop",
            "2nd filter of velocity detection",
            "2nd torque filter",
            "Velocity feed forward gain",
            "Velocity feed forward filter",
            "Torque feed forward gain",
            "Torque feed forward filter",
            "2nd gain setup",
            "Control switching mode",
            "Position control switching delay time",
            "Control switching level",
            "Control switch hysteresis",
            "Gain switching time",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Speed regulator-kr",
            "Speed regulator-km",
            "Speed regulator-kd",
            "Filter",
            "Reserved parameter",
            "1st position loop integral time",
            "1st position loop differential time",
            "2nd position loop integral time",
            "2nd position loop differential time",
            "Position loop differential filter",
            "Speed given filter",
            "Reserved parameter",
            "Position command digital filter Settings",
            "Encoder feedback pulse digital filter Setting",
            "Special function register",
            "Reserved parameter",
            "Reserved parameter",
            "Adaptive filter mode setup",
            "1st notch frequency",
            "1st notch width",
            "1st notch depth",
            "2nd notch frequency",
            "2nd notch width",
            "2nd notch depth",
            "3rd notch frequency",
            "3rd notch width",
            "3rd notch depth",
            "4th notch frequency",
            "4th notch width",
            "4th notch depth",
            "Selection of damping filter switching",
            "1st damping frequency",
            "1st damping filter",
            "2nd damping frequency",
            "2nd damping filter",
            "3rd damping frequency",
            "3rd damping filter",
            "4th damping frequency",
            "4th damping filter",
            "Positional command smoothing filter",
            "Positional command FIR filter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Velocity setup internal and external switching",
            "Speed command rotational direction",
            "Speed command input gain",
            "Speed command reversal input",
            "1st speed setup",
            "2nd speed setup",
            "3rd speed setup",
            "4th speed setup",
            "5th speed setup",
            "6th speed setup",
            "7th speed setup",
            "8th speed setup",
            "Time setup acceleration",
            "Time setup deceleration",
            "Sigmoid acceleration/deceleration time setup",
            "Speed zero-clamp level",
            "Speed zero-clamp level",
            "Torque command internal and external switching",
            "Torque command direction selection",
            "Torque command input gain",
            "Torque command input reversal",
            "Speed limit value 1",
            "Speed limit value 2",
            "Reserved parameter",
            "Maximum speed of motor rotation",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Input selection SI1",
            "Input selection SI2",
            "Input selection SI3",
            "Input selection SI4",
            "Input selection SI5",
            "Input selection SI6",
            "Input selection SI7",
            "Input selection SI8",
            "Input selection SI9",
            "Input selection SI10",
            "Input selection SI11",
            "Output selection SO1",
            "Output selection SO2",
            "Output selection SO3",
            "Output selection SO4",
            "Output selection SO5",
            "Output selection SO6",
            "Output selection SO7",
            "Output selection SO8",
            "Output selection SO9",
            "Reserved parameter",
            "Reserved parameter",
            "Mechanical brake action at stalling setup",
            "Mechanical brake action at running setup",
            "Braking release delay time",
            "Positioning complete range",
            "Positioning complete output setup",
            "Positioning complete hold time",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Positioning command pass range",
            "Speed arrival output setup",
            "Speed arrival output range",
            "Speed reach output setup",
            "Speed reach range",
            "Speed reach output condition setup",
            "Torque arrival output setup",
            "Torque arrival level",
            "Torque arrival hysteresis",
            "Zero-speed clamp speed",
            "Zero-speed clamp hysteresis",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Drive inhibit input setup",
            "Sequence at drive inhibit input",
            "Sequence at servo off",
            "Main power off stop mode",
            "LV-trip selection at main power off",
            "The main power off detection time",
            "Sequence at alarm",
            "Torque setup for emergency stop",
            "Over-load level setup",
            "Over-speed level setup",
            "Motor working over-temperature alarm threshold setting",
            "Control power supply under-voltage alarm threshold setting",
            "Continuous regeneration processing time limit",
            "Internal braking resistor resistance",
            "Internal braking resistor power",
            "External braking resistor power",
            "Position setup unit select",
            "Selection of torque limit",
            "2nd torque limit",
            "Torque limit positive",
            "Torque limit negative",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Speed axis pulse output division numerator",
            "Reserved parameter",
            "Pulse output division denominator",
            "Reserved parameter",
            "Front panel lock",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Drive velocity limit",
            "Position 3rd gain valid setup",
            "Position 3rd gain multiplication",
            "Additional torque filter",
            "Torque compensation value",
            "Torque compensation direction",
            "Function extension setup",
            "Current response setup",
            "Current response setup",
            "Reserved parameter",
            "Emergency stop time at alarm",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Power-up wait time",
            "Encoder Z phase setup",
            "Trial running distance",
            "Trial running wait time",
            "Trial running cycle times",
            "Disturbance torque compensating gain",
            "Disturbance observer filter",
            "Reserved parameter",
            "Reserved parameter",
            "Alarm latch time selection",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Real-time auto tuning estimation speed",
            "Real-time auto tuning custom setup",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Reserved parameter",
            "Oscillation detection level",
            "Alarm mask setup",
            "Reserved parameter",
            "Current loop gain",
            "Current loop integral time",
            "Motor rotor initial position angle compensation",
            "Reserved parameter",
            "Motor rated voltage",
            "Motor pole pairs",
            "Motor phase resistor",
            "Motor D/Q inductance",
            "Motor back EMF coefficient",
            "Motor torque coefficient",
            "Motor rated speed",
            "Motor maximum speed",
            "Motor rated current",
            "Motor rotor inertia",
            "Motor power selection",
            "Motor model input",
            "Encoder selection",
            "Motor maximum current",
            "Encoder Index Angle compensation",
            "Reserved parameter",
            "Drive model input",
            "Servo model input",
            "Reserved parameter",
            "Reserved parameter",
            "Fan control mode setting",
            "Fan open temperature setting",
            "Fan temperature control hysteresis",
            "Drive over-temperature alarm threshold setting",
            "Time of Bleeder alarm window",
            "DC bus voltage detection filter",
            "Under-voltage point set",
            "Bleeder control mode setting",
            "Bleeder open the threshold set",
            "Bleeder control hysteresis",
            "Overvoltage point set",
            "Relay control mode setting",
            "Threshold setting of relay suction",
            "Analog quantity AI3 hardware zero drift compensation",
            "Analog quantity AI3 hardware zero drift compensation",
            "Analog quantity AI3 hardware zero drift compensation",
            "RS232 communication mode settings",
            "RS485 communication baud rate settings",
            "RS232-ID",
            "DC bus voltage hardware zero drift compensation",
            "Temperature measurement hardware zero drift compensation",
            "DC bus voltage hardware slope coefficient",
            "Current U phase sampling hardware slope coefficient",
            "Current V phase sampling hardware slope coefficient",
            "Reserved parameter",
            "Reserved parameter"
        };

        public static string ParameterName(int index)
        {
            int group = 0;
            for (int g = 0; g < GroupStart.Length; g++)
            {
                if (index >= GroupStart[g]) group = g;
            }
            return "Pr" + group + "." + (index - GroupStart[group]).ToString("00", CultureInfo.InvariantCulture);
        }

        public static List<ServoRegisterEntry> CreateTable()
        {
            var list = new List<ServoRegisterEntry>(Count);
            for (int i = 0; i < Count; i++)
            {
                list.Add(new ServoRegisterEntry
                {
                    Index = i,
                    Parameter = ParameterName(i),
                    Name = i < Names.Length ? Names[i] : "",
                    UnitSpecific = i == UnitSpecificIndex,
                });
            }
            return list;
        }

        // Pr0.08 (0x0008)  actual: 3200  tuned: 3200
        // Pr0.06 (0x0006)  actual: 0  tuned: 1  <-- differs
        // Pr7.18 (0x0111)  actual: 1234  (unit-specific, not compared)
        // Pr1.00 (0x0019)  read error
        private static readonly Regex DumpLine = new Regex(
            @"^Pr\d\.\d{2} \(0x([0-9A-Fa-f]{4})\)\s+(?:actual: (-?\d+)(?:\s+tuned: (-?\d+))?|(read error))",
            RegexOptions.Compiled);

        public static bool TryParseDumpLine(string line, out int index, out int? actual, out int? tuned, out bool readError)
        {
            index = -1;
            actual = null;
            tuned = null;
            readError = false;
            var m = DumpLine.Match(line.Trim());
            if (!m.Success) return false;
            index = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (m.Groups[4].Success)
            {
                readError = true;
                return true;
            }
            actual = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[3].Success) tuned = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return true;
        }

        // SERVO_WRITE 1 0x0008 = 3200 OK / SERVO_WRITE 1 0x0008 = 3200 failed (read back 100)
        private static readonly Regex WriteLine = new Regex(
            @"^SERVO_WRITE (\d) 0x([0-9A-Fa-f]{4}) = (-?\d+) (OK|failed)(?: \(read back (-?\d+)\))?",
            RegexOptions.Compiled);

        public static bool TryParseWriteReply(string line, out int index, out bool ok, out int? readBack)
        {
            index = -1;
            ok = false;
            readBack = null;
            var m = WriteLine.Match(line.Trim());
            if (!m.Success) return false;
            index = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ok = m.Groups[4].Value == "OK";
            if (ok) readBack = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            else if (m.Groups[5].Success) readBack = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            return true;
        }
    }
}
