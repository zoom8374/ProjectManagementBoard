using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CalendarTool
{
    /// <summary>
    /// Scales text by walking the visual tree: every TextBlock / Control remembers its original size (<see cref="BaseSizeProperty"/>)
    /// and gets base × scale. Icon fonts are left alone. Elements created later are handled one by one from their Loaded event.
    /// </summary>
    public static class FontScaler
    {
        public const double MinScale = 0.8, MaxScale = 1.4, Step = 0.1;

        static readonly DependencyProperty BaseSizeProperty =
            DependencyProperty.RegisterAttached("BaseSize", typeof(double), typeof(FontScaler), new PropertyMetadata(0.0));

        public static double Clamp(double scale) =>
            double.IsNaN(scale) || scale <= 0 ? 1 : Math.Round(Math.Clamp(scale, MinScale, MaxScale), 1);

        /// <summary>Scales <paramref name="root"/> and everything below it, except the subtrees for which <paramref name="skip"/> is true.</summary>
        public static void Apply(DependencyObject root, double scale, Func<DependencyObject, bool>? skip = null)
        {
            if (skip?.Invoke(root) == true) return;
            ApplyOne(root, scale);
            if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D) return;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) Apply(VisualTreeHelper.GetChild(root, i), scale, skip);
        }

        public static void ApplyOne(DependencyObject d, double scale)
        {
            double current;
            Action<double> set;
            string family;
            switch (d)
            {
                case TextBlock t: current = t.FontSize; family = t.FontFamily.Source; set = v => t.FontSize = v; break;
                case Control c: current = c.FontSize; family = c.FontFamily.Source; set = v => c.FontSize = v; break;
                default: return;
            }
            if (family.Contains("MDL2", StringComparison.OrdinalIgnoreCase) || family.Contains("Icons", StringComparison.OrdinalIgnoreCase)) return;

            double basis = (double)d.GetValue(BaseSizeProperty);
            if (basis <= 0)
            {
                if (Math.Abs(scale - 1) < 0.001) return;   // nothing to scale and nothing to restore
                basis = current;
                d.SetValue(BaseSizeProperty, basis);
            }
            double target = Math.Round(basis * scale, 1);
            if (Math.Abs(current - target) > 0.01) set(target);
        }

        public static bool IsInside(DependencyObject? d, DependencyObject ancestor)
        {
            for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (ReferenceEquals(d, ancestor)) return true;
            return false;
        }
    }
}
