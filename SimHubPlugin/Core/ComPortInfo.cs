using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace DiyBeltTensioner.Core
{
    public enum DeviceKind
    {
        Unknown,
        BeltTensioner,      // belt tensioner firmware (USB product "diy_belt_tensioner" or answered the probe)
        FfbPedal,           // DIY FFB Pedal firmware (303A:8332..8334, F011:F011, USB product "FFB_Pedal_..." / "DiyFfbPedal")
        FfbPedalBridge,     // DIY FFB Pedal wireless bridge (303A:8331)
        EspBootloader,      // ESP32-S3 in ROM download mode
        EspNativeUsb,       // other ESP32-S3 native USB device
        UsbUart,            // CP2102 / CH340 / FTDI bridge: firmware unknown until probed
        Other,
    }

    /// <summary>A COM port that is currently present, with USB information.</summary>
    public class ComPortInfo
    {
        public string PortName { get; set; }
        public string Description { get; set; }
        public string Vid { get; set; }
        public string Pid { get; set; }

        /// <summary>USB product string reported by the device (DEVPKEY_Device_BusReportedDeviceDesc).</summary>
        public string Product { get; set; }

        public DeviceKind Kind { get; set; } = DeviceKind.Unknown;

        /// <summary>Pedal role (Clutch, Brake, Throttle) if known.</summary>
        public string Role { get; set; }

        /// <summary>Any DIY FFB Pedal device (pedal or bridge): never the belt tensioner.</summary>
        public bool IsPedalDevice => Kind == DeviceKind.FfbPedal || Kind == DeviceKind.FfbPedalBridge;

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case DeviceKind.BeltTensioner: return "Belt Tensioner";
                    case DeviceKind.FfbPedal: return string.IsNullOrEmpty(Role) ? "FFB Pedal" : "FFB Pedal: " + Role;
                    case DeviceKind.FfbPedalBridge: return "FFB Pedal Bridge";
                    case DeviceKind.EspBootloader: return "ESP32 bootloader";
                    case DeviceKind.EspNativeUsb: return "ESP32 USB";
                    case DeviceKind.UsbUart: return "USB-UART, firmware unknown";
                    default: return string.IsNullOrEmpty(Description) ? "" : Description;
                }
            }
        }

        /// <summary>"COM19  (Belt Tensioner)"</summary>
        public string DisplayName
        {
            get
            {
                string kind = KindText;
                return string.IsNullOrEmpty(kind) ? PortName : PortName + "  (" + kind + ")";
            }
        }

        public string ToolTip
        {
            get
            {
                return $"{PortName}: {Description}\nUSB {Vid ?? "?"}:{Pid ?? "?"}" + (string.IsNullOrEmpty(Product) ? "" : "\nProduct: " + Product);
            }
        }

        public override string ToString() => DisplayName;

        private static readonly Regex ComRegex = new Regex(@"\((COM\d+)\)", RegexOptions.Compiled);
        private static readonly Regex VidRegex = new Regex(@"VID_([0-9A-F]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PidRegex = new Regex(@"PID_([0-9A-F]{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Ports identified by actively probing them (UART boards have no USB product string of their own)
        private static readonly ConcurrentDictionary<string, DeviceKind> probedKinds = new ConcurrentDictionary<string, DeviceKind>(StringComparer.OrdinalIgnoreCase);

        public static void RememberKind(string portName, DeviceKind kind)
        {
            if (!string.IsNullOrEmpty(portName)) probedKinds[portName] = kind;
        }

        /// <summary>
        /// Lists the COM ports that are present right now (WMI). SerialPort.GetPortNames() reads the
        /// registry and also returns ports of unplugged devices, so it is only used as fallback.
        /// </summary>
        public static List<ComPortInfo> GetPresentPorts(bool readUsbProduct = true)
        {
            var result = new Dictionary<string, ComPortInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        using (obj)
                        {
                            string name = obj["Name"] as string ?? "";
                            string deviceId = obj["DeviceID"] as string ?? "";
                            var m = ComRegex.Match(name);
                            if (!m.Success) continue;
                            string com = m.Groups[1].Value;
                            var vid = VidRegex.Match(deviceId);
                            var pid = PidRegex.Match(deviceId);
                            var info = new ComPortInfo
                            {
                                PortName = com,
                                Description = name.Replace("(" + com + ")", "").Trim(),
                                Vid = vid.Success ? vid.Groups[1].Value.ToUpperInvariant() : null,
                                Pid = pid.Success ? pid.Groups[1].Value.ToUpperInvariant() : null,
                            };
                            if (readUsbProduct && info.Vid != null)
                            {
                                info.Product = ReadUsbProduct(obj);
                            }
                            Classify(info);
                            result[com] = info;
                        }
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
                    var info = new ComPortInfo { PortName = name, Description = "" };
                    Classify(info);
                    result[name] = info;
                }
            }

            return result.Values.OrderBy(p => PortNumber(p.PortName)).ToList();
        }

        public static ComPortInfo Find(string portName)
        {
            return GetPresentPorts(false).FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
        }

        private static void Classify(ComPortInfo p)
        {
            p.Kind = ClassifyKind(p);
        }

        private static DeviceKind ClassifyKind(ComPortInfo p)
        {
            string product = p.Product ?? "";
            if (product.IndexOf("belt_tensioner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                product.IndexOf("belttensioner", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DeviceKind.BeltTensioner;
            }

            // DIY FFB Pedal: the firmware sets VID 303A with one PID per pedal role and the
            // product name FFB_Pedal_<role> (ESP32/src/Controller.cpp of the pedal repository)
            if (p.Vid == "303A")
            {
                switch (p.Pid)
                {
                    case "8331": return DeviceKind.FfbPedalBridge;
                    case "8332": p.Role = "Clutch"; return DeviceKind.FfbPedal;
                    case "8333": p.Role = "Brake"; return DeviceKind.FfbPedal;
                    case "8334": p.Role = "Throttle"; return DeviceKind.FfbPedal;
                }
            }
            if (product.StartsWith("FFB_Pedal_", StringComparison.OrdinalIgnoreCase))
            {
                string role = product.Substring("FFB_Pedal_".Length);
                p.Role = role.Equals("NOASSIGNMENT", StringComparison.OrdinalIgnoreCase) ? "unassigned" : role;
                return DeviceKind.FfbPedal;
            }
            if ((p.Vid == "F011" && p.Pid == "F011") || product.IndexOf("DiyFfbPedal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                product.IndexOf("FFB_Pedal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DeviceKind.FfbPedal;
            }

            DeviceKind probed;
            if (probedKinds.TryGetValue(p.PortName, out probed) && probed != DeviceKind.Unknown)
            {
                return probed;
            }

            if (p.Vid == "303A")
            {
                if (p.Pid == "0002" || (p.Pid == "1001" && product.IndexOf("JTAG", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return DeviceKind.EspBootloader;
                }
                return DeviceKind.EspNativeUsb;
            }
            // Silicon Labs CP210x, WCH CH340/CH9102, FTDI
            if (p.Vid == "10C4" || p.Vid == "1A86" || p.Vid == "0403")
            {
                return DeviceKind.UsbUart;
            }
            return p.Vid == null ? DeviceKind.Other : DeviceKind.Unknown;
        }

        /// <summary>
        /// Reads the USB product string. Composite devices (TinyUSB CDC) report the interface name
        /// on the COM port node, the product string sits on the parent USB device.
        /// </summary>
        private static string ReadUsbProduct(ManagementObject portDevice)
        {
            string desc, parent;
            ReadProperties(portDevice, out desc, out parent);
            if (IsUseful(desc)) return desc;

            if (!string.IsNullOrEmpty(parent) && parent.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string query = "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID = '" + parent.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
                    using (var searcher = new ManagementObjectSearcher(query))
                    {
                        foreach (ManagementObject parentObj in searcher.Get())
                        {
                            using (parentObj)
                            {
                                string parentDesc, ignored;
                                ReadProperties(parentObj, out parentDesc, out ignored);
                                if (IsUseful(parentDesc)) return parentDesc;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return desc;
        }

        private static bool IsUseful(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return false;
            // Generic interface names say nothing about the firmware
            return desc.IndexOf("CDC", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>Win32_PnPEntity.GetDeviceProperties (Windows 10 and newer).</summary>
        private static void ReadProperties(ManagementObject device, out string busReportedDesc, out string parent)
        {
            busReportedDesc = null;
            parent = null;
            try
            {
                var inParams = device.GetMethodParameters("GetDeviceProperties");
                inParams["devicePropertyKeys"] = new[] { "DEVPKEY_Device_BusReportedDeviceDesc", "DEVPKEY_Device_Parent" };
                var outParams = device.InvokeMethod("GetDeviceProperties", inParams, null);
                var properties = outParams?["deviceProperties"] as ManagementBaseObject[];
                if (properties == null) return;
                foreach (var property in properties)
                {
                    string key = property["KeyName"] as string;
                    string data = property["Data"] as string;
                    if (key == "DEVPKEY_Device_BusReportedDeviceDesc") busReportedDesc = data;
                    else if (key == "DEVPKEY_Device_Parent") parent = data;
                }
            }
            catch (Exception)
            {
                // Older Windows or missing property
            }
        }

        private static int PortNumber(string name)
        {
            int n;
            return name != null && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(3), out n) ? n : int.MaxValue;
        }
    }
}
