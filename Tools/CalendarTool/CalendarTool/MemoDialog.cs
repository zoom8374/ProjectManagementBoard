using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CalendarTool
{
    /// <summary>Dark, rounded memo editor for a single date. Styles come from <paramref name="host"/> (the calendar control).</summary>
    public static class MemoDialog
    {
        /// <summary>Returns the new memo text ("" = delete) or null when cancelled.</summary>
        public static string? Show(FrameworkElement host, string heading, string current, bool canDelete)
        {
            var muted = new SolidColorBrush(Color.FromRgb(0xA9, 0xB3, 0xC7));
            var accent = Color.FromRgb(0x7C, 0x9C, 0xFF);
            var white = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA));
            string? result = null;

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
                Foreground = white,
            };

            var badge = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
                Child = new TextBlock
                {
                    Text = "\uE70F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xBE, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            titles.Children.Add(new TextBlock { Text = "메모", FontSize = 17, FontWeight = FontWeights.SemiBold });
            titles.Children.Add(new TextBlock { Text = heading, FontSize = 12, Foreground = muted, Margin = new Thickness(0, 1, 0, 0) });
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(badge);
            header.Children.Add(titles);

            var box = new TextBox
            {
                Style = (Style)host.FindResource("Field"),
                Text = current, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 13.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
                Height = 190, Margin = new Thickness(0, 16, 0, 0), MaxLength = 1000,
            };

            var hint = new TextBlock { Text = "Ctrl+Enter 저장 · Esc 취소", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x7A, 0x90)), VerticalAlignment = VerticalAlignment.Center };

            var cancel = new Button { Style = (Style)host.FindResource("PillBtn"), Content = "취소", Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.White };
            var save = new Button { Style = (Style)host.FindResource("AccentBtn"), Content = "저장" };
            cancel.Click += (_, _) => win.Close();
            save.Click += (_, _) => { result = box.Text.Trim(); win.Close(); };

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (canDelete)
            {
                var del = new Button { Style = (Style)host.FindResource("DangerBtn"), Content = "삭제", Margin = new Thickness(0, 0, 8, 0) };
                del.Click += (_, _) => { result = ""; win.Close(); };
                right.Children.Add(del);
            }
            right.Children.Add(cancel);
            right.Children.Add(save);

            var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(hint);
            buttons.Children.Add(right);

            var body = new StackPanel { Width = 441 };
            body.Children.Add(header);
            body.Children.Add(box);
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
                box.Focus();
                box.CaretIndex = box.Text.Length;
            };
            win.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { win.Close(); e.Handled = true; }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    result = box.Text.Trim(); win.Close(); e.Handled = true;
                }
            };
            win.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && !box.IsKeyboardFocusWithin) win.DragMove();
            };

            win.ShowDialog();
            return result;
        }
    }
}
