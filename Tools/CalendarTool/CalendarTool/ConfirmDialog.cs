using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CalendarTool
{
    /// <summary>Dark, rounded replacement for MessageBox (confirm / cancel). Styles come from <paramref name="host"/> (the calendar control); the dialog is owned by the window that hosts it.</summary>
    public static class ConfirmDialog
    {
        public static bool Show(FrameworkElement host, string title, string message, string confirmText, string cancelText = "취소", bool danger = true, string? glyph = null)
        {
            var muted = new SolidColorBrush(Color.FromRgb(0xA9, 0xB3, 0xC7));
            var red = danger ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0x7C, 0x9C, 0xFF);   // badge tone: red for destructive, accent otherwise

            var owner = Window.GetWindow(host);
            var win = new Window
            {
                Owner = owner,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Topmost = owner?.Topmost ?? false,
                FontFamily = TextElement.GetFontFamily(host),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA)),
            };

            // header: warning badge + title
            var badge = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(0x33, red.R, red.G, red.B)),
                Child = new TextBlock
                {
                    Text = glyph ?? "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                    Foreground = new SolidColorBrush(danger ? Color.FromRgb(0xFF, 0x7A, 0x7F) : Color.FromRgb(0xA8, 0xBE, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(badge);
            header.Children.Add(new TextBlock
            {
                Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            });

            // buttons
            bool result = false;
            var cancel = new Button { Style = (Style)host.FindResource("PillBtn"), Content = cancelText, Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.White };
            var ok = new Button { Style = (Style)host.FindResource(danger ? "DangerBtn" : "AccentBtn"), Content = confirmText };
            cancel.Click += (_, _) => { result = false; win.Close(); };
            ok.Click += (_, _) => { result = true; win.Close(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            var body = new StackPanel { Width = 340 };
            body.Children.Add(header);
            body.Children.Add(new TextBlock { Text = message, Foreground = muted, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = 20 });
            body.Children.Add(buttons);

            var scale = new ScaleTransform(0.94, 0.94);
            var card = new Border
            {
                Margin = new Thickness(18),
                Padding = new Thickness(22),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x21, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.6 },
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Opacity = 0,
                Child = body,
            };
            win.Content = card;

            win.Loaded += (_, _) =>
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                var s = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, s);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, s);
            };
            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { result = false; win.Close(); }
                else if (e.Key == Key.Enter) { result = true; win.Close(); }
            };
            win.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            win.ShowDialog();
            return result;
        }
    }
}
