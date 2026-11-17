using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CheckMemoTool.Models;

namespace CheckMemoTool.Views
{
    internal static class Palette
    {
        public static readonly Color High = Color.FromRgb(0xE5, 0x39, 0x35);    // 빨강
        public static readonly Color Medium = Color.FromRgb(0xFB, 0x8C, 0x00);  // 주황
        public static readonly Color Low = Color.FromRgb(0x43, 0xA0, 0x47);     // 녹색

        public static Color? ForPriority(string priority)
        {
            if (priority == Priority.High) return High;
            if (priority == Priority.Medium) return Medium;
            if (priority == Priority.Low) return Low;
            return null;
        }

        public static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    // 항목 행 배경: 중요도 색을 흐리게 (미완료 약 15%, 완료는 그 절반) — F-21
    public class PriorityRowBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string priority = values.Length > 0 ? values[0] as string : null;
            bool isDone = values.Length > 1 && values[1] is bool && (bool)values[1];
            Color? c = Palette.ForPriority(priority);
            if (!c.HasValue) return Brushes.Transparent;
            byte alpha = isDone ? (byte)0x13 : (byte)0x26;
            return Palette.Frozen(Color.FromArgb(alpha, c.Value.R, c.Value.G, c.Value.B));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 중요도 동그라미 채움색 ('없음'은 투명 = 빈 원)
    public class PriorityDotBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Color? c = Palette.ForPriority(value as string);
            if (!c.HasValue) return Brushes.Transparent;
            return Palette.Frozen(c.Value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 중요도 동그라미 테두리색 ('없음'은 회색 테두리)
    public class PriorityDotStrokeConverter : IValueConverter
    {
        private static readonly SolidColorBrush Empty = Palette.Frozen(Color.FromRgb(0x9A, 0x9A, 0x9A));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Color? c = Palette.ForPriority(value as string);
            if (!c.HasValue) return Empty;
            return Palette.Frozen(c.Value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // 글자 크기(pt) + 오프셋(pt) → WPF 단위(1/96인치)
    public class PointToDipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double pt = value is double ? (double)value : FontLimits.DefaultFontSize;
            double offset = 0;
            if (parameter != null) double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
            double result = (pt + offset) * 96.0 / 72.0;
            return Math.Max(8.0, result);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool && (bool)value;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
