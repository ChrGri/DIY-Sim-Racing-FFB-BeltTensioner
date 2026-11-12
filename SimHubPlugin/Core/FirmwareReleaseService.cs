using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DiyBeltTensioner.Core
{
    public class ReleaseAsset
    {
        public string Name { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
    }

    public class FirmwareRelease
    {
        public string Tag { get; set; }
        public string Name { get; set; }
        public DateTime PublishedAt { get; set; }
        public string HtmlUrl { get; set; }
        public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();

        /// <summary>Release archive of a board: DIY-Sim-Racing-FFB-BeltTensioner_&lt;environment&gt;.zip</summary>
        public ReleaseAsset FindAsset(string environment)
        {
            string suffix = "_" + environment + ".zip";
            return Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Downloads pre-compiled firmware from the GitHub releases of the belt tensioner repository.</summary>
    public static class FirmwareReleaseService
    {
        private static readonly HttpClient http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DiyBeltTensioner-SimHub-Plugin/1.0");
            return client;
        }

        public static string CacheRoot
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiyBeltTensioner", "firmware");
            }
        }

        public static async Task<FirmwareRelease> GetLatestAsync(string repository)
        {
            string url = "https://api.github.com/repos/" + repository.Trim().Trim('/') + "/releases/latest";
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await http.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} for {url}");
                    }

                    var json = JObject.Parse(body);
                    var release = new FirmwareRelease
                    {
                        Tag = (string)json["tag_name"],
                        Name = (string)json["name"],
                        HtmlUrl = (string)json["html_url"],
                    };
                    DateTime published;
                    if (DateTime.TryParse((string)json["published_at"], out published)) release.PublishedAt = published;

                    var assets = json["assets"] as JArray;
                    if (assets != null)
                    {
                        foreach (var a in assets)
                        {
                            release.Assets.Add(new ReleaseAsset
                            {
                                Name = (string)a["name"],
                                DownloadUrl = (string)a["browser_download_url"],
                                Size = (long?)a["size"] ?? 0,
                            });
                        }
                    }
                    return release;
                }
            }
        }

        /// <summary>Downloads (or reuses a cached copy of) the board archive and extracts it. Returns the extract folder.</summary>
        public static async Task<string> DownloadAndExtractAsync(FirmwareRelease release, string environment, Action<string> log)
        {
            var asset = release.FindAsset(environment);
            if (asset == null)
            {
                throw new InvalidOperationException($"Release {release.Tag} has no firmware for {environment}.");
            }

            string folder = Path.Combine(CacheRoot, SafeName(release.Tag), environment);
            var cached = FirmwareImage.FromPath(folder);
            if (cached != null && cached.IsValid)
            {
                log?.Invoke($"Using cached firmware {release.Tag} for {environment}: {folder}");
                return folder;
            }

            Directory.CreateDirectory(folder);
            string zipPath = Path.Combine(folder, asset.Name);
            log?.Invoke($"Downloading {asset.Name} ({asset.Size / 1024} kB)...");
            using (var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                {
                    await input.CopyToAsync(output).ConfigureAwait(false);
                }
            }

            ExtractZip(zipPath, folder);
            log?.Invoke("Extracted to " + folder);
            return folder;
        }

        /// <summary>Extracts a local release .zip to a temp folder and returns the folder.</summary>
        public static string ExtractLocalZip(string zipPath)
        {
            string folder = Path.Combine(CacheRoot, "local", SafeName(Path.GetFileNameWithoutExtension(zipPath)) + "_" + File.GetLastWriteTimeUtc(zipPath).Ticks);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            ExtractZip(zipPath, folder);
            return folder;
        }

        private static void ExtractZip(string zipPath, string folder)
        {
            string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                    string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip slip guard
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
