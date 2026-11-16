using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiyBeltTensioner.Core
{
    /// <summary>Controller boards (PlatformIO environments, see platformio.envs/).</summary>
    public class BoardInfo
    {
        public string Environment { get; private set; }
        public string Name { get; private set; }
        public string Chip { get; private set; }

        /// <summary>Flash offset of bootloader.bin (0x0 on ESP32-S3, 0x1000 on the classic ESP32).</summary>
        public string BootloaderOffset => Chip == "esp32" ? "0x1000" : "0x0";

        public string DisplayName => Name + "  (" + Environment + ")";

        public override string ToString() => DisplayName;

        private BoardInfo(string env, string name, string chip)
        {
            Environment = env;
            Name = name;
            Chip = chip;
        }

        /// <summary>Known boards in display order (newest first).</summary>
        public static readonly IReadOnlyList<BoardInfo> All = new List<BoardInfo>
        {
            new BoardInfo("ControlBoard_V8", "ControlBoard V8 (ESP32-S3, native USB)", "esp32s3"),
            new BoardInfo("ControlBoard_V7", "ControlBoard V7 (ESP32-S3, native USB)", "esp32s3"),
            new BoardInfo("ControlBoard_V6", "ControlBoard V6 (ESP32-S3, CP2102)", "esp32s3"),
            new BoardInfo("ControlBoard_V5_without_espnow", "ControlBoard V5 (ESP32-S3, CP2102)", "esp32s3"),
            new BoardInfo("ControlBoard_V4", "ControlBoard V4 (ESP32-S3, CP2102)", "esp32s3"),
            new BoardInfo("ControlBoard_PCBA_V2X_without_espnow", "ControlBoard PCBA V2.x (LOLIN S3 Mini, native USB)", "esp32s3"),
            new BoardInfo("ControlBoard_PCBA_V1X", "ControlBoard PCBA V1.x (ESP32-S3, native USB)", "esp32s3"),
            new BoardInfo("esp32_devkit", "Generic ESP32 DevKit v1", "esp32"),
        };

        /// <summary>Board of an environment name; unknown names (new boards) get a generic entry.</summary>
        public static BoardInfo FromEnvironment(string env)
        {
            var known = All.FirstOrDefault(b => string.Equals(b.Environment, env, StringComparison.OrdinalIgnoreCase));
            if (known != null) return known;
            bool classic = env.IndexOf("esp32_devkit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           env.IndexOf("esp32dev", StringComparison.OrdinalIgnoreCase) >= 0;
            return new BoardInfo(env, env.Replace('_', ' '), classic ? "esp32" : "esp32s3");
        }

        private static readonly Regex AssetName = new Regex(@"^DIY-Sim-Racing-FFB-BeltTensioner_(.+)\.zip$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The boards a release has firmware for, in display order.</summary>
        public static List<BoardInfo> FromRelease(FirmwareRelease release)
        {
            var boards = new List<BoardInfo>();
            foreach (var asset in release.Assets)
            {
                var m = AssetName.Match(asset.Name ?? "");
                if (m.Success) boards.Add(FromEnvironment(m.Groups[1].Value));
            }
            return boards
                .OrderBy(b => { int i = IndexOf(b); return i < 0 ? int.MaxValue : i; })
                .ThenBy(b => b.Environment, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int IndexOf(BoardInfo board)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (ReferenceEquals(All[i], board)) return i;
            }
            return -1;
        }
    }
}
