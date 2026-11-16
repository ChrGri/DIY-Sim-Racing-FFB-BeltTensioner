using DiyBeltTensioner.Core;
using DiyBeltTensioner.UI;
using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
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

        /// <summary>Single belt tensioner (layout "single").</summary>
        public TensionerChannel SingleChannel { get; private set; }

        /// <summary>Left and right belt (layout "dual", one controller board each).</summary>
        public TensionerChannel LeftChannel { get; private set; }
        public TensionerChannel RightChannel { get; private set; }

        /// <summary>Channels of the selected layout.</summary>
        public IReadOnlyList<TensionerChannel> ActiveChannels
        {
            get
            {
                return Settings.Dual
                    ? new[] { LeftChannel, RightChannel }
                    : new[] { SingleChannel };
            }
        }

        public IReadOnlyList<TensionerChannel> AllChannels => new[] { SingleChannel, LeftChannel, RightChannel };

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
            if (Settings.Single == null) Settings.Single = new ChannelSettings();
            if (Settings.Left == null) Settings.Left = new ChannelSettings();
            if (Settings.Right == null) Settings.Right = new ChannelSettings();

            SingleChannel = new TensionerChannel("Single", "Belt", Settings.Single);
            LeftChannel = new TensionerChannel("Left", "Left belt", Settings.Left);
            RightChannel = new TensionerChannel("Right", "Right belt", Settings.Right);

            // Properties for dashboards, e.g. BeltTensionerPlugin.Left.Connected
            foreach (var channel in AllChannels)
            {
                var c = channel;
                this.AttachDelegate(c.Key + ".Connected", () => c.IsOpen);
                this.AttachDelegate(c.Key + ".ComPort", () => c.Link.PortName);
                this.AttachDelegate(c.Key + ".Status", () => c.StatusText);
                this.AttachDelegate(c.Key + ".ServoLoad", () => c.LastLoad ?? 0);
                this.AttachDelegate(c.Key + ".TestRunning", () => c.Tester.IsRunning);
            }

            if (Settings.AutoConnect)
            {
                foreach (var channel in ActiveChannels)
                {
                    if (string.IsNullOrEmpty(channel.Settings.ComPort)) continue;
                    try
                    {
                        channel.Link.Open(channel.Settings.ComPort);
                    }
                    catch (Exception ex)
                    {
                        SimHub.Logging.Current.Warn("DIY FFB Belt Tensioner: auto connect to " + channel.Settings.ComPort + " failed: " + ex.Message);
                    }
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
            foreach (var channel in AllChannels)
            {
                try
                {
                    channel.Tester.Stop(park: true);
                }
                catch (Exception)
                {
                }
                channel.Link.Close();
            }
            SaveSettings();
        }
    }
}
