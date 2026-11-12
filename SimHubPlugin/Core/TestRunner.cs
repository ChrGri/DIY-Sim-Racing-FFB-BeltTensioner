using System;
using System.Diagnostics;
using System.Threading;

namespace DiyBeltTensioner.Core
{
    public enum TestMode
    {
        Hold = 0,        // hold the position of the slider
        Sine = 1,        // continuous sine around a center position
        BrakePulse = 2,  // repeated short pull from base to peak (like a braking zone)
    }

    /// <summary>
    /// Streams target positions (CMD 1) to the controller like SimHub Motion does, so the
    /// actuator can be tested without a game. The parameters can be changed while running.
    /// When stopped, the controller is parked (CMD 12).
    /// </summary>
    public class TestRunner
    {
        public const double IdlePercent = 10.0; // IDLE_POSITION_PCT of the firmware
        private const int UpdateIntervalMs = 10;

        private readonly BeltTensionerLink link;
        private Thread thread;
        private volatile bool running;

        public TestRunner(BeltTensionerLink link)
        {
            this.link = link;
        }

        public bool IsRunning => running;

        // Parameters (percent of the working range)
        public volatile TestMode Mode = TestMode.Hold;
        public double HoldPercent = 30;
        public double SineCenter = 40;
        public double SineAmplitude = 20;
        public double SineFrequencyHz = 1.0;
        public double PulseBase = 20;
        public double PulsePeak = 70;
        public int PulseDurationMs = 300;
        public int PulseIntervalMs = 2000;
        public volatile bool Axis1 = true;
        public volatile bool Axis2 = true;

        /// <summary>Last commanded position in percent (for display).</summary>
        public double CurrentPercent { get; private set; }

        /// <summary>Raised on the test thread when streaming fails (e.g. connection lost).</summary>
        public event Action<string> Failed;

        public void Start(int stepperFrequency, int stepperAcceleration)
        {
            Stop(park: false);
            if (!link.IsOpen) throw new InvalidOperationException("Not connected");

            ushort f = (ushort)Math.Max(1, Math.Min(65535, stepperFrequency));
            uint a = (uint)Math.Max(1, stepperAcceleration);
            link.SetMaxSpeed(f, f);
            link.SetMaxAcceleration(a, a);

            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "BeltTensionerTest" };
            thread.Start();
        }

        public void Stop(bool park)
        {
            running = false;
            var t = thread;
            thread = null;
            if (t != null && t != Thread.CurrentThread) t.Join(500);
            if (park && link.IsOpen)
            {
                try { link.ParkNow(); } catch (Exception) { }
            }
        }

        private double Target(double seconds)
        {
            switch (Mode)
            {
                case TestMode.Sine:
                    return SineCenter + SineAmplitude * Math.Sin(2 * Math.PI * Math.Max(0.01, SineFrequencyHz) * seconds);
                case TestMode.BrakePulse:
                    {
                        int interval = Math.Max(PulseDurationMs + 100, PulseIntervalMs);
                        double tMs = (seconds * 1000.0) % interval;
                        return tMs < PulseDurationMs ? PulsePeak : PulseBase;
                    }
                default:
                    return HoldPercent;
            }
        }

        private void Loop()
        {
            var sw = Stopwatch.StartNew();
            while (running)
            {
                double pct = Math.Max(0, Math.Min(100, Target(sw.Elapsed.TotalSeconds)));
                CurrentPercent = pct;
                ushort active = BeltTensionerLink.PercentToValue(pct);
                ushort idle = BeltTensionerLink.PercentToValue(IdlePercent);
                try
                {
                    link.SetPosition(Axis1 ? active : idle, Axis2 ? active : idle);
                }
                catch (Exception ex)
                {
                    running = false;
                    Failed?.Invoke(ex.Message);
                    return;
                }
                Thread.Sleep(UpdateIntervalMs);
            }
        }
    }
}
