using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace DiyBeltTensioner.Core
{
    /// <summary>
    /// Checks the GitHub releases for a newer DiyBeltTensioner.dll and installs it.
    /// Like the DIY FFB Pedal plugin the update runs in two steps: the DLL is downloaded while
    /// SimHub keeps running, then a small cmd script (admin rights for the SimHub folder) waits
    /// for SimHub to exit, swaps the DLL (with backup) and starts SimHub again.
    /// </summary>
    public static class PluginUpdater
    {
        public const string DllName = "DiyBeltTensioner.dll";
        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "DiyBeltTensioner_update");

        /// <summary>Version of the running plugin, e.g. "26.45.05.2" ("dev" for local builds).</summary>
        public static string CurrentVersion
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string v = attr?.InformationalVersion ?? "";
                int plus = v.IndexOf('+');
                if (plus >= 0) v = v.Substring(0, plus);
                return ParseVersion(v) == null || v.StartsWith("0.0.0") ? "dev" : v;
            }
        }

        public static bool IsDevBuild => CurrentVersion == "dev";

        /// <summary>"26.45.05.2" -> [26, 45, 5, 2]; null if not a version tag.</summary>
        public static int[] ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string t = tag.Trim().TrimStart('v', 'V');
            var parts = t.Split('.');
            var numbers = new List<int>();
            foreach (var part in parts)
            {
                int n;
                if (!int.TryParse(part, out n) || n < 0) return null;
                numbers.Add(n);
            }
            return numbers.Count >= 2 ? numbers.ToArray() : null;
        }

        /// <summary>&gt; 0 if a is newer than b.</summary>
        public static int Compare(int[] a, int[] b)
        {
            int length = Math.Max(a.Length, b.Length);
            for (int i = 0; i < length; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        /// <summary>
        /// The newest full release with a plugin DLL that is newer than the running plugin.
        /// Returns null if there is none. Dev builds get the newest release (includeForDevBuild).
        /// </summary>
        public static FirmwareRelease FindUpdate(IEnumerable<FirmwareRelease> releases, bool includeForDevBuild)
        {
            var candidate = releases
                .Where(r => !r.Prerelease && r.PluginAsset != null && ParseVersion(r.Tag) != null)
                .OrderByDescending(r => r.PublishedAt)
                .FirstOrDefault();
            if (candidate == null) return null;
            if (IsDevBuild) return includeForDevBuild ? candidate : null;
            return Compare(ParseVersion(candidate.Tag), ParseVersion(CurrentVersion)) > 0 ? candidate : null;
        }

        /// <summary>Downloads the plugin of the release into a temp folder and checks that it is the plugin.</summary>
        public static async Task DownloadAsync(FirmwareRelease release)
        {
            var asset = release.PluginAsset;
            if (asset == null) throw new InvalidOperationException("Release " + release.Tag + " has no " + DllName + ".");

            Directory.CreateDirectory(TempDir);
            string dllPath = Path.Combine(TempDir, DllName);
            if (File.Exists(dllPath)) File.Delete(dllPath);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DiyBeltTensioner-SimHub-Plugin/1.0");
                File.WriteAllBytes(dllPath, await client.GetByteArrayAsync(asset.DownloadUrl).ConfigureAwait(false));
            }

            // Never install something that is not the plugin (error page, truncated file, ...)
            bool valid;
            try
            {
                valid = AssemblyName.GetAssemblyName(dllPath).Name == "DiyBeltTensioner";
            }
            catch (Exception)
            {
                valid = false;
            }
            if (!valid)
            {
                File.Delete(dllPath);
                throw new InvalidDataException("The downloaded file is not a valid DiyBeltTensioner plugin.");
            }
        }

        /// <summary>
        /// Starts the update script with admin rights and closes SimHub. Throws if the script cannot
        /// be started (e.g. the admin prompt was declined); nothing has been changed then.
        /// </summary>
        public static void InstallAndRestart()
        {
            string simhubDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!File.Exists(Path.Combine(simhubDir, "SimHubWPF.exe")))
            {
                throw new FileNotFoundException("SimHubWPF.exe was not found next to the plugin (" + simhubDir + ").");
            }
            if (!File.Exists(Path.Combine(TempDir, DllName)))
            {
                throw new FileNotFoundException("The update has not been downloaded.");
            }

            string scriptPath = Path.Combine(TempDir, "update.cmd");
            // %1 = SimHub folder, %2 = folder with the download. The backup must succeed before the plugin
            // is replaced, and a failed copy restores it, so SimHub always keeps a working plugin.
            File.WriteAllLines(scriptPath, new[]
            {
                "@echo off",
                "title DIY FFB Belt Tensioner plugin update",
                "echo Waiting for SimHub to close...",
                "set n=0",
                ":wait",
                @"tasklist /fi ""IMAGENAME eq SimHubWPF.exe"" 2>nul | find /i ""SimHubWPF.exe"" >nul || goto closed",
                "set /a n+=1",
                "if %n% geq 60 taskkill /f /im SimHubWPF.exe >nul 2>&1",
                "timeout /t 1 /nobreak >nul 2>&1 || ping -n 2 127.0.0.1 >nul",
                "goto wait",
                ":closed",
                "echo Installing plugin...",
                @"copy /y ""%~1\" + DllName + @""" ""%~1\" + DllName + @".bak"" >nul || goto failed",
                @"copy /y ""%~2\" + DllName + @""" ""%~1\" + DllName + @""" >nul || goto restore",
                @"del ""%~2\" + DllName + @""" >nul 2>&1",
                "echo Update completed. Restarting SimHub...",
                "timeout /t 2 /nobreak >nul 2>&1",
                // /d: SimHub resolves its data folders from the working directory; an elevated cmd starts in System32
                @"start """" /d ""%~1"" ""%~1\SimHubWPF.exe""",
                "exit /b 0",
                ":restore",
                @"copy /y ""%~1\" + DllName + @".bak"" ""%~1\" + DllName + @""" >nul",
                ":failed",
                "echo The plugin could not be replaced. The previous version was kept.",
                "timeout /t 10 /nobreak 2>nul || ping -n 11 127.0.0.1 >nul",
                @"start """" /d ""%~1"" ""%~1\SimHubWPF.exe""",
            });

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\" \"{simhubDir}\" \"{TempDir}\"\"",
                Verb = "runas",
                UseShellExecute = true,
            });
            System.Windows.Application.Current.Shutdown();
        }
    }
}
