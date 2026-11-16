using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DiyBeltTensioner.UI
{
    /// <summary>
    /// Draws the stroke of the actuator like the force-travel view of the pedal plugin:
    /// 0..100 % of the working range with the safety window, the idle (park) position,
    /// the direction in which braking pulls and the live target position of a test.
    /// </summary>
    public class TravelView : FrameworkElement
    {
        public double RangeMm { get; set; } = 60;
        public double MinPct { get; set; } = 5;
        public double MaxPct { get; set; } = 95;
        public double IdlePct { get; set; } = 10;
        public bool Inverted { get; set; }
        public bool CenterAfterHoming { get; set; }

        /// <summary>Measured carriage position in percent of the working range (NaN = unknown).</summary>
        public double ActualPct { get; set; } = double.NaN;

        /// <summary>Commanded position in percent of the game input (NaN = none).</summary>
        public double LivePct { get; set; } = double.NaN;

        private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        private static readonly Brush DimText = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
        private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        private static readonly Brush LiveBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        private static readonly Brush LimitBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));

        static TravelView()
        {
            BgBrush.Freeze();
            GridBrush.Freeze();
            TextBrush.Freeze();
            DimText.Freeze();
            IdleBrush.Freeze();
            LiveBrush.Freeze();
            LimitBrush.Freeze();
        }

        public void Refresh()
        {
            InvalidateVisual();
        }

        private Brush Accent
        {
            get
            {
                return TryFindResource("AccentColorBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
            }
        }

        private FormattedText Text(string text, double size, Brush brush, bool bold = false)
        {
#pragma warning disable CS0618 // pixelsPerDip overload needs .NET 4.6.2 APIs of the visual
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                size, brush);
#pragma warning restore CS0618
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 100 || h < 80) return;

            var accent = Accent;
            dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 4, 4);

            const double left = 40, right = 40;
            double barTop = h * 0.42, barHeight = 22;
            double x0 = left, x1 = w - right;
            Func<double, double> X = pct => x0 + (x1 - x0) * Math.Max(0, Math.Min(100, pct)) / 100.0;

            // Title
            dc.DrawText(Text("STROKE", 13, Brushes.White, true), new Point(14, 10));
            dc.DrawText(Text(string.Format(CultureInfo.InvariantCulture, "working range {0:0.#} mm", RangeMm), 11, TextBrush), new Point(90, 12));

            // Grid lines every 10 %
            var gridPen = new Pen(GridBrush, 1);
            for (int p = 0; p <= 100; p += 10)
            {
                double x = X(p);
                dc.DrawLine(gridPen, new Point(x, barTop - 14), new Point(x, barTop + barHeight + 14));
                var label = Text(p + "%", 9, DimText);
                dc.DrawText(label, new Point(x - label.Width / 2, barTop + barHeight + 16));
            }

            // Whole stroke and usable (safety) window
            dc.DrawRectangle(GridBrush, null, new Rect(x0, barTop, x1 - x0, barHeight));
            double minX = X(MinPct), maxX = X(MaxPct);
            var windowBrush = accent.Clone();
            windowBrush.Opacity = 0.35;
            dc.DrawRectangle(windowBrush, new Pen(accent, 1.5), new Rect(minX, barTop, Math.Max(1, maxX - minX), barHeight));

            // Hard limits
            var limitPen = new Pen(LimitBrush, 2);
            dc.DrawLine(limitPen, new Point(minX, barTop - 6), new Point(minX, barTop + barHeight + 6));
            dc.DrawLine(limitPen, new Point(maxX, barTop - 6), new Point(maxX, barTop + barHeight + 6));
            DrawLabelAbove(dc, string.Format(CultureInfo.InvariantCulture, "MIN {0:0.#}%\n{1:0.#} mm", MinPct, RangeMm * MinPct / 100), minX, barTop, LimitBrush);
            DrawLabelAbove(dc, string.Format(CultureInfo.InvariantCulture, "MAX {0:0.#}%\n{1:0.#} mm", MaxPct, RangeMm * MaxPct / 100), maxX, barTop, LimitBrush);

            // Idle / park position (mirrored when inverted, like the firmware)
            double idle = Inverted ? 100 - IdlePct : IdlePct;
            double idleX = X(idle);
            var idlePen = new Pen(IdleBrush, 2) { DashStyle = DashStyles.Dash };
            dc.DrawLine(idlePen, new Point(idleX, barTop - 2), new Point(idleX, barTop + barHeight + 2));
            var idleText = Text(string.Format(CultureInfo.InvariantCulture, "PARK {0:0.#}%", idle), 10, IdleBrush, true);
            dc.DrawText(idleText, new Point(Math.Min(x1 - idleText.Width, Math.Max(x0, idleX - idleText.Width / 2)), barTop + barHeight + 30));

            if (CenterAfterHoming)
            {
                var c = Text("after homing: 50 %", 9, IdleBrush);
                dc.DrawText(c, new Point(X(50) - c.Width / 2, barTop + barHeight + 44));
            }

            // Braking direction arrow
            double arrowY = h - 18;
            double from = Inverted ? maxX : minX, to = Inverted ? minX : maxX;
            var arrowPen = new Pen(accent, 2);
            dc.DrawLine(arrowPen, new Point(from, arrowY), new Point(to, arrowY));
            double dir = Math.Sign(to - from);
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(new Point(to, arrowY), true, true);
                g.LineTo(new Point(to - dir * 9, arrowY - 5), true, false);
                g.LineTo(new Point(to - dir * 9, arrowY + 5), true, false);
            }
            head.Freeze();
            dc.DrawGeometry(accent, null, head);
            var dirText = Text("0 % game input  →  100 % (braking pulls)", 9, TextBrush);
            dc.DrawText(dirText, new Point((x0 + x1) / 2 - dirText.Width / 2, arrowY - 15));

            // Carriage position reported by the controller
            if (!double.IsNaN(ActualPct))
            {
                double ax = X(ActualPct);
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(ax, barTop - 1), true, true);
                    g.LineTo(new Point(ax - 6, barTop - 11), true, false);
                    g.LineTo(new Point(ax + 6, barTop - 11), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(Brushes.White, null, tri);
                dc.DrawLine(new Pen(Brushes.White, 1.5), new Point(ax, barTop), new Point(ax, barTop + barHeight));
                var at = Text(string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", ActualPct), 9, Brushes.White);
                dc.DrawText(at, new Point(Math.Min(x1 - at.Width, Math.Max(x0, ax - at.Width / 2)), barTop - 13 - at.Height));
            }

            // Live position of a running test (game input 0..100 % mapped into the window)
            if (!double.IsNaN(LivePct))
            {
                double input = Math.Max(0, Math.Min(100, LivePct));
                double pos = Inverted ? MaxPct - input / 100 * (MaxPct - MinPct) : MinPct + input / 100 * (MaxPct - MinPct);
                double lx = X(pos);
                dc.DrawRectangle(LiveBrush, null, new Rect(lx - 3, barTop - 4, 6, barHeight + 8));
            }
        }

        private void DrawLabelAbove(DrawingContext dc, string text, double x, double barTop, Brush brush)
        {
            var t = Text(text, 9, brush);
            t.TextAlignment = TextAlignment.Center;
            dc.DrawText(t, new Point(x, barTop - 10 - t.Height));
        }
    }
}
