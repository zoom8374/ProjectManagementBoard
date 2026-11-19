using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ProjectManagementBoard.Contracts;
using ProjectManagementBoard.Models;
using ProjectManagementBoard.Services;

namespace ProjectManagementBoard.Controls
{
    /// <summary>
    /// A draggable, resizable 알고리즘 card: a title above an empty rounded rectangle. No task/status
    /// content — this app's cards carry only a name, a color and a shape.
    ///
    /// Motion model: the pointer only ever writes the *target* geometry into <see cref="ToolModel"/>.
    /// Every frame (CompositionTarget.Rendering) the displayed geometry eases exponentially toward
    /// that target, and the card tilts with its horizontal velocity while dragged. The render loop
    /// unsubscribes itself once everything has settled.
    ///
    /// Click vs. drag: a press anywhere (except buttons / resize grip) arms a drag that only starts
    /// after the pointer moves <see cref="DragThreshold"/> px. A press released before that on the
    /// title opens the edit popup instead.
    /// </summary>
    public partial class ToolCardView : UserControl
    {
        public const double MinCardWidth = 240;

        /// <summary>Fixed chrome (header, thumbnails, footer) plus one skill row; grows with the skill list.</summary>
        private const double MinCardHeight = 160;

        private const double DragThreshold = 4;
        private const double FollowStiffness = 20;   // higher = snappier follow
        private const double TiltStiffness = 10;
        private const double TiltPerVelocity = 0.006; // degrees per px/s
        private const double MaxTilt = 6;

        public ToolModel Model { get; }

        /// <summary>Raised when the user asks to delete this card.</summary>
        public event EventHandler? DeleteRequested;

        /// <summary>The copy button was pressed (the host keeps the copied 알고리즘 for Ctrl+V).</summary>
        public event EventHandler? CopyRequested;

        /// <summary>The save button was pressed: the host writes this tool to its own file (state captured first).</summary>
        public event EventHandler? SaveToolRequested;

        /// <summary>Raised on any press inside the card, so the host can bring it to front.</summary>
        public event EventHandler? Activated;

        /// <summary>Raised when a text field should be edited in the popup.</summary>
        public event EventHandler<EditRequest>? EditRequested;

        /// <summary>The card's target position changed during a drag (host highlights the group under it).</summary>
        public event EventHandler? DragMoved;

        /// <summary>A loaded thumbnail was clicked: the host shows it large (argument = the picture's full path).</summary>

        /// <summary>A drag or resize finished (group membership may have changed).</summary>
        public event EventHandler? Released;

        private readonly GradientStop _cardTop = new() { Offset = 0 };
        private readonly GradientStop _cardBottom = new() { Offset = 1 };

        private double _x, _y, _w, _h, _tilt;
        private bool _ticking, _pressed, _dragging, _resizing;
        private TimeSpan _lastFrame;
        private Point _pressPoint;
        private EditableText? _pressTarget;
        private Vector _dragOffset;
        private Point _resizeStart;
        private Size _resizeStartSize;

        public ToolCardView(ToolModel model, ToolCatalog catalog)
        {
            Model = model;
            DataContext = model;
            InitializeComponent();
            DockTool(catalog);

            var cardBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            cardBrush.GradientStops.Add(_cardTop);
            cardBrush.GradientStops.Add(_cardBottom);
            CardShadowLayer.Background = cardBrush;
            CardBody.Background = cardBrush;
            ApplyCardColor(animate: false);

            model.Width = Math.Max(model.Width, MinCardWidth);
            model.Height = Math.Max(model.Height, MinCardHeight);

            _x = model.X; _y = model.Y; _w = model.Width; _h = model.Height;
            ApplyFrame();

            model.PropertyChanged += OnModelChanged;
            Unloaded += (_, _) => StopTicking();
        }

        private IBoardToolInstance? _instance;

        /// <summary>Creates the tool named by <see cref="ToolModel.ToolId"/> and docks its view; a failing tool never takes the board down.</summary>
        private void DockTool(ToolCatalog catalog)
        {
            // an empty card has nothing of its own to save
            SaveToolButton.Visibility = Model.ToolId.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            SaveToolTipText.Text = $"관리 툴 개별 저장 (*{catalog.FileExtensionFor(Model.ToolId)})";
            if (Model.ToolId.Length == 0) return;
            var tool = catalog.Find(Model.ToolId);
            if (tool == null)
            {
                PlaceholderText.Text = $"관리 툴을 찾을 수 없어요 ({Model.ToolId})\nPlugins 폴더를 확인하세요";
                return; // Model.State stays untouched so nothing is lost when the tool comes back
            }
            try
            {
                _instance = tool.CreateInstance();
                if (Model.State.Length > 0) _instance.LoadState(Model.State);
                if (_instance is IBoardToolResizeRequest resizable) resizable.ResizeRequested += OnToolResizeRequested;
                ToolHost.Child = _instance.View;
                Placeholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _instance = null;
                ToolHost.Child = null;
                PlaceholderText.Text = $"관리 툴을 불러오지 못했어요\n{ex.GetBaseException().Message}";
            }
        }

        /// <summary>The docked tool asked for a bigger/smaller card (e.g. CalendarTool opening its Schedule panel); eased like any resize.</summary>
        private void OnToolResizeRequested(object? sender, Vector delta)
        {
            Model.Width = Math.Max(MinCardWidth, Model.Width + delta.X);
            Model.Height = Math.Max(MinCardHeight, Model.Height + delta.Y);
            Released?.Invoke(this, EventArgs.Empty);   // group membership / counts may change with the new size
        }

        /// <summary>Copies the tool's current data into <see cref="ToolModel.State"/> (before copy, delete, save).</summary>
        public void CaptureState()
        {
            if (_instance == null) return;
            try { Model.State = _instance.SaveState() ?? ""; }
            catch (Exception) { /* keep the previous state */ }
        }

        private Canvas? Host => Parent as Canvas;

        /// <summary>Stops listening to the model; call when the card leaves the board.</summary>
        public void Detach()
        {
            CaptureState();
            if (_instance is IBoardToolResizeRequest resizable) resizable.ResizeRequested -= OnToolResizeRequested;
            (_instance as IDisposable)?.Dispose();
            _instance = null;
            Model.PropertyChanged -= OnModelChanged;
            StopTicking();
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ToolModel.X) or nameof(ToolModel.Y) or nameof(ToolModel.Width) or nameof(ToolModel.Height):
                    EnsureTicking();
                    break;
                case nameof(ToolModel.CardColor):
                    ApplyCardColor(animate: true);
                    break;
            }
        }

        #region Render loop

        private void EnsureTicking()
        {
            if (_ticking) return;
            _ticking = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopTicking()
        {
            if (!_ticking) return;
            _ticking = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _lastFrame) return; // Rendering can fire more than once per frame
            double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 0.05);
            _lastFrame = now;

            double k = 1 - Math.Exp(-FollowStiffness * dt);
            double prevX = _x;
            _x += (Model.X - _x) * k;
            _y += (Model.Y - _y) * k;
            _w += (Model.Width - _w) * k;
            _h += (Model.Height - _h) * k;

            double vx = (_x - prevX) / dt;
            double tiltTarget = _dragging ? Math.Clamp(vx * TiltPerVelocity, -MaxTilt, MaxTilt) : 0;
            _tilt += (tiltTarget - _tilt) * (1 - Math.Exp(-TiltStiffness * dt));

            if (!_dragging && !_resizing && IsSettled())
            {
                _x = Model.X; _y = Model.Y; _w = Model.Width; _h = Model.Height; _tilt = 0;
                StopTicking();
            }
            ApplyFrame();
        }

        private bool IsSettled() =>
            Math.Abs(Model.X - _x) < 0.2 && Math.Abs(Model.Y - _y) < 0.2 &&
            Math.Abs(Model.Width - _w) < 0.2 && Math.Abs(Model.Height - _h) < 0.2 &&
            Math.Abs(_tilt) < 0.05;

        private void ApplyFrame()
        {
            Canvas.SetLeft(this, _x);
            Canvas.SetTop(this, _y);
            Width = _w;
            Height = _h;
            CardRotate.Angle = _tilt;
        }

        #endregion

        #region Press: drag to move, or click to edit

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            Activated?.Invoke(this, EventArgs.Empty);

            var source = e.OriginalSource as DependencyObject;
            if (Host is not { } host || IsInteractive(source)) return;

            host.Focus();
            _pressPoint = e.GetPosition(host);
            _dragOffset = new Vector(_pressPoint.X - Model.X, _pressPoint.Y - Model.Y);
            _pressTarget = source.SelfAndAncestors(this).OfType<EditableText>().FirstOrDefault();
                        _pressed = true;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (!_pressed || Host is not { } host) return;

            var p = e.GetPosition(host);
            if (!_dragging)
            {
                if ((p - _pressPoint).Length < DragThreshold) return;
                _dragging = true;
                UpdateElevation();
                EnsureTicking();
            }
            Model.X = BoardSnap.Apply(p.X - _dragOffset.X);
            Model.Y = BoardSnap.Apply(p.Y - _dragOffset.Y);
            DragMoved?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (!_pressed) return;
            bool click = !_dragging;
            var clickTarget = click ? _pressTarget : null;
            EndPress();
            if (clickTarget != null) BeginEdit(clickTarget);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_pressed) EndPress();
        }

        private void EndPress()
        {
            bool wasDragging = _dragging;
            _pressed = _dragging = false;
            _pressTarget = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (wasDragging)
            {
                UpdateElevation();
                EnsureTicking(); // let the tilt settle back to 0
                Released?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Presses on buttons and the resize grip shouldn't arm a drag.</summary>
        private bool IsInteractive(DependencyObject? d) =>
            d.SelfAndAncestors(this).Any(x => x is ButtonBase || x is ToolContentHost || x == ResizeGrip);

        #endregion

        #region Resize

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Host is not { } host) return;
            host.Focus();
            _resizing = true;
            _resizeStart = e.GetPosition(host);
            _resizeStartSize = new Size(Model.Width, Model.Height);
            ResizeGrip.CaptureMouse();
            UpdateElevation();
            EnsureTicking();
            e.Handled = true;
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizing || Host is not { } host) return;
            var d = e.GetPosition(host) - _resizeStart;
            Model.Width = Math.Max(MinCardWidth, BoardSnap.Apply(_resizeStartSize.Width + d.X));
            Model.Height = Math.Max(MinCardHeight, BoardSnap.Apply(_resizeStartSize.Height + d.Y));
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndResize();

        private void ResizeGrip_LostMouseCapture(object sender, MouseEventArgs e) => EndResize();

        private void EndResize()
        {
            if (!_resizing) return;
            _resizing = false;
            if (ResizeGrip.IsMouseCaptured) ResizeGrip.ReleaseMouseCapture();
            UpdateElevation();
            EnsureTicking();
            Released?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Hover / elevation / enter-exit animations

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Fade(CardActions, 1);
            Fade(ResizeGrip, 0.85);
            UpdateElevation();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging || _resizing) return;
            Fade(CardActions, 0);
            Fade(ResizeGrip, 0);
            UpdateElevation();
        }

        /// <summary>Rest → hover → lifted (dragging) shadow and scale.</summary>
        private void UpdateElevation()
        {
            int level = _dragging ? 2 : (IsMouseOver || _resizing) ? 1 : 0;
            double[] scale = { 1, 1, 1.035 }, blur = { 18, 26, 44 }, depth = { 5, 8, 16 }, opacity = { 0.4, 0.5, 0.6 }; // rest / hover / lifted (see the shadow tiers in App.xaml)

            IEasingFunction ease = level == 2
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(level == 2 ? 160 : 320);

            Animate(CardScale, ScaleTransform.ScaleXProperty, scale[level], dur, ease);
            Animate(CardScale, ScaleTransform.ScaleYProperty, scale[level], dur, ease);
            Animate(CardShadow, DropShadowEffect.BlurRadiusProperty, blur[level], dur, ease);
            Animate(CardShadow, DropShadowEffect.ShadowDepthProperty, depth[level], dur, ease);
            Animate(CardShadow, DropShadowEffect.OpacityProperty, opacity[level], dur, ease);
        }

        public void PlayEnter(TimeSpan delay)
        {
            var pop = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(420);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = delay });
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.82, 1, dur) { BeginTime = delay, EasingFunction = pop });
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.82, 1, dur) { BeginTime = delay, EasingFunction = pop });
        }

        public void PlayExit(Action onCompleted)
        {
            IsHitTestVisible = false;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(200);
            var fade = new DoubleAnimation(0, dur) { EasingFunction = ease };
            fade.Completed += (_, _) => onCompleted();
            Animate(CardScale, ScaleTransform.ScaleXProperty, 0.88, dur, ease);
            Animate(CardScale, ScaleTransform.ScaleYProperty, 0.88, dur, ease);
            BeginAnimation(OpacityProperty, fade);
        }

        private static void Fade(UIElement element, double to) =>
            Animate(element, OpacityProperty, to, TimeSpan.FromMilliseconds(180), new QuadraticEase());

        private static void Animate(IAnimatable target, DependencyProperty property, double to,
                                    TimeSpan duration, IEasingFunction ease) =>
            target.BeginAnimation(property, new DoubleAnimation(to, duration) { EasingFunction = ease });

        #endregion

        #region Editing (popup)

        /// <summary>Every popup opened from a card is tinted with that card's color.</summary>
        private void RaiseEdit(EditRequest request) =>
            EditRequested?.Invoke(this, request with { Tint = Model.CardColor });

        private void BeginEdit(EditableText target)
        {
            switch (target.Field)
            {
                case EditableField.Title: BeginInfoEdit(); break;
            }
        }

        /// <summary>Which popup a press inside the card body opens: the pressed skill row or the completion footer.</summary>
        public void BeginInfoEdit() => RaiseEdit(ToolEditors.ToolInfo(Model));

        /// <summary>Short scale pulse used when flying to this 알고리즘 on the board.</summary>
        public void Flash()
        {
            var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)),
                new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }));
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        #endregion

        #region Card actions

        private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

        private void SaveTool_Click(object sender, RoutedEventArgs e)
        {
            CaptureState();
            SaveToolRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CardColor_Click(object sender, RoutedEventArgs e)
        {
            var original = Model.CardColor;
            var field = new EditField
            {
                Label = "색상 (HEX)",
                Kind = FieldKind.Color,
                Swatches = ColorUtil.CardSwatches,
                Value = original,
                Preview = v => { if (ColorUtil.TryParse(v, out var c)) Model.CardColor = ColorUtil.ToHex(c); },
            };
            RaiseEdit(new EditRequest("카드 색상", new[] { field }, 0,
                Save: () => Model.CardColor = ColorUtil.TryParse(field.Value, out var c) ? ColorUtil.ToHex(c) : original,
                Cancel: () => Model.CardColor = original) { TintFollowsValue = true });
        }

        /// <summary>Builds the card's vertical gradient from its base color (lighter top, darker bottom).</summary>
        private void ApplyCardColor(bool animate)
        {
            var baseColor = ColorUtil.Parse(Model.CardColor, ToolModel.DefaultCardColor);
            var top = ColorUtil.Shade(baseColor, 0.08);
            var bottom = ColorUtil.Shade(baseColor, -0.06);
            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(300);
                _cardTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(top, dur));
                _cardBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(bottom, dur));
            }
            else
            {
                _cardTop.Color = top;
                _cardBottom.Color = bottom;
            }
        }

        #endregion
    }
}
