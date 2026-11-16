using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DiyBeltTensioner.Core
{
    /// <summary>One SimHub DIY Belt Tensioner controller (= one board / COM port) in the preset.</summary>
    public class MotionControllerSetup
    {
        public string ComPort { get; set; }
        public bool Reverse { get; set; }
    }

    /// <summary>
    /// Builds a SimHub Motion "Import all platform settings" file (.shmotionoutput) from the
    /// presets in docs/motionPluginSetup, with the user's COM ports and limits filled in.
    /// </summary>
    public static class MotionPresetBuilder
    {
        // Actuator roles used by SimHub Motion (taken from the reference presets)
        private const int RoleUnused = 0;
        private const int RoleDualLeft = 21;
        private const int RoleDualRight = 22;
        private const int RoleSingleCenter = 23;

        private static JObject LoadTemplate(bool dual)
        {
            string name = dual ? "DiyBeltTensioner.Presets.Dual.shmotionoutput" : "DiyBeltTensioner.Presets.Single.shmotionoutput";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new FileNotFoundException("Embedded preset not found: " + name);
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return JObject.Parse(reader.ReadToEnd());
                }
            }
        }

        private static JObject CreateController(JObject template, MotionControllerSetup setup, int role, int frequency, int acceleration)
        {
            var aggregated = (JObject)template.DeepClone();
            aggregated["IsEnabled"] = true;
            var settings = (JObject)aggregated["Output"]["Settings"];
            settings["SerialPort"] = setup.ComPort ?? "";
            settings["StepperFrequency"] = Math.Max(1, Math.Min(36000, frequency));
            settings["StepperAcceleration"] = Math.Max(1, Math.Min(100000, acceleration));
            settings["UseRotationCompensation"] = false;
            settings["OutputId"] = Guid.NewGuid().ToString();

            var main = new JObject { ["Role"] = role, ["MotionTarget"] = 0 };
            if (setup.Reverse) main["ReverseDirection"] = true;
            settings["ActuatorOrderingSettings"] = new JObject
            {
                ["ConfigurationDone"] = true,
                ["Roles"] = new JArray(main, new JObject { ["Role"] = RoleUnused, ["MotionTarget"] = 0 }),
            };
            return aggregated;
        }

        /// <param name="single">Controller of a single belt tensioner (used when <paramref name="right"/> is null).</param>
        /// <param name="right">Controller of the right belt for a dual belt tensioner, the first one is then the left belt.</param>
        public static string Build(MotionControllerSetup single, MotionControllerSetup right, int frequency, int acceleration)
        {
            bool dual = right != null;
            var preset = LoadTemplate(dual);
            var outputs = (JArray)preset["Output"]["AggregatedOutputs"];
            var template = (JObject)outputs.First(o => (string)o["Output"]["TypeName"] == "DIYSimHubBeltTensionerOutput");

            var result = new JArray();
            if (dual)
            {
                result.Add(CreateController(template, single, RoleDualLeft, frequency, acceleration));
                result.Add(CreateController(template, right, RoleDualRight, frequency, acceleration));
            }
            else
            {
                result.Add(CreateController(template, single, RoleSingleCenter, frequency, acceleration));
            }
            preset["Output"]["AggregatedOutputs"] = result;

            return preset.ToString(Formatting.Indented);
        }
    }
}
