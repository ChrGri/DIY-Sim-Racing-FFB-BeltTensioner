using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DiyBeltTensioner.UI
{
    /// <summary>
    /// Slider with "Title: value unit" header like SliderWithLabel of the DIY FFB Pedal plugin.
    /// The value can be typed into the box of the header (Enter or leaving the box applies it).
    /// </summary>
    public class LabeledSlider : UserControl
    {
        private readonly TextBlock titleText;
        private readonly TextBox valueBox;
        private readonly TextBlock unitText;
        private readonly Slider slider;
        private bool updating;

        /// <summary>Raised when the user changed the value (not when set from code).</summary>
        public event EventHandler<double> ValueCommitted;

        public LabeledSlider()
        {
            titleText = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Arial"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            valueBox = new TextBox
            {
                Width = 52,
                Margin = new Thickness(4, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Right,
            };
            valueBox.SetResourceReference(StyleProperty, "ValueBox");
            unitText = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Arial"), FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            header.Children.Add(titleText);
            header.Children.Add(valueBox);
            header.Children.Add(unitText);

            slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, Height = 14 };
            slider.SetResourceReference(StyleProperty, "PedalSlider");

            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
            panel.Children.Add(header);
            panel.Children.Add(slider);
            Content = panel;

            slider.ValueChanged += Slider_ValueChanged;
            valueBox.KeyDown += ValueBox_KeyDown;
            valueBox.LostFocus += (s, e) => CommitText();
            UpdateText();
        }

        public string Title
        {
            get { return titleText.Text; }
            set { titleText.Text = value + ":"; }
        }

        public string Unit
        {
            get { return unitText.Text; }
            set { unitText.Text = value; }
        }

        public double Minimum
        {
            get { return slider.Minimum; }
            set { slider.Minimum = value; }
        }

        public double Maximum
        {
            get { return slider.Maximum; }
            set { slider.Maximum = value; }
        }

        /// <summary>Slider step (values are rounded to it).</summary>
        public double Step { get; set; } = 1;

        public int Decimals { get; set; } = 0;

        public double Value
        {
            get { return slider.Value; }
            set
            {
                updating = true;
                slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
                updating = false;
                UpdateText();
            }
        }

        /// <summary>Tooltip of title and slider.</summary>
        public string Hint
        {
            get { return titleText.ToolTip as string; }
            set
            {
                titleText.ToolTip = value;
                slider.ToolTip = value;
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updating) return;
            double rounded = Step > 0 ? Math.Round(e.NewValue / Step) * Step : e.NewValue;
            if (Math.Abs(rounded - e.NewValue) > 1e-9)
            {
                updating = true;
                slider.Value = rounded;
                updating = false;
            }
            UpdateText();
            ValueCommitted?.Invoke(this, slider.Value);
        }

        private void UpdateText()
        {
            valueBox.Text = slider.Value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
        }

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                UpdateText();
                e.Handled = true;
            }
        }

        private void CommitText()
        {
            double v;
            string text = (valueBox.Text ?? "").Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                v = Math.Max(slider.Minimum, Math.Min(slider.Maximum, v));
                if (Math.Abs(v - slider.Value) > 1e-9)
                {
                    updating = true;
                    slider.Value = v;
                    updating = false;
                    ValueCommitted?.Invoke(this, v);
                }
            }
            UpdateText();
        }
    }
}
