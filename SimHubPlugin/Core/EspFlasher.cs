using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DiyBeltTensioner.Core
{
    /// <summary>The binaries of one firmware build (content of a release .zip).</summary>
    public class FirmwareImage
    {
        public string Folder { get; private set; }
        public string Bootloader { get; private set; }
        public string Partitions { get; private set; }
        public string BootApp0 { get; private set; }
        public string Firmware { get; private set; }
        public string Factory { get; private set; }

        /// <summary>bootloader + partitions + boot_app0 + firmware are all present.</summary>
        public bool HasSeparateImages => Bootloader != null && Partitions != null && BootApp0 != null && Firmware != null;

        public bool IsValid => HasSeparateImages || Factory != null;

        /// <summary>Accepts a folder or the path of any .bin inside the folder.</summary>
        public static FirmwareImage FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (folder == null || !Directory.Exists(folder)) return null;

            // Release archives are flat, but accept one level of sub folders as well
            string firmware = FindFile(folder, "firmware.bin");
            if (firmware != null) folder = Path.GetDirectoryName(firmware);

            return new FirmwareImage
            {
                Folder = folder,
                Bootloader = FindFile(folder, "bootloader.bin"),
                Partitions = FindFile(folder, "partitions.bin"),
                BootApp0 = FindFile(folder, "boot_app0.bin"),
                Firmware = FindFile(folder, "firmware.bin"),
                Factory = FindFile(folder, "firmware.factory.bin"),
            };
        }

        private static string FindFile(string folder, string name)
        {
            string direct = Path.Combine(folder, name);
            if (File.Exists(direct)) return direct;
            try
            {
                return Directory.GetFiles(folder, name, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Flashes the controller with esptool.exe (embedded in the plugin).
    /// The port handling follows the USB flasher of the DIY FFB Pedal plugin:
    /// CP2102 boards are reset by esptool via DTR/RTS, native USB boards are put into the
    /// ROM bootloader with a 1200 bps touch and may come back under a different COM number.
    /// </summary>
    public class EspFlasher
    {
        public event Action<string> Output;

        private const int MaxAttempts = 3;
        private const int DefaultBaud = 460800;
        private const int FallbackBaud = 115200;

        private static readonly string[] ConnectionErrorMarkers =
        {
            "Failed to connect", "No serial data received", "could not open port", "Could not open",
            "Lost connection", "Write timeout", "Invalid head of packet", "does not recognize the command",
            "PermissionError", "port doesn't exist", "port is busy", "Timed out waiting for packet",
            "Serial data stream stopped",
        };

        private void Log(string text) => Output?.Invoke(text);

        /// <summary>Path of esptool.exe: next to the plugin DLL, or extracted from the embedded resource.</summary>
        public static string GetEsptoolPath()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string local = Path.Combine(pluginDir, "esptool.exe");
            if (File.Exists(local)) return local;

            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("DiyBeltTensioner.esptool.exe"))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("esptool.exe is not embedded in the plugin. Place esptool.exe next to DiyBeltTensioner.dll in the SimHub folder.");
                }

                string exePath = Path.Combine(Path.GetTempPath(), "esptool_diy_belt_tensioner_" + stream.Length + ".exe");
                if (!File.Exists(exePath) || new FileInfo(exePath).Length != stream.Length)
                {
                    using (var file = new FileStream(exePath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(file);
                    }
                }
                return exePath;
            }
        }

        public async Task<bool> FlashAsync(string comPort, BoardInfo board, FirmwareImage image)
        {
            if (image == null || !image.IsValid)
            {
                Log("Error: firmware files not found (need bootloader.bin, partitions.bin, boot_app0.bin and firmware.bin, or firmware.factory.bin).");
                return false;
            }

            string files;
            if (image.HasSeparateImages)
            {
                files = $"{board.BootloaderOffset} \"{image.Bootloader}\" 0x8000 \"{image.Partitions}\" 0xe000 \"{image.BootApp0}\" 0x10000 \"{image.Firmware}\"";
                Log("Firmware: " + image.Folder + " (bootloader, partitions, boot_app0, firmware)");
            }
            else
            {
                files = $"0x0 \"{image.Factory}\"";
                Log("Firmware: " + image.Factory + " (merged factory image)");
            }

            bool ok = await RunWithRetriesAsync(comPort, board.Chip, "Flashing firmware",
                (port, before, baud) => $"--chip {board.Chip} --port {port} --baud {baud} --before {before} --after hard-reset write-flash -z {files}");

            Log("");
            if (ok)
            {
                Log("SUCCESS: firmware flashed. Saved spindle pitch / working range (NVS) are kept.");
                Log("Next: power the servo, connect and run 'Flash servo parameters' once (Configuration tab).");
            }
            else
            {
                Log("FAILED. If esptool could not connect, put the ESP32 into bootloader mode:");
                Log("  1. hold BOOT, 2. press and release RST, 3. release BOOT, then click Flash again.");
            }
            return ok;
        }

        // ------------------------------------------------------------------
        // Port preparation (adapted from the DIY FFB Pedal plugin)
        // ------------------------------------------------------------------

        private static Dictionary<string, ComPortInfo> Snapshot()
        {
            return ComPortInfo.GetPresentPorts().ToDictionary(p => p.PortName, p => p, StringComparer.OrdinalIgnoreCase);
        }

        private static string Describe(Dictionary<string, ComPortInfo> ports)
        {
            return ports.Count == 0 ? "none" : string.Join(", ", ports.Values.Select(p => $"{p.PortName} [{p.Vid ?? "?"}:{p.Pid ?? "?"}]"));
        }

        // 303A:1001 = USB-Serial/JTAG (ROM download mode or HW-CDC app), 303A:0002 = ROM download mode over USB-OTG
        private static bool IsBootloaderPort(ComPortInfo info)
        {
            return info != null && info.Vid == "303A" && (info.Pid == "1001" || info.Pid == "0002");
        }

        private async Task<string> TouchAndResolveBootloaderPortAsync(string comPort)
        {
            var initial = Snapshot();
            Log("Ports before reset: " + Describe(initial));
            try
            {
                Log($"Sending 1200 bps touch reset to {comPort}...");
                using (var port = new SerialPort(comPort, 1200, Parity.None, 8, StopBits.One))
                {
                    port.DtrEnable = true;
                    port.RtsEnable = true;
                    port.Open();
                    await Task.Delay(100);
                    port.DtrEnable = false;
                    port.RtsEnable = false;
                    await Task.Delay(100);
                    port.Close();
                }
            }
            catch (Exception ex)
            {
                Log("Touch note: " + ex.Message);
            }

            Log("Waiting for the ESP32-S3 bootloader to enumerate...");
            bool originalGone = false;
            string pending = null;
            string lastText = null;
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(200);
                var now = Snapshot();
                string text = Describe(now);
                if (text != lastText)
                {
                    Log("Ports now: " + text);
                    lastText = text;
                }

                bool originalPresent = now.ContainsKey(comPort);
                if (!originalPresent) originalGone = true;

                string candidate = null;
                if (originalGone)
                {
                    candidate = now.Values.Where(p => !initial.ContainsKey(p.PortName) && p.Vid == "303A").Select(p => p.PortName).FirstOrDefault();
                }

                if (candidate != null)
                {
                    if (string.Equals(candidate, pending, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Bootloader detected on new port " + candidate);
                        return candidate;
                    }
                    pending = candidate; // confirm on the next poll
                    continue;
                }
                pending = null;

                if (originalGone && originalPresent)
                {
                    Log($"Bootloader re-appeared on {comPort}.");
                    return comPort;
                }
            }
            Log("No new bootloader port detected, using " + comPort);
            return comPort;
        }

        private async Task WaitForPortReadyAsync(string comPort, int timeoutMs = 5000)
        {
            int ok = 0;
            string lastError = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool success = false;
                if (Snapshot().ContainsKey(comPort))
                {
                    try
                    {
                        using (var port = new SerialPort(comPort, 115200, Parity.None, 8, StopBits.One))
                        {
                            port.Open();
                            port.Close();
                        }
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }
                else
                {
                    lastError = "port not present";
                }

                ok = success ? ok + 1 : 0;
                if (ok >= 2)
                {
                    await Task.Delay(300);
                    return;
                }
                await Task.Delay(250);
            }
            Log($"{comPort} not ready after {timeoutMs} ms ({lastError ?? "unknown"}), trying anyway.");
        }

        private async Task<Tuple<string, string>> PreparePortAsync(string comPort, string chip)
        {
            var selected = ComPortInfo.Find(comPort);
            Log($"Selected port: {comPort} [{selected?.Vid ?? "?"}:{selected?.Pid ?? "?"}]");

            if (IsBootloaderPort(selected))
            {
                Log("Native USB-Serial/JTAG port, esptool resets via USB.");
                await WaitForPortReadyAsync(comPort);
                return Tuple.Create(comPort, selected.Pid == "1001" ? "usb-reset" : "no-reset");
            }

            if (chip != "esp32s3" || (selected != null && selected.Vid != null && selected.Vid != "303A"))
            {
                Log("USB-UART port (CP2102 / CH340), esptool resets via DTR/RTS.");
                await WaitForPortReadyAsync(comPort);
                return Tuple.Create(comPort, "default-reset");
            }

            // Native TinyUSB CDC of the running firmware: 1200 bps touch reboots into the ROM bootloader
            string uploadPort = await TouchAndResolveBootloaderPortAsync(comPort);
            if (!Snapshot().ContainsKey(uploadPort)) uploadPort = comPort;
            await WaitForPortReadyAsync(uploadPort);

            if (IsBootloaderPort(ComPortInfo.Find(uploadPort)))
            {
                Log($"Bootloader active on {uploadPort}, connecting without another reset.");
                return Tuple.Create(uploadPort, "no-reset");
            }
            return Tuple.Create(uploadPort, "default-reset");
        }

        private string ResolveRetryPort(string lastPort, string selectedPort)
        {
            var ports = Snapshot();
            if (ports.ContainsKey(lastPort)) return lastPort;
            if (ports.ContainsKey(selectedPort)) return selectedPort;
            var bootloader = ports.Values.FirstOrDefault(IsBootloaderPort);
            return bootloader != null ? bootloader.PortName : lastPort;
        }

        private async Task<int> RunEsptoolAsync(string exe, string args, List<string> output)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (output) output.Add(e.Data);
                    Log(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (output) output.Add(e.Data);
                    Log("ERROR: " + e.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());
                return process.ExitCode;
            }
        }

        private async Task<bool> RunWithRetriesAsync(string comPort, string chip, string description, Func<string, string, int, string> buildArgs)
        {
            string exe;
            try
            {
                exe = GetEsptoolPath();
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
                return false;
            }

            try
            {
                var prepared = await PreparePortAsync(comPort, chip);
                string uploadPort = prepared.Item1;
                string before = prepared.Item2;

                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    int baud = attempt == MaxAttempts ? FallbackBaud : DefaultBaud;
                    if (attempt > 1)
                    {
                        await Task.Delay(1500);
                        uploadPort = ResolveRetryPort(uploadPort, comPort);
                        await WaitForPortReadyAsync(uploadPort);
                        before = "default-reset";
                        Log($"");
                        Log($"Retry {attempt}/{MaxAttempts} on {uploadPort} ({baud} baud)...");
                    }

                    Log($"{description} on {uploadPort} (--before {before}, {baud} baud)...");
                    var output = new List<string>();
                    int exit = await RunEsptoolAsync(exe, buildArgs(uploadPort, before, baud), output);
                    if (exit == 0) return true;

                    bool connectionError;
                    lock (output)
                    {
                        connectionError = output.Any(line => ConnectionErrorMarkers.Any(m => line.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0));
                    }
                    if (!connectionError)
                    {
                        Log("esptool failed with a non-connection error, not retrying.");
                        return false;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Log("Exception: " + ex.Message);
                return false;
            }
        }
    }
}
