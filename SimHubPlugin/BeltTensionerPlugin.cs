using DiyBeltTensioner.Core;
using DiyBeltTensioner.UI;
using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Reflection;
using System.Windows.Media;

namespace DiyBeltTensioner
{
    [PluginDescription("Flash, configure and test the DIY Sim Racing FFB Belt Tensioner and create SimHub Motion presets for it.")]
    [PluginAuthor("ChrGri")]
    [PluginName("DIY FFB Belt Tensioner")]
    public class BeltTensionerPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public BeltTensionerSettings Settings { get; private set; }

        /// <summary>Serial connection to the controller (shared by all tabs).</summary>
        public BeltTensionerLink Link { get; private set; }

        /// <summary>Generates the test motion (hold / sine / brake pulse).</summary>
        public TestRunner Tester { get; private set; }

        public PluginManager PluginManager { get; set; }

        private ImageSource icon;

        public ImageSource PictureIcon
        {
            get
            {
                if (icon != null) return icon;
                try
                {
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DiyBeltTensioner.icon.png");
                    if (stream == null) return null;
                    // Bitmap keeps reading from the stream, so neither is disposed
                    icon = this.ToIcon(new System.Drawing.Bitmap(stream));
                }
                catch (Exception)
                {
                    icon = null;
                }
                return icon;
            }
        }

        public string LeftMenuTitle => "FFB Belt Tensioner";

        private BeltTensionerControl control;

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("DIY FFB Belt Tensioner plugin starting");
            Settings = this.ReadCommonSettings<BeltTensionerSettings>("GeneralSettings", () => new BeltTensionerSettings());
            Link = new BeltTensionerLink();
            Tester = new TestRunner(Link);

            // Properties for dashboards
            this.AttachDelegate("Connected", () => Link.IsOpen);
            this.AttachDelegate("ComPort", () => Link.PortName);
            this.AttachDelegate("FirmwareVersion", () => Link.FirmwareVersion);
            this.AttachDelegate("EnabledMotors", () => Link.EnabledMotors);
            this.AttachDelegate("TestRunning", () => Tester.IsRunning);

            if (Settings.AutoConnect && !string.IsNullOrEmpty(Settings.ComPort))
            {
                try
                {
                    Link.Open(Settings.ComPort);
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn("DIY FFB Belt Tensioner: auto connect to " + Settings.ComPort + " failed: " + ex.Message);
                }
            }
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // The belt tensioner itself is driven by SimHub Motion, nothing to do here.
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            if (control == null)
            {
                control = new BeltTensionerControl(this);
            }
            return control;
        }

        public void SaveSettings()
        {
            try
            {
                this.SaveCommonSettings("GeneralSettings", Settings);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("DIY FFB Belt Tensioner: saving settings failed: " + ex.Message);
            }
        }

        public void End(PluginManager pluginManager)
        {
            try
            {
                Tester?.Stop(park: true);
            }
            catch (Exception)
            {
            }
            Link?.Close();
            SaveSettings();
        }
    }
}
