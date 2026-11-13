namespace DiyBeltTensioner
{
    /// <summary>
    /// Plugin settings, stored by SimHub in PluginsData\Common\DiyBeltTensioner.GeneralSettings.json
    /// </summary>
    public class BeltTensionerSettings
    {
        // Connection
        public string ComPort { get; set; } = "";
        public bool AutoConnect { get; set; } = false;

        // Plugin updates
        public bool CheckPluginUpdates { get; set; } = true;
        public string SkippedPluginVersion { get; set; } = "";

        // Firmware
        public string GitHubRepository { get; set; } = "ChrGri/DIY-Sim-Racing-FFB-BeltTensioner";
        public string BoardEnvironment { get; set; } = "ControlBoard_V6";
        public int FirmwareSource { get; set; } = 0; // 0 = GitHub release, 1 = local .zip, 2 = local folder
        public string LocalFirmwarePath { get; set; } = "";

        // Test
        public int TestMode { get; set; } = 0; // 0 = hold, 1 = sine, 2 = brake pulse
        public double TestPositionPct { get; set; } = 30;
        public double TestSineCenterPct { get; set; } = 40;
        public double TestSineAmplitudePct { get; set; } = 20;
        public double TestSineFrequencyHz { get; set; } = 1.0;
        public double TestPulseBasePct { get; set; } = 20;
        public double TestPulsePeakPct { get; set; } = 70;
        public int TestPulseDurationMs { get; set; } = 300;
        public int TestPulseIntervalMs { get; set; } = 2000;
        public bool TestAxis1 { get; set; } = true;
        public bool TestAxis2 { get; set; } = true;
        public int StepperFrequency { get; set; } = 36000;
        public int StepperAcceleration { get; set; } = 100000;

        // SimHub Motion preset
        public bool MotionDual { get; set; } = false;
        public string MotionPortSingle { get; set; } = "";
        public string MotionPortLeft { get; set; } = "";
        public string MotionPortRight { get; set; } = "";
        public bool MotionReverseSingle { get; set; } = false;
        public bool MotionReverseLeft { get; set; } = false;
        public bool MotionReverseRight { get; set; } = false;
    }
}
