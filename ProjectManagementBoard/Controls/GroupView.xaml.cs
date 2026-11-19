using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ProjectManagementBoard.Models;

namespace ProjectManagementBoard.Controls
{
    /// <summary>
    /// A group box behind the cards. Same motion model as <see cref="ToolCardView"/> (pointer writes
    /// the target geometry, a render loop eases toward it). Dragging the header raises <see cref="Moved"/>
    /// with each target delta so the host can move the member cards along.
    /// </summary>
    public partial class GroupView : UserControl
    {
        public const double MinGroupWidth = 320;
        public const double MinGroupHeight = 220;

        private const double DragThreshold = 4;
        private const double FollowStiffness = 20;

        public GroupModel Model { get; }

        public event EventHandler? DeleteRequested;

        /// <summary>The save button was pressed: the host writes the group and its contents to a *.pmgr file.</summary>
        public event EventHandler? SaveRequested;
        public event EventHandler<EditRequest>? EditRequested;

        /// <summary>A header drag is starting (host snapshots the member cards).</summary>
        public event EventHandler? DragStarted;

        /// <summary>The group's target position moved by this delta during a drag.</summary>
        public event EventHandler<Vector>? Moved;

        /// <summary>A drag or resize finished (membership may have changed).</summary>
        public event EventHandler? Changed;

        private double _x, _y, _w, _h;
        private bool _ticking, _pressed, _dragging, _resizing;
        private TimeSpan _lastFrame;
        private Point _pressPoint;
        private EditableText? _pressTarget;
        private Vector _dragOffset;
        private Point _resizeStart;
        private Size _resizeStartSize;

        public GroupView(GroupModel model)
        {
            Model = model;
            DataContext = model;
            InitializeComponent();

            model.Width = Math.Max(model.Width, MinGroupWidth);
            model.Height = Math.Max(model.Height, MinGroupHeight);
            _x = model.X; _y = model.Y; _w = model.Width; _h = model.Height;
            ApplyFrame();
            ApplyColor(animate: false);

            model.PropertyChanged += OnModelChanged;
            Unloaded += (_, _) => StopTicking();
        }

        private Canvas? Host => Parent as Canvas;

        public void Detach()
        {
            Model.PropertyChanged -= OnModelChanged;
            StopTicking();
        }

        public void SetMemberCount(int cards, int groups)
        {
            var parts = new List<string>();
            if (cards > 0) parts.Add($"관리 툴 {cards}개");
            if (groups > 0) parts.Add($"그룹 {groups}개");
            CountText.Text = string.Join(" · ", parts);
        }

        /// <summary>Emphasizes the box while a card is dragged over it.</summary>
        public void SetHighlight(bool on)
        {
            var dur = TimeSpan.FromMilliseconds(160);
            Fill.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0.6, dur));
            Outline.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0.7, dur));
            Outline.BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(on ? 3 : 2, dur));
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(GroupModel.X) or nameof(GroupModel.Y) or nameof(GroupModel.Width) or nameof(GroupModel.Height):
                    EnsureTicking();
                    break;
                case nameof(GroupModel.Color):
                    ApplyColor(animate: true);
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
            if (now == _lastFrame) return;
            double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 0.05);
            _lastFrame = now;

            double k = 1 - Math.Exp(-FollowStiffness * dt);
            _x += (Model.X - _x) * k;
            _y += (Model.Y - _y) * k;
            _w += (Model.Width - _w) * k;
            _h += (Model.Height - _h) * k;

            if (!_dragging && !_resizing &&
                Math.Abs(Model.X - _x) < 0.2 && Math.Abs(Model.Y - _y) < 0.2 &&
                Math.Abs(Model.Width - _w) < 0.2 && Math.Abs(Model.Height - _h) < 0.2)
            {
                _x = Model.X; _y = Model.Y; _w = Model.Width; _h = Model.Height;
                StopTicking();
            }
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            Canvas.SetLeft(this, _x);
            Canvas.SetTop(this, _y);
            Width = _w;
            Height = _h;
        }

        #endregion

        #region Header press: drag (with members) or click title to rename

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            var source = e.OriginalSource as DependencyObject;
            if (Host is not { } host) return;
            var chain = source.SelfAndAncestors(this).ToList();
            if (!chain.Contains(Header) || chain.Any(x => x is ButtonBase)) return;

            host.Focus();
            _pressPoint = e.GetPosition(host);
            _dragOffset = new Vector(_pressPoint.X - Model.X, _pressPoint.Y - Model.Y);
            _pressTarget = chain.OfType<EditableText>().FirstOrDefault();
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
                SetHighlight(true);
                DragStarted?.Invoke(this, EventArgs.Empty);
                EnsureTicking();
            }
            var delta = new Vector(BoardSnap.Apply(p.X - _dragOffset.X) - Model.X, BoardSnap.Apply(p.Y - _dragOffset.Y) - Model.Y);
            Model.X += delta.X;
            Model.Y += delta.Y;
            Moved?.Invoke(this, delta);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (!_pressed) return;
            bool click = !_dragging && _pressTarget != null;
            EndPress();
            if (click) EditTitle();
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
            if (!wasDragging) return;
            SetHighlight(false);
            Changed?.Invoke(this, EventArgs.Empty);
        }

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
            EnsureTicking();
            e.Handled = true;
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizing || Host is not { } host) return;
            var d = e.GetPosition(host) - _resizeStart;
            Model.Width = Math.Max(MinGroupWidth, BoardSnap.Apply(_resizeStartSize.Width + d.X));
            Model.Height = Math.Max(MinGroupHeight, BoardSnap.Apply(_resizeStartSize.Height + d.Y));
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndResize();

        private void ResizeGrip_LostMouseCapture(object sender, MouseEventArgs e) => EndResize();

        private void EndResize()
        {
            if (!_resizing) return;
            _resizing = false;
            if (ResizeGrip.IsMouseCaptured) ResizeGrip.ReleaseMouseCapture();
            EnsureTicking();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ResizeGrip_MouseEnter(object sender, MouseEventArgs e) => Fade(ResizeGrip, 0.9);

        private void ResizeGrip_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_resizing) Fade(ResizeGrip, 0.35);
        }

        #endregion

        #region Look & animations

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Fade(GroupActions, 1);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_dragging) Fade(GroupActions, 0);
        }

        private void ApplyColor(bool animate)
        {
            var c = ColorUtil.Parse(Model.Color, GroupModel.DefaultColor);
            var fill = Color.FromArgb(0x30, c.R, c.G, c.B);
            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(300);
                FillBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fill, dur));
                OutlineBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, dur));
            }
            else
            {
                FillBrush.Color = fill;
                OutlineBrush.Color = c;
            }
        }

        public void PlayEnter(TimeSpan delay)
        {
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(380);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = delay });
            GroupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, dur) { BeginTime = delay, EasingFunction = ease });
            GroupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, dur) { BeginTime = delay, EasingFunction = ease });
        }

        public void PlayExit(Action onCompleted)
        {
            IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) => onCompleted();
            GroupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(200)));
            GroupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(200)));
            BeginAnimation(OpacityProperty, fade);
        }

        private static void Fade(UIElement element, double to) =>
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180)));

        #endregion

        #region Editing

        public void EditTitle() => EditRequested?.Invoke(this, ToolEditors.GroupTitle(Model));

        private void GroupColor_Click(object sender, RoutedEventArgs e)
        {
            var original = Model.Color;
            var field = new EditField
            {
                Label = "색상 (HEX)",
                Kind = FieldKind.Color,
                Swatches = ColorUtil.GroupSwatches,
                Value = original,
                Preview = v => { if (ColorUtil.TryParse(v, out var c)) Model.Color = ColorUtil.ToHex(c); },
            };
            EditRequested?.Invoke(this, new EditRequest("그룹 색상", new[] { field }, 0,
                Save: () => Model.Color = ColorUtil.TryParse(field.Value, out var c) ? ColorUtil.ToHex(c) : original,
                Cancel: () => Model.Color = original) { Tint = ToolEditors.GroupPopupTint(Model) });
        }

        private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

        #endregion
    }
}
