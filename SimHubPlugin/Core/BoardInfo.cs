using System.Collections.Generic;

namespace DiyBeltTensioner.Core
{
    /// <summary>Supported controller boards (PlatformIO environments, see platformio.envs/).</summary>
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

        public static readonly IReadOnlyList<BoardInfo> All = new List<BoardInfo>
        {
            new BoardInfo("ControlBoard_V4", "ControlBoard V4 (ESP32-S3 DevKit, CP2102)", "esp32s3"),
            new BoardInfo("ControlBoard_V5_without_espnow", "ControlBoard V5 (ESP32-S3 DevKit, CP2102)", "esp32s3"),
            new BoardInfo("ControlBoard_V6", "ControlBoard V6 (ESP32-S3 DevKit, CP2102)", "esp32s3"),
            new BoardInfo("ControlBoard_V7", "ControlBoard V7 (ESP32-S3 Zero, native USB)", "esp32s3"),
            new BoardInfo("ControlBoard_V8", "ControlBoard V8 (ESP32-S3 Zero, native USB)", "esp32s3"),
            new BoardInfo("ControlBoard_PCBA_V1X", "ControlBoard PCBA V1.x (native USB)", "esp32s3"),
            new BoardInfo("ControlBoard_PCBA_V2X_without_espnow", "ControlBoard PCBA V2.x (LOLIN S3 Mini, native USB)", "esp32s3"),
            new BoardInfo("esp32_devkit", "Generic ESP32 DevKit v1", "esp32"),
        };
    }
}
