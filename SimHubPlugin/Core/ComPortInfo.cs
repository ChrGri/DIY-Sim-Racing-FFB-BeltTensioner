using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace DiyBeltTensioner.Core
{
    /// <summary>A COM port that is currently present, with USB VID/PID if available.</summary>
    public class ComPortInfo
    {
        public string PortName { get; set; }
        public string Description { get; set; }
        public string Vid { get; set; }
        public string Pid { get; set; }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(Description)) return PortName;
                return PortName + " - " + Description;
            }
        }

        public override string ToString() => DisplayName;

        private static readonly Regex ComRegex = new Regex(@"\((COM\d+)\)", RegexOptions.Compiled);
        private static readonly Regex VidRegex = new Regex(@"VID_([0-9A-F]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PidRegex = new Regex(@"PID_([0-9A-F]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Lists the COM ports that are present right now (WMI). SerialPort.GetPortNames() reads the
        /// registry and also returns ports of unplugged devices, so it is only used as fallback.
        /// </summary>
        public static List<ComPortInfo> GetPresentPorts()
        {
            var result = new Dictionary<string, ComPortInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"] as string ?? "";
                        string deviceId = obj["DeviceID"] as string ?? "";
                        var m = ComRegex.Match(name);
                        if (!m.Success) continue;
                        string com = m.Groups[1].Value;
                        var vid = VidRegex.Match(deviceId);
                        var pid = PidRegex.Match(deviceId);
                        result[com] = new ComPortInfo
                        {
                            PortName = com,
                            Description = name.Replace("(" + com + ")", "").Trim(),
                            Vid = vid.Success ? vid.Groups[1].Value.ToUpperInvariant() : null,
                            Pid = pid.Success ? pid.Groups[1].Value.ToUpperInvariant() : null,
                        };
                    }
                }
            }
            catch (Exception)
            {
                // WMI not available - fall back to the registry list
            }

            if (result.Count == 0)
            {
                foreach (var name in SerialPort.GetPortNames())
                {
                    result[name] = new ComPortInfo { PortName = name, Description = "" };
                }
            }

            return result.Values.OrderBy(p => PortNumber(p.PortName)).ToList();
        }

        public static ComPortInfo Find(string portName)
        {
            return GetPresentPorts().FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
        }

        private static int PortNumber(string name)
        {
            int n;
            return name != null && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(3), out n) ? n : int.MaxValue;
        }
    }
}
