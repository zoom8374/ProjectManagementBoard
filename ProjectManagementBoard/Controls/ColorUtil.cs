using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ProjectManagementBoard.Controls
{
    internal static class ColorUtil
    {
        // The color popups show 8 swatches per row (the dialog is 400 wide), so every list is a multiple of 8,
        // grouped by hue family from left to right and top to bottom.

        /// <summary>Card colors: mid/deep tones so the white text and the white border stay readable on any of them.</summary>
        public static readonly string[] CardSwatches =
        {
            // blues & teals
            "#4472C4", "#2B5797", "#1F4E79", "#2E75B6", "#2E8B9A", "#1B7F8C", "#2E9C6E", "#3C8D5A",
            // greens, golds, oranges, reds, pinks
            "#5B8C3A", "#7A8F2B", "#A68A1E", "#D9822B", "#C55A11", "#C0504D", "#A23B3B", "#B5577A",
            // purples & neutrals
            "#8E5BB5", "#6F4AA0", "#5B4B9A", "#7A6A4F", "#5E4B3C", "#3D4A5C", "#4A5568", "#6D6E70",
        };

        /// <summary>Lighter tones: groups are drawn as a translucent fill + outline of this color.</summary>
        public static readonly string[] GroupSwatches =
        {
            // blues, teals, greens
            "#9EC5FF", "#8FA3C8", "#7FB3E8", "#6EC6D9", "#7BD8C8", "#7BD88F", "#A5E07A", "#D4E26B",
            // yellows, oranges, reds, pinks, violets
            "#FFD966", "#FFC773", "#F4B183", "#FF9E9E", "#FF8FA3", "#F2A3D0", "#D9A3F2", "#C9A0FF",
            // indigos, pale neutrals, white
            "#AFA3FF", "#A3B8FF", "#B8D4E0", "#D0D8E0", "#E8D5B7", "#D7B899", "#BFBFBF", "#FFFFFF",
        };

        /// <summary>
        /// Board colors: two rows of dark boards (white text) and a row of light boards (the app switches to its
        /// dark-ink theme automatically for those, see AppTheme).
        /// </summary>
        public static readonly string[] BackgroundSwatches =
        {
            // dark
            "#1F3864", "#152238", "#0F2A3F", "#14342B", "#2D1F3D", "#3A2430", "#3A2A1A", "#1E1E2E",
            "#2B2B2B", "#11151C", "#0B3B3C", "#2A3B1F", "#4A2020", "#1D2B4F", "#2F2547", "#243040",
            // light (soft but clearly tinted, not near-white)
            "#C9DCF2", "#B8DCE0", "#BFE3D0", "#F5E0A3", "#F2D0A8", "#F2C9C9", "#D8C9EE", "#CBD2DA",
        };

        /// <summary>Parses "#RGB", "#RRGGBB" or "RRGGBB" (alpha is dropped).</summary>
        public static bool TryParse(string? text, out Color color)
        {
            color = default;
            var s = text?.Trim() ?? "";
            if (s.Length == 0) return false;
            if (!s.StartsWith('#')) s = "#" + s;
            if (s.Length != 4 && s.Length != 7) return false;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(s);
                color.A = 255;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Color Parse(string? text, string fallback) =>
            TryParse(text, out var c) ? c : (Color)ColorConverter.ConvertFromString(fallback);

        /// <summary>WCAG relative luminance (0 = black, 1 = white).</summary>
        public static double Luminance(Color c)
        {
            static double Linear(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        /// <summary>
        /// True when dark text reads better than white on this background (higher WCAG contrast ratio).
        /// Midnight blues and other dark boards keep the white theme; pastels and whites switch to the ink theme.
        /// </summary>
        public static bool PrefersDarkText(Color background)
        {
            double l = Luminance(background);
            double white = 1.05 / (l + 0.05);
            double ink = (l + 0.05) / (Luminance(Palette.Ink) + 0.05);
            return ink > white;
        }

        /// <summary>Mixes toward white (amount &gt; 0) or black (amount &lt; 0).</summary>
        public static Color Shade(Color c, double amount)
        {
            byte Mix(byte v) => (byte)Math.Round(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount));
            return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        }
    }

    /// <summary>
    /// Select-button colors: each option has its own color (상 green / 중 yellow / 하 red, other options use the
    /// accent orange). ConverterParameter "bg" = pill fill (the option's color when picked, faint white otherwise),
    /// "fg" = text (dark when picked, the option's color otherwise). Values: option, current value.
    /// </summary>
    public class ChoiceBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string option = values.Length > 0 ? values[0] as string ?? "" : "";
            string current = values.Length > 1 ? values[1] as string ?? "" : "";
            bool picked = option.Length > 0 && string.Equals(option, current, StringComparison.OrdinalIgnoreCase);
            var color = option switch
            {
                "상" => Palette.High,
                "중" => Palette.Yellow,
                "하" => Palette.Low,
                _ => Palette.Orange,
            };
            var result = (string?)parameter == "bg"
                ? (picked ? color : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF))
                : (picked ? Palette.Ink : color);
            var brush = new SolidColorBrush(result);
            brush.Freeze();
            return brush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>The app palette for code (keep in sync with the colors at the top of App.xaml).</summary>
    public static class Palette
    {
        public static readonly Color Orange = Color.FromRgb(0xED, 0x7D, 0x31);
        public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD8, 0x66);   // also 중
        public static readonly Color High = Color.FromRgb(0x8E, 0xF0, 0xA8);     // 상
        public static readonly Color Low = Color.FromRgb(0xFF, 0x9C, 0x8A);      // 하
        public static readonly Color Ink = Color.FromRgb(0x12, 0x20, 0x3F);      // dark text on a filled pill / light boards

        // Darker stand-ins for the light accent tints, used on a light board (see AppTheme)
        public static readonly Color AmberOnLight = Color.FromRgb(0xA8, 0x6B, 0x00);
        public static readonly Color GreenOnLight = Color.FromRgb(0x1F, 0x8F, 0x4E);
        public static readonly Color RedOnLight = Color.FromRgb(0xC2, 0x43, 0x2F);
        public static readonly Color LabelOnLight = Color.FromRgb(0x3D, 0x5A, 0x8C);
    }

    /// <summary>True when all bound values are equal strings (case-insensitive).</summary>
    public class EqualsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length >= 2 && values.All(v => v is string) &&
            values.Skip(1).All(v => string.Equals((string)v, (string)values[0], StringComparison.OrdinalIgnoreCase));

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
