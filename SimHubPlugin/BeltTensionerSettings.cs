namespace DiyBeltTensioner
{
    /// <summary>Settings of one belt channel (one controller board on one COM port).</summary>
    public class ChannelSettings
    {
        public string ComPort { get; set; } = "";
        public string BoardEnvironment { get; set; } = "ControlBoard_V6";
        public bool MotionReverse { get; set; } = false;
    }

    /// <summary>
    /// Plugin settings, stored by SimHub in PluginsData\Common\DiyBeltTensioner.GeneralSettings.json
    /// </summary>
    public class BeltTensionerSettings
    {
        // Rig layout: one belt tensioner in the middle, or one per shoulder belt
        public bool Dual { get; set; } = false;
        public ChannelSettings Single { get; set; } = new ChannelSettings();
        public ChannelSettings Left { get; set; } = new ChannelSettings();
        public ChannelSettings Right { get; set; } = new ChannelSettings();

        // Connection / plugin
        public bool AutoConnect { get; set; } = false;
        public bool LiveStatus { get; set; } = true;
        public bool CheckPluginUpdates { get; set; } = true;
        public string SkippedPluginVersion { get; set; } = "";

        // Firmware
        public string GitHubRepository { get; set; } = "ChrGri/DIY-Sim-Racing-FFB-BeltTensioner";
        public int FirmwareSource { get; set; } = 0; // 0 = latest GitHub release, 1 = local .zip, 2 = local folder
        public string LocalFirmwarePath { get; set; } = "";

        // Motion test
        public int TestMode { get; set; } = 0; // 0 = hold, 1 = sine, 2 = brake pulse
        public double TestPositionPct { get; set; } = 30;
        public double TestSineCenterPct { get; set; } = 40;
        public double TestSineAmplitudePct { get; set; } = 20;
        public double TestSineFrequencyHz { get; set; } = 1.0;
        public double TestPulseBasePct { get; set; } = 20;
        public double TestPulsePeakPct { get; set; } = 70;
        public int TestPulseDurationMs { get; set; } = 300;
        public int TestPulseIntervalMs { get; set; } = 2000;

        // SimHub Motion limits (also used by the motion test)
        public int StepperFrequency { get; set; } = 36000;
        public int StepperAcceleration { get; set; } = 100000;
    }
}
