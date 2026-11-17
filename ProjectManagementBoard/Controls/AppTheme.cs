using System.Windows;
using System.Windows.Media;

namespace ProjectManagementBoard.Controls
{
    /// <summary>
    /// Dark-board / light-board theme switch. Everything that sits on the board background uses the brushes in
    /// Themes/OnDark.xaml through DynamicResource (ThemeText, Fg## = text color at that alpha, the accent tints);
    /// <see cref="Apply"/> replaces them at app level, so the whole window re-colors at once. Surfaces that are always
    /// dark (dialogs, card bodies, zoom bar, toast, coach mark) merge their own copy of OnDark.xaml and ignore the switch.
    /// </summary>
    public static class AppTheme
    {
        // Must match the Fg## keys in Themes/OnDark.xaml
        private static readonly byte[] Alphas =
            { 0x0A, 0x14, 0x1A, 0x1C, 0x22, 0x26, 0x30, 0x33, 0x38, 0x40, 0x4D, 0x55, 0x66, 0x99, 0xB3, 0xCC, 0xD0 };

        /// <summary>True while a light board is showing (text is ink, overlays are dark).</summary>
        public static bool IsLight { get; private set; }

        /// <summary>The text color of the current theme.</summary>
        public static Color Foreground => IsLight ? Palette.Ink : Colors.White;

        public static void Apply(bool light)
        {
            IsLight = light;
            var res = Application.Current.Resources;
            var fg = Foreground;

            res["ThemeText"] = Frozen(fg);
            foreach (byte a in Alphas)
                res[$"Fg{a:X2}"] = Frozen(Color.FromArgb(a, fg.R, fg.G, fg.B));

            res["AccentYellow"] = Frozen(light ? Palette.AmberOnLight : Palette.Yellow);
            res["AccentBrush"] = Frozen(light ? Palette.AmberOnLight : Palette.Yellow);
            res["LevelHighBrush"] = Frozen(light ? Palette.GreenOnLight : Palette.High);
            res["LevelMidBrush"] = Frozen(light ? Palette.AmberOnLight : Palette.Yellow);
            res["LevelLowBrush"] = Frozen(light ? Palette.RedOnLight : Palette.Low);
            res["LabelBrush"] = Frozen(light ? Palette.LabelOnLight : Color.FromRgb(0xCF, 0xE0, 0xFF));
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
