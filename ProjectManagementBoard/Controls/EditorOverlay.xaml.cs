using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ProjectManagementBoard.Models;

namespace ProjectManagementBoard.Controls
{
    public enum FieldKind { Text, Color, Path, Choice }

    public class EditField : ObservableObject
    {
        private string _value = "";

        public string Label { get; init; } = "";
        public FieldKind Kind { get; init; }

        /// <summary>Color fields: the swatches offered above the hex box.</summary>
        public IReadOnlyList<string> Swatches { get; init; } = Array.Empty<string>();

        /// <summary>Called on every change, e.g. to preview a color live before saving.</summary>
        public Action<string>? Preview { get; init; }

        /// <summary>Choice fields: the options shown as select buttons (the field's Value is the picked one).</summary>
        public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

        /// <summary>Path fields: shows a 찾기 button that returns the picked path (null = cancelled).</summary>
        public Func<string, string?>? Browse { get; init; }

        public string Value
        {
            get => _value;
            set
            {
                if (!SetField(ref _value, value ?? "")) return;
                Preview?.Invoke(_value);
            }
        }
    }

    /// <param name="Save">Reads the edited <see cref="EditField.Value"/>s back into the model.</param>
    /// <param name="Cancel">Optional; runs when the popup is dismissed without saving.</param>
    public record EditRequest(string Heading, IReadOnlyList<EditField> Fields, int FocusIndex,
                              Action Save, Action? Cancel = null)
    {
        /// <summary>Base color (#RRGGBB) for the dialog background; null = default navy.</summary>
        public string? Tint { get; init; }

        /// <summary>Re-tint the dialog live from the (first) color field's value, e.g. while picking a card color.</summary>
        public bool TintFollowsValue { get; init; }

        /// <summary>Optional text under the heading (for confirm-style dialogs with no fields).</summary>
        public string? Message { get; init; }

        /// <summary>Dialog width in px (default 400); capped to the window so it never overflows.</summary>
        public double Width { get; init; } = 400;

        public string SaveText { get; init; } = "저장";

        /// <summary>When set, shows a third button (<see cref="DiscardText"/>) that runs this and closes.</summary>
        public Action? Discard { get; init; }

        public string DiscardText { get; init; } = "저장 안 함";
    }

    /// <summary>In-window modal popup (dimmed backdrop + animated dialog) used for all text/color editing.</summary>
    public partial class EditorOverlay : UserControl
    {
        private const string DefaultTint = "#243F70";

        private EditRequest? _request;
        private EditField? _tintSource;
        private readonly GradientStop _tintTop = new() { Offset = 0 };
        private readonly GradientStop _tintBottom = new() { Offset = 1 };

        public event EventHandler? Closed;

        public EditorOverlay()
        {
            InitializeComponent();
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(_tintTop);
            brush.GradientStops.Add(_tintBottom);
            DialogShadowLayer.Background = brush;
            DialogBody.Background = brush;
            ApplyTint(DefaultTint, animate: false);
        }

        public bool IsOpen => _request != null;

        public void Show(EditRequest request)
        {
            if (IsOpen) Close(save: false);
            _request = request;
            HeadingText.Text = request.Heading;
            DialogRoot.Width = request.Width;
            // The overlay is still collapsed here (ActualWidth == 0), so measure the window instead.
            double hostWidth = Window.GetWindow(this)?.ActualWidth ?? SystemParameters.PrimaryScreenWidth;
            DialogRoot.MaxWidth = Math.Max(400, hostWidth - 40);
            HintText.Text = "Enter 저장 · Esc 취소";
            FieldList.ItemsSource = request.Fields;
            MessageText.Text = request.Message ?? "";
            MessageText.Visibility = request.Message == null ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.Content = request.SaveText;
            DiscardButton.Content = request.DiscardText;
            DiscardButton.Visibility = request.Discard == null ? Visibility.Collapsed : Visibility.Visible;
            HintText.Visibility = request.Fields.Count == 0 ? Visibility.Collapsed : Visibility.Visible; // confirm: buttons only
            Visibility = Visibility.Visible;

            ApplyTint(request.Tint ?? DefaultTint, animate: false);
            if (request.TintFollowsValue && request.Fields.FirstOrDefault(f => f.Kind == FieldKind.Color) is { } colorField)
            {
                _tintSource = colorField;
                colorField.PropertyChanged += OnTintSourceChanged;
            }

            var pop = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(320);
            Animate(Backdrop, OpacityProperty, 0, 1, 180, null);
            Animate(DialogRoot, OpacityProperty, 0, 1, 180, null);
            Animate(DialogScale, ScaleTransform.ScaleXProperty, 0.92, 1, dur.TotalMilliseconds, pop);
            Animate(DialogScale, ScaleTransform.ScaleYProperty, 0.92, 1, dur.TotalMilliseconds, pop);
            Animate(DialogShift, TranslateTransform.YProperty, 14, 0, dur.TotalMilliseconds, pop);

            if (request.Fields.Count == 0) return; // confirm-style dialog: nothing to focus

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (FieldList.ItemContainerGenerator.ContainerFromIndex(request.FocusIndex) is not DependencyObject row) return;
                if (row.FindDescendant<TextBox>() is not { } box) return;
                box.Focus();
                box.SelectAll();
            });
        }

        private void Close(bool save) => Close(save ? r => r.Save() : r => r.Cancel?.Invoke());

        private void Close(Action<EditRequest> outcome)
        {
            if (_request is not { } request) return;
            _request = null;
            if (_tintSource != null)
            {
                _tintSource.PropertyChanged -= OnTintSourceChanged;
                _tintSource = null;
            }
            outcome(request);

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) =>
            {
                if (!IsOpen) Visibility = Visibility.Collapsed;
            };
            Backdrop.BeginAnimation(OpacityProperty, fade);
            Animate(DialogRoot, OpacityProperty, null, 0, 140, ease);
            Animate(DialogScale, ScaleTransform.ScaleXProperty, null, 0.95, 160, ease);
            Animate(DialogScale, ScaleTransform.ScaleYProperty, null, 0.95, 160, ease);

            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void OnTintSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditField.Value) && sender is EditField field)
                ApplyTint(field.Value, animate: true);
        }

        /// <summary>Same gradient recipe as the cards (lighter top, darker bottom). Invalid colors are ignored.</summary>
        private void ApplyTint(string hex, bool animate)
        {
            if (!ColorUtil.TryParse(hex, out var baseColor)) return;
            var top = ColorUtil.Shade(baseColor, 0.08);
            var bottom = ColorUtil.Shade(baseColor, -0.06);
            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(300);
                _tintTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(top, dur));
                _tintBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(bottom, dur));
            }
            else
            {
                _tintTop.BeginAnimation(GradientStop.ColorProperty, null);
                _tintBottom.BeginAnimation(GradientStop.ColorProperty, null);
                _tintTop.Color = top;
                _tintBottom.Color = bottom;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (!IsOpen) return;
            if (e.Key == Key.Enter) { Close(save: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(save: false); e.Handled = true; }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => Close(save: true);

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close(save: false);

        private void Discard_Click(object sender, RoutedEventArgs e) => Close(r => r.Discard?.Invoke());

        private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close(save: false);

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not EditField { Browse: { } browse } field) return;
            if (browse(field.Value) is { } picked) field.Value = picked;
        }

        private void Choice_Click(object sender, RoutedEventArgs e)
        {
            var button = (FrameworkElement)sender;
            var field = button.SelfAndAncestors().OfType<ItemsControl>().First().DataContext as EditField;
            if (field != null && button.DataContext is string option) field.Value = option;
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            var button = (FrameworkElement)sender;
            var field = button.SelfAndAncestors().OfType<ItemsControl>().First().DataContext as EditField;
            if (field != null && button.DataContext is string color) field.Value = color;
        }

        private static void Animate(IAnimatable target, DependencyProperty property, double? from, double to,
                                    double ms, IEasingFunction? ease) =>
            target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                From = from,
                EasingFunction = ease,
            });
    }
}
