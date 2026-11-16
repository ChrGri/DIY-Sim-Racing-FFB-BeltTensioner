using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiyBeltTensioner.Core
{
    /// <summary>Result of DIFF_SERVO (servo registers compared with the tuned table).</summary>
    public class ServoCheckResult
    {
        private static readonly Regex SummaryRegex = new Regex(@"Summary:\s*(\d+)\s+registers,\s*(\d+)\s+differ,\s*(\d+)\s+read errors", RegexOptions.Compiled);

        public int Registers { get; private set; }
        public int Differ { get; private set; }
        public int ReadErrors { get; private set; }

        /// <summary>The summary line arrived, so the list is complete.</summary>
        public bool Complete { get; private set; }

        /// <summary>Servo not enabled / not answering (firmware message).</summary>
        public bool ServoUnavailable { get; private set; }

        /// <summary>Differing registers, e.g. "Pr0.22 (0x0016)  actual: 234  tuned: 0".</summary>
        public List<string> DifferingLines { get; } = new List<string>();

        /// <summary>Registers that could not be read.</summary>
        public List<string> ErrorLines { get; } = new List<string>();

        public static ServoCheckResult Parse(IList<string> reply)
        {
            if (reply == null || reply.Count == 0) return null;
            var r = new ServoCheckResult();
            foreach (var raw in reply)
            {
                string line = raw.Trim();
                if (line.Contains("is not enabled"))
                {
                    r.ServoUnavailable = true;
                    continue;
                }
                var m = SummaryRegex.Match(line);
                if (m.Success)
                {
                    r.Registers = int.Parse(m.Groups[1].Value);
                    r.Differ = int.Parse(m.Groups[2].Value);
                    r.ReadErrors = int.Parse(m.Groups[3].Value);
                    r.Complete = true;
                    continue;
                }
                if (line.EndsWith("<-- differs"))
                {
                    r.DifferingLines.Add(Regex.Replace(line.Substring(0, line.Length - "<-- differs".Length).Trim(), @"\s{2,}", "   "));
                }
                else if (line.EndsWith("read error"))
                {
                    r.ErrorLines.Add(line.Substring(0, line.Length - "read error".Length).Trim());
                }
            }
            if (!r.Complete)
            {
                r.Differ = r.DifferingLines.Count;
                r.ReadErrors = r.ErrorLines.Count;
            }
            return r;
        }

        public bool Ok => Complete && Differ == 0 && ReadErrors == 0;

        public string SummaryText
        {
            get
            {
                if (ServoUnavailable) return "servo check: the servo is not enabled or not connected.";
                string text = Differ == 0
                    ? "servo check: all registers match the tuned values"
                    : "servo check: " + Differ + " register" + (Differ == 1 ? "" : "s") + " differ from the tuned values";
                if (ReadErrors > 0) text += ", " + ReadErrors + " read error" + (ReadErrors == 1 ? "" : "s");
                if (!Complete) text += " (incomplete answer, update the firmware and check again)";
                return text + ".";
            }
        }

        public string DetailText
        {
            get
            {
                var lines = DifferingLines.ToList();
                lines.AddRange(ErrorLines.Select(l => l + "   read error"));
                return string.Join(System.Environment.NewLine, lines);
            }
        }
    }
}
