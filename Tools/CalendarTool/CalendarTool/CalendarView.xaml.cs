using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CalendarTool
{
    public partial class CalendarView : UserControl
    {
        const int Rows = 8;           // visible weeks
        const double PanelWidth = 310;

        static readonly string[] Palette =
        {
            "#EF5B5B", "#F2994A", "#F2C94C", "#6FCF97", "#27AE8F", "#56CCF2",
            "#4F8EF7", "#7C6CF2", "#BB6BD9", "#EB5C9C", "#8E9AAF", "#A0705B",
        };

        /// <summary>40 swatches: a grey ramp followed by 8 hues x 4 lightness levels.</summary>
        static readonly Color[] FullPalette = BuildPalette();

        /// <summary>Background presets offered by the colour dot in the header (same as the standalone app).</summary>
        static readonly Color[] WindowPresets = new[]
        {
            "#151925", "#0E1014", "#1D1F27", "#262626", "#1A1430", "#2D1F3D",
            "#0E2233", "#1F2A44", "#0F211B", "#14302B", "#2A1520", "#3A2418",
        }.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();

        static Color[] BuildPalette()
        {
            var list = new List<Color>();
            foreach (var l in new[] { 0.96, 0.82, 0.68, 0.54, 0.40, 0.28, 0.17, 0.08 })
                list.Add(Hsl(0, 0, l));
            foreach (var l in new[] { 0.72, 0.60, 0.48, 0.36 })
                foreach (var h in new double[] { 0, 32, 52, 140, 175, 205, 262, 320 })
                    list.Add(Hsl(h, 0.7, l));
            return list.ToArray();
        }

        static Color Hsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - c / 2;
            (double r, double g, double b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x)
                                           : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        static string HexOf(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        static bool TryParseColor(string s, out Color c)
        {
            c = default;
            s = s.Trim();
            if (s.Length > 0 && s[0] != '#') s = "#" + s;
            if (s.Length != 7) return false;
            try { c = (Color)ColorConverter.ConvertFromString(s); return true; }
            catch { return false; }
        }

        static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        static readonly Color AccentColor = Color.FromRgb(0x7C, 0x9C, 0xFF);
        static readonly Color ChangeColor = Color.FromRgb(0x4D, 0xD0, 0xE1);   // "changed cell" marks (server feature, unused here)

        // The standalone app's server viewing mode does not exist here: everything is always editable.
        static bool viewing => false;
        static bool LiveBlocked() => false;
        static void Persist() { }

        sealed class DayCell
        {
            public Border Root = null!;
            public TextBlock Num = null!, MonthTag = null!, Label = null!;
            public Border Hover = null!, Sel = null!, TodayRing = null!;
            public TextBlock Memo = null!;
            public bool HasMemo;
            public SolidColorBrush Fill = null!;
            public ScaleTransform Scale = null!;
            public DateTime Date;
            public string? Sig;
            public Border Band1 = null!, Band2 = null!;
            public SolidColorBrush Fill1 = null!, Fill2 = null!;
            public TextBlock Check1 = null!, Check2 = null!;
            public Border Miss1 = null!, Miss2 = null!;
            public Border Chg = null!;
            public Border Diag = null!;              // two goals split diagonally "/" (earlier top-left, later bottom-right)
            public GradientStop[] DiagStops = null!;
            public Ellipse ChgDot = null!;
            public bool Selected;
        }

        readonly CalendarState data;
        ObservableCollection<Goal> goals = new();
        readonly DayCell[] cells = new DayCell[Rows * 7];

        DateTime startDate;                       // Sunday of the first visible week
        DateTime ViewMonth => startDate.AddDays(13);  // the month being "viewed"

        DateTime? selA, selB;
        bool dragging;
        readonly SolidColorBrush bgBrush;   // calendar background (CalendarState.BgHex), animated by the colour dot

        Sheet Active => data.Sheets[data.ActiveSheet];
        Dictionary<string, Guid> Days => Active.Days;
        Dictionary<string, Guid> Days2 => Active.Days2;
        Dictionary<string, string> Notes => Active.Notes;

        /// <summary>Builds the calendar for <paramref name="state"/> (edited in place; read it back with <see cref="Snapshot"/>).</summary>
        public CalendarView(CalendarState state)
        {
            InitializeComponent();

            data = state.Normalize();
            bgBrush = new SolidColorBrush(TryParseColor(data.BgHex, out var bc) ? bc : Color.FromRgb(0x15, 0x19, 0x25));
            Root.Background = bgBrush;
            BindGoals();
            BuildCells();
            SetPanel(data.PanelOpen, animate: false);
            SetSchedule(data.ScheduleOpen, animate: false);   // a saved card already has the schedule's height
            SizeChanged += (_, _) => FollowCardSize();

            startDate = StartFor(DateTime.Today);
            RefreshCells();
            UpdateSelection();
            BuildTabs();

            SizeChanged += (_, _) => TabHint.Visibility = ActualWidth >= 940 ? Visibility.Visible : Visibility.Collapsed;
            TabScroll.ScrollChanged += (_, _) => UpdateTabScrollButtons();
            Loaded += (_, _) => Transition(0);
        }

        /// <summary>The current state (goal list of the active sheet written back first) for the board's SaveState.</summary>
        public CalendarState Snapshot()
        {
            Save();
            return data;
        }

        // ───────────────────────── schedule sheets (tabs) ─────────────────────────

        sealed class TabVisual
        {
            public Sheet S = null!;
            public Border Root = null!, TopLine = null!;
            public Ellipse Dot = null!;
            public TextBlock Sync = null!;     // cloud glyph: how this sheet relates to the server
            public SolidColorBrush Bg = null!;
            public bool Active;
        }

        readonly Dictionary<Guid, TabVisual> tabVisuals = new();
        Guid? renamingId, dragId;
        bool renameCommitting;
        Point dragOrigin;

        public static readonly DependencyProperty TabOffsetProperty = DependencyProperty.Register(
            nameof(TabOffset), typeof(double), typeof(CalendarView),
            new PropertyMetadata(0.0, (d, e) => ((CalendarView)d).TabScroll.ScrollToHorizontalOffset((double)e.NewValue)));
        public double TabOffset { get => (double)GetValue(TabOffsetProperty); set => SetValue(TabOffsetProperty, value); }

        static Color TabAccent(Sheet s) => s.ColorHex != null && TryParseColor(s.ColorHex, out var c) ? c : AccentColor;

        void BindGoals()
        {
            foreach (var g in goals) g.PropertyChanged -= Goal_PropertyChanged;
            goals.CollectionChanged -= Goals_CollectionChanged;
            goals = new ObservableCollection<Goal>(Active.Goals);
            foreach (var g in goals) g.PropertyChanged += Goal_PropertyChanged;
            goals.CollectionChanged += Goals_CollectionChanged;
            GoalList.ItemsSource = goals;
            EmptyHint.Visibility = goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Re-binds the UI to the active sheet (goals, calendar, tabs).</summary>
        void ReloadActive()
        {
            BindGoals();
            selA = selB = null;
            RefreshCells();
            UpdateSelection();
            BuildTabs();
            Transition(0);
            Save();
        }

        void ActivateSheet(int i)
        {
            if (i < 0 || i >= data.Sheets.Count || i == data.ActiveSheet) return;
            Save();                       // persist the sheet we are leaving
            data.ActiveSheet = i;
            ReloadActive();
        }

        void BuildTabs()
        {
            TabsPanel.Children.Clear();
            tabVisuals.Clear();
            for (int i = 0; i < data.Sheets.Count; i++)
                TabsPanel.Children.Add(MakeTab(data.Sheets[i], i == data.ActiveSheet));
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateTabScrollButtons();
                if (tabVisuals.TryGetValue(Active.Id, out var v)) v.Root.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        static void ApplyTabColor(TabVisual v)
        {
            var accent = TabAccent(v.S);
            v.TopLine.Background = new SolidColorBrush(accent);
            v.Dot.Fill = v.S.ColorHex != null ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(0x6F, 0x7A, 0x90));
            v.Root.BorderBrush = v.Active ? new SolidColorBrush(Color.FromArgb(0x8C, accent.R, accent.G, accent.B)) : Brushes.Transparent;
            v.Bg.BeginAnimation(SolidColorBrush.ColorProperty, null);
            v.Bg.Color = v.Active ? Color.FromArgb(0x2E, accent.R, accent.G, accent.B) : Color.FromArgb(0, 255, 255, 255);
        }

        Border MakeTab(Sheet s, bool active)
        {
            var v = new TabVisual { S = s, Active = active };
            v.Bg = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            v.TopLine = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Visibility = active ? Visibility.Visible : Visibility.Collapsed };
            v.Dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

            v.Sync = new TextBlock { Text = "\uE753", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 14, 8) };
            row.Children.Add(v.Dot);
            if (renamingId == s.Id)
            {
                var tb = new TextBox
                {
                    Style = (Style)FindResource("Field"), Text = s.Name, Width = 130, MaxLength = 24,
                    Padding = new Thickness(6, 2, 6, 2), FontSize = 13,
                };
                tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
                tb.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) { CommitRename(tb.Text); e.Handled = true; }
                    else if (e.Key == Key.Escape) { CommitRename(null); e.Handled = true; }
                };
                tb.LostFocus += (_, _) => CommitRename(tb.Text);
                row.Children.Add(tb);
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = s.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xA9, 0xB3, 0xC7)),
                });
            }

            row.Children.Add(v.Sync);
            var g = new Grid();
            g.Children.Add(row);
            g.Children.Add(v.TopLine);
            v.Root = new Border
            {
                Child = g, Background = v.Bg, CornerRadius = new CornerRadius(0, 0, 9, 9),
                BorderThickness = new Thickness(1, 0, 1, 1), Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand, ToolTip = s.Name,
            };
            ApplyTabColor(v);
            tabVisuals[s.Id] = v;

            if (active)
            {   // fade the highlight in
                var target = v.Bg.Color;
                v.Bg.Color = Color.FromArgb(0, target.R, target.G, target.B);
                v.Bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
            }
            else
            {
                v.Root.MouseEnter += (_, _) => v.Bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x18, 255, 255, 255), TimeSpan.FromMilliseconds(110)));
                v.Root.MouseLeave += (_, _) => v.Bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0, 255, 255, 255), TimeSpan.FromMilliseconds(180)));
            }

            v.Root.MouseLeftButtonDown += (_, e) =>
            {
                if (renamingId != null) return;
                dragId = s.Id;
                dragOrigin = e.GetPosition(null);
                if (e.ClickCount == 2) { StartRename(s.Id); e.Handled = true; return; }
                ActivateSheet(data.Sheets.IndexOf(s));
            };
            v.Root.MouseRightButtonDown += (_, _) => { if (renamingId == null) ActivateSheet(data.Sheets.IndexOf(s)); };
            v.Root.MouseRightButtonUp += (_, e) => { if (renamingId == null) { ShowTabMenu(s); e.Handled = true; } };
            return v.Root;
        }

        void StartRename(Guid id)
        {
            if (viewing) return;
            int i = data.Sheets.FindIndex(x => x.Id == id);
            if (i < 0) return;
            if (i != data.ActiveSheet)
            {
                Save();
                data.ActiveSheet = i;
                BindGoals();
                selA = selB = null;
                RefreshCells();
                UpdateSelection();
                Transition(0);
            }
            renamingId = id;
            BuildTabs();
        }

        void CommitRename(string? text)
        {
            if (renameCommitting || renamingId == null) return;
            renameCommitting = true;
            var s = data.Sheets.FirstOrDefault(x => x.Id == renamingId);
            if (s != null && !string.IsNullOrWhiteSpace(text)) s.Name = text.Trim();
            renamingId = null;
            BuildTabs();
            Save();
            renameCommitting = false;
        }

        void AddSheet_Click(object sender, RoutedEventArgs e)
        {
            if (viewing) return;
            Save();
            // A new sheet is a new project: it starts with its own, empty goal list (use "복사" to carry goals over).
            var ns = new Sheet { Name = $"새 일정표 {data.Sheets.Count + 1}" };
            data.Sheets.Add(ns);
            data.ActiveSheet = data.Sheets.Count - 1;
            renamingId = ns.Id;
            ReloadActive();
        }

        void CopySheet(Sheet s)
        {
            if (viewing) return;
            Save();
            var c = s.Clone(s.Name + " (복사)", keepDays: true);
            int at = data.Sheets.IndexOf(s) + 1;
            data.Sheets.Insert(at, c);
            data.ActiveSheet = at;
            ReloadActive();
        }

        void DeleteSheet(Sheet s)
        {
            if (viewing) return;
            if (data.Sheets.Count <= 1) return;
            string msg = $"'{s.Name}' 일정표를 삭제하면 목표 {s.Goals.Count}개와 지정된 날짜 {s.Days.Count}일이 모두 사라져요.\n이 작업은 되돌릴 수 없습니다.";
            if (!ConfirmDialog.Show(this, "일정표 삭제", msg, "삭제")) return;
            Save();
            int idx = data.Sheets.IndexOf(s);
            data.Sheets.RemoveAt(idx);
            if (idx < data.ActiveSheet) data.ActiveSheet--;
            else if (idx == data.ActiveSheet) data.ActiveSheet = Math.Min(idx, data.Sheets.Count - 1);
            ReloadActive();
        }

        void ShowTabMenu(Sheet s)
        {
            if (viewing) return;
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("DarkMenu"),
                PlacementTarget = TabsPanel,
                Placement = PlacementMode.MousePoint,
            };
            var style = (Style)FindResource("DarkMenuItem");

            MenuItem Item(string header, string glyph, Action act, bool enabled = true, Color? c = null)
            {
                var icon = new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
                    Foreground = new SolidColorBrush(c ?? Color.FromRgb(0xC9, 0xD1, 0xE3)),
                };
                var mi = new MenuItem { Header = header, Icon = icon, Style = style, IsEnabled = enabled };
                mi.Click += (_, _) => act();
                return mi;
            }

            menu.Items.Add(Item("이름 변경", "", () => StartRename(s.Id)));
            menu.Items.Add(Item("복사", "", () => CopySheet(s)));
            menu.Items.Add(Item("탭 색상…", "", () =>
            {
                if (!tabVisuals.TryGetValue(s.Id, out var v)) return;
                ShowColorPopup(v.Root, "탭 색상", TabAccent(s), FullPalette, c =>
                {
                    s.ColorHex = HexOf(c);
                    if (tabVisuals.TryGetValue(s.Id, out var tv)) ApplyTabColor(tv);
                    Persist();
                });
            }));
            if (s.ColorHex != null)
                menu.Items.Add(Item("탭 색상 초기화", "", () =>
                {
                    s.ColorHex = null;
                    if (tabVisuals.TryGetValue(s.Id, out var tv)) ApplyTabColor(tv);
                    Persist();
                }));
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSep") });
            menu.Items.Add(Item(data.Sheets.Count > 1 ? "삭제" : "삭제 (마지막 일정표)","", () => DeleteSheet(s),
                enabled: data.Sheets.Count > 1, c: Color.FromRgb(0xFF, 0x7A, 0x7F)));
            menu.IsOpen = true;
        }

        // drag to reorder
        void TabsPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (viewing) return;
            if (dragId == null || e.LeftButton != MouseButtonState.Pressed || renamingId != null) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(p.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var id = dragId.Value;
            dragId = null;
            DragDrop.DoDragDrop(TabsPanel, new DataObject("sheetId", id.ToString()), DragDropEffects.Move);
        }

        void TabsPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => dragId = null;

        void TabsPanel_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("sheetId") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        void TabsPanel_Drop(object sender, DragEventArgs e)
        {
            if (viewing) return;
            if (!e.Data.GetDataPresent("sheetId") || !Guid.TryParse((string)e.Data.GetData("sheetId")!, out var id)) return;
            int from = data.Sheets.FindIndex(x => x.Id == id);
            if (from < 0) return;

            double x = e.GetPosition(TabsPanel).X;
            int to = 0;
            foreach (FrameworkElement child in TabsPanel.Children)
                if (child.TranslatePoint(new Point(child.ActualWidth / 2, 0), TabsPanel).X < x) to++;
            if (from < to) to--;
            if (to == from) return;

            Save();
            var moving = data.Sheets[from];
            var current = Active;
            data.Sheets.RemoveAt(from);
            data.Sheets.Insert(to, moving);
            data.ActiveSheet = data.Sheets.IndexOf(current);
            BuildTabs();
            Persist();
        }

        // tab strip scrolling
        void UpdateTabScrollButtons()
        {
            bool over = TabScroll.ScrollableWidth > 0.5;
            TabLeft.Visibility = TabRight.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
            TabLeft.Opacity = TabScroll.HorizontalOffset > 0.5 ? 1 : 0.3;
            TabRight.Opacity = TabScroll.HorizontalOffset < TabScroll.ScrollableWidth - 0.5 ? 1 : 0.3;
        }

        void ScrollTabs(double delta)
        {
            double from = TabScroll.HorizontalOffset;
            double to = Math.Clamp(from + delta, 0, TabScroll.ScrollableWidth);
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut };
            a.Completed += (_, _) => BeginAnimation(TabOffsetProperty, null);
            BeginAnimation(TabOffsetProperty, a);
        }

        void TabLeft_Click(object sender, RoutedEventArgs e) => ScrollTabs(-180);
        void TabRight_Click(object sender, RoutedEventArgs e) => ScrollTabs(180);

        void TabScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollTabs(-e.Delta);
            e.Handled = true;
        }

        // ───────────────────────── goal panel ─────────────────────────

        // ───────────────────────── schedule (Gantt) panel under the calendar ─────────────────────────

        const double ScheduleHeight = 300;

        /// <summary>
        /// Asks the host to grow (positive) or shrink (negative) the card by this height. The board does it with its usual eased
        /// resize; <see cref="FollowCardSize"/> hands exactly the gained / lost height to the schedule row, so the calendar above
        /// never changes size. Without a listener the schedule row takes its height from the calendar instead.
        /// </summary>
        public event Action<double>? HeightChangeRequested;

        SchedulePanel? sched;
        bool following;                    // a card resize requested by the schedule toggle is in progress
        double followBase, followFrom, followTo;
        System.Windows.Threading.DispatcherTimer? followTimeout;

        void Schedule_Click(object sender, RoutedEventArgs e) => SetSchedule(!data.ScheduleOpen, animate: true);

        void SetSchedule(bool open, bool animate)
        {
            data.ScheduleOpen = open;
            ScheduleBtn.Foreground = new SolidColorBrush(open ? AccentColor : Color.FromRgb(0xC9, 0xD1, 0xE3));
            if (open && sched == null)
            {
                sched = new SchedulePanel(this);
                sched.CloseRequested += () => SetSchedule(false, animate: true);
                ScheduleHost.Child = sched;
                PushSchedule();
            }

            double from = ScheduleRow.ActualHeight, to = open ? ScheduleHeight : 0;
            if (!animate || HeightChangeRequested == null || !IsLoaded || Math.Abs(to - from) < 0.5)
            {
                following = false;
                ScheduleRow.Height = new GridLength(to);
                return;
            }

            // follow the card: whatever height the card gains or loses goes to (or comes from) the schedule row
            following = true;
            followFrom = from;
            followTo = to;
            followBase = ActualHeight - from;   // height of everything but the schedule
            HeightChangeRequested(to - from);

            // safety net: if the card never reaches the target (e.g. the board clamps its size), settle anyway
            followTimeout?.Stop();
            followTimeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            followTimeout.Tick += (_, _) => { followTimeout?.Stop(); if (following) { following = false; ScheduleRow.Height = new GridLength(followTo); } };
            followTimeout.Start();
        }

        /// <summary>While the card animates after a schedule toggle, gives the schedule row the card's extra height.</summary>
        void FollowCardSize()
        {
            if (!following) return;
            double v = Math.Clamp(ActualHeight - followBase, 0, Math.Max(followFrom, followTo));
            ScheduleRow.Height = new GridLength(v);
            if (Math.Abs(v - followTo) < 0.5)
            {
                following = false;
                followTimeout?.Stop();
                ScheduleRow.Height = new GridLength(followTo);
            }
        }

        /// <summary>Rebuilds the Gantt rows from the active sheet (only while the panel exists). Called at the end of <c>RefreshCells</c>.</summary>
        void PushSchedule()
        {
            if (sched == null) return;

            var rows = new List<ScheduleRow>();
            foreach (var g in goals)
            {
                var row = new ScheduleRow { Name = g.Name, Color = g.Rgb };
                foreach (var key in Days.Keys.Union(Days2.Keys))
                {
                    if (!IdsAt(key).Contains(g.Id)) continue;
                    if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
                    row.Days.Add(d);
                    if (IsDone(key, g.Id)) row.Done.Add(d);
                }
                row.Days.Sort();
                rows.Add(row);
            }

            var memos = new Dictionary<DateTime, string>();
            foreach (var kv in Notes)
                if (!string.IsNullOrWhiteSpace(kv.Value) &&
                    DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var md))
                    memos[md] = kv.Value;

            sched.SetData(new ScheduleData { SheetName = Active.Name, Rows = rows, Memos = memos });
        }

        // ───────────────────────── goal panel toggle ─────────────────────────

        void PanelToggle_Click(object sender, RoutedEventArgs e) => SetPanel(!data.PanelOpen, animate: true);

        void SetPanel(bool open, bool animate)
        {
            data.PanelOpen = open;
            double w = open ? PanelWidth : 0;
            double angle = open ? 180 : 0;   // open: "<" ; collapsed: ">"

            if (!animate)
            {
                PanelHost.BeginAnimation(WidthProperty, null);
                PanelHost.Width = w;
                ChevronRot.BeginAnimation(RotateTransform.AngleProperty, null);
                ChevronRot.Angle = angle;
                return;
            }

            var dur = TimeSpan.FromMilliseconds(320);
            PanelHost.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = EaseOut });
            ChevronRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, dur) { EasingFunction = EaseOut });
        }

        // ───────────────────────── colours: calendar background + picker ─────────────────────────

        /// <summary>Colour dot in the header: changes the calendar's background (saved with the card).</summary>
        void Theme_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPopup(ThemeBtn, "달력 색상", bgBrush.Color, WindowPresets, c =>
            {
                data.BgHex = HexOf(c);
                bgBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(c, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
            });
        }

        /// <summary>Popup with swatches, a hex field and R/G/B sliders. <paramref name="apply"/> fires live.</summary>
        void ShowColorPopup(FrameworkElement target, string title, Color initial, IReadOnlyList<Color> swatches, Action<Color> apply)
        {
            var pop = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = 4,
            };

            var muted = new SolidColorBrush(Color.FromRgb(0x99, 0xA3, 0xB8));
            var preview = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(initial), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)), BorderThickness = new Thickness(1.5) };
            var hexBox = new TextBox { Style = (Style)FindResource("Field"), Width = 96, Padding = new Thickness(9, 5, 9, 5), Text = HexOf(initial), FontFamily = new FontFamily("Consolas"), MaxLength = 7 };
            var sliders = new Slider[3];
            var chan = new[] { "R", "G", "B" };
            var chanBrush = new[] { Color.FromRgb(0xFF, 0x8A, 0x8A), Color.FromRgb(0x7D, 0xE0, 0x9B), Color.FromRgb(0x8A, 0xB4, 0xFF) };
            var init = new[] { initial.R, initial.G, initial.B };
            bool updating = false;

            void Set(Color c)
            {
                updating = true;
                preview.Background = new SolidColorBrush(c);
                hexBox.Text = HexOf(c);
                sliders[0].Value = c.R; sliders[1].Value = c.G; sliders[2].Value = c.B;
                updating = false;
                apply(c);
            }

            var root = new StackPanel { Width = 272 };
            root.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Margin = new Thickness(4, 2, 0, 6) });

            var wrap = new WrapPanel { Width = 272 };
            foreach (var sc in swatches)
            {
                var col = sc;
                var sw = new Button { Style = (Style)FindResource("SwatchBtn"), Background = new SolidColorBrush(col), Margin = new Thickness(4), ToolTip = HexOf(col) };
                sw.Click += (_, _) => Set(col);
                wrap.Children.Add(sw);
            }
            root.Children.Add(wrap);

            root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), Margin = new Thickness(4, 8, 4, 8) });

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 6) };
            top.Children.Add(preview);
            hexBox.Margin = new Thickness(10, 0, 0, 0);
            top.Children.Add(hexBox);
            top.Children.Add(new TextBlock { Text = "직접 입력 (#RRGGBB)", Foreground = muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            root.Children.Add(top);

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var s = new Slider { Style = (Style)FindResource("ThinSlider"), Minimum = 0, Maximum = 255, Value = init[i], IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
                s.ValueChanged += (_, _) =>
                {
                    if (updating) return;
                    Set(Color.FromRgb((byte)sliders[0].Value, (byte)sliders[1].Value, (byte)sliders[2].Value));
                };
                sliders[idx] = s;
                var row = new Grid { Margin = new Thickness(4, 0, 4, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = chan[i], Foreground = new SolidColorBrush(chanBrush[i]), FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(s, 1);
                row.Children.Add(s);
                root.Children.Add(row);
            }

            void CommitHex()
            {
                if (TryParseColor(hexBox.Text, out var c)) Set(c);
                else hexBox.Text = HexOf(((SolidColorBrush)preview.Background).Color);
            }
            hexBox.LostFocus += (_, _) => { if (!updating) CommitHex(); };
            hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitHex(); e.Handled = true; } };

            pop.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x20, 0x26, 0x36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10),
                Margin = new Thickness(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5 },
                Child = root,
            };
            pop.IsOpen = true;
        }

        void Goals_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (Goal g in e.OldItems) g.PropertyChanged -= Goal_PropertyChanged;
            if (e.NewItems != null) foreach (Goal g in e.NewItems) g.PropertyChanged += Goal_PropertyChanged;
            EmptyHint.Visibility = goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshCells();
            Save();
        }

        void Goal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Goal.Name) or nameof(Goal.Hex))
            {
                RefreshCells();
                Save();
            }
        }

        void AddGoal_Click(object sender, RoutedEventArgs e) => AddGoal();

        void NewGoalBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { AddGoal(); e.Handled = true; }
        }

        void AddGoal()
        {
            if (viewing) return;
            var name = NewGoalBox.Text.Trim();
            if (name.Length == 0) name = $"새 목표 {goals.Count + 1}";
            var hex = Palette.FirstOrDefault(p => goals.All(g => g.Hex != p)) ?? Palette[goals.Count % Palette.Length];
            goals.Add(new Goal { Name = name, Hex = hex });
            NewGoalBox.Clear();
        }

        static Goal? GoalOf(object sender) => (sender as FrameworkElement)?.Tag as Goal;

        void GoalUp_Click(object sender, RoutedEventArgs e)
        {
            if (viewing) return;
            if (GoalOf(sender) is not { } g) return;
            int i = goals.IndexOf(g);
            if (i > 0) { goals.Move(i, i - 1); Save(); }
        }

        void GoalDown_Click(object sender, RoutedEventArgs e)
        {
            if (viewing) return;
            if (GoalOf(sender) is not { } g) return;
            int i = goals.IndexOf(g);
            if (i >= 0 && i < goals.Count - 1) { goals.Move(i, i + 1); Save(); }
        }

        void GoalDelete_Click(object sender, RoutedEventArgs e)
        {
            if (viewing) return;
            if (GoalOf(sender) is not { } g) return;
            var used = Days.Where(kv => kv.Value == g.Id).Select(kv => kv.Key)
                .Union(Days2.Where(kv => kv.Value == g.Id).Select(kv => kv.Key)).ToList();
            string msg = used.Count > 0
                ? $"'{g.Name}' 목표를 삭제하면 지정된 {used.Count}일의 색상도 함께 지워져요.\n이 작업은 되돌릴 수 없습니다."
                : $"'{g.Name}' 목표를 삭제할까요?";
            if (!ConfirmDialog.Show(this, "목표 삭제", msg, "삭제")) return;
            foreach (var k in used) SetIds(k, IdsAt(k).Where(id => id != g.Id).ToList());
            goals.Remove(g);   // triggers refresh + save
        }

        void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (viewing) return;
            if (sender is not Button b || b.Tag is not Goal g) return;
            ShowColorPopup(b, $"'{g.Name}' 색상", g.Rgb, FullPalette, c => g.Hex = HexOf(c));
        }

        // ───────────────────────── calendar building / refresh ─────────────────────────

        static DateTime SundayOf(DateTime d) => d.Date.AddDays(-(int)d.DayOfWeek);

        /// <summary>First visible week = the week before the week containing the 1st of d's month.</summary>
        static DateTime StartFor(DateTime d) => SundayOf(new DateTime(d.Year, d.Month, 1)).AddDays(-7);

        static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        void BuildCells()
        {
            for (int r = 0; r < Rows; r++) DayGrid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < 7; c++) DayGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var accent = new SolidColorBrush(AccentColor);
            var muted = new SolidColorBrush(Color.FromRgb(0x99, 0xA3, 0xB8));

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = new DayCell
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
                    Scale = new ScaleTransform(1, 1),
                };
                var radius = new CornerRadius(10);
                cell.Root = new Border
                {
                    Margin = new Thickness(2.5),
                    CornerRadius = radius,
                    Background = cell.Fill,
                    RenderTransform = cell.Scale,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                cell.Hover = new Border { CornerRadius = radius, Opacity = 0, IsHitTestVisible = false, Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)) };
                cell.TodayRing = new Border { CornerRadius = radius, IsHitTestVisible = false, BorderBrush = accent, BorderThickness = new Thickness(1.8), Visibility = Visibility.Collapsed };
                cell.Chg = new Border { CornerRadius = radius, IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed, BorderBrush = new SolidColorBrush(ChangeColor), BorderThickness = new Thickness(1.8), Background = new SolidColorBrush(Color.FromArgb(0x22, ChangeColor.R, ChangeColor.G, ChangeColor.B)) };
                cell.ChgDot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(ChangeColor), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed };
                cell.Sel = new Border { CornerRadius = radius, IsHitTestVisible = false, Opacity = 0, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)) };
                cell.Num = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                cell.MonthTag = new TextBlock { FontSize = 10.5, Foreground = muted, Margin = new Thickness(0, 9, 9, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                cell.Label = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 255, 255, 255)), Margin = new Thickness(10, 0, 22, 7), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };

                cell.Memo = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A)), Margin = new Thickness(0, 8, 9, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };

                cell.Fill1 = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                cell.Fill2 = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                cell.Band1 = new Border { Background = cell.Fill1, CornerRadius = new CornerRadius(10, 10, 0, 0) };
                cell.Band2 = new Border { Background = cell.Fill2, CornerRadius = new CornerRadius(0, 0, 10, 10) };
                var bands = new Grid { IsHitTestVisible = false };
                bands.RowDefinitions.Add(new RowDefinition());
                bands.RowDefinitions.Add(new RowDefinition());
                Grid.SetRow(cell.Band2, 1);
                bands.Children.Add(cell.Band1);
                bands.Children.Add(cell.Band2);

                // Diagonal split: one brush whose colour jumps at the middle of the (0,0)->(1,1) gradient. In relative
                // coordinates that boundary runs exactly from the top-right to the bottom-left corner ("/"), whatever the cell size.
                cell.DiagStops = new[]
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.497),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.503), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                };
                cell.Diag = new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(new GradientStopCollection(cell.DiagStops), new Point(0, 0), new Point(1, 1)) };
                Grid.SetRowSpan(cell.Diag, 2);
                var diagBrush = (LinearGradientBrush)cell.Diag.Background;
                diagBrush.MappingMode = BrushMappingMode.Absolute;
                cell.Diag.SizeChanged += (_, e) => AimDiagonal(diagBrush, e.NewSize.Width, e.NewSize.Height, 10);
                bands.Children.Add(cell.Diag);

                static TextBlock Chk() => new()
                {
                    Text = "\uE73E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 8, 5), Opacity = 0, IsHitTestVisible = false,
                };
                static Border Msg() => new()
                {
                    Height = 2.5, CornerRadius = new CornerRadius(1.25), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(9, 0, 9, 3), Opacity = 0, IsHitTestVisible = false,
                };
                cell.Check1 = Chk(); cell.Check2 = Chk(); cell.Miss1 = Msg(); cell.Miss2 = Msg();
                Grid.SetRow(cell.Check2, 1); Grid.SetRow(cell.Miss2, 1);
                bands.Children.Add(cell.Check1); bands.Children.Add(cell.Check2);
                bands.Children.Add(cell.Miss1); bands.Children.Add(cell.Miss2);
                {   // with the diagonal split the earlier goal owns the left edge: its check / delay marks sit there
                    Grid.SetRowSpan(cell.Check1, 2);
                    cell.Check1.HorizontalAlignment = HorizontalAlignment.Left; cell.Check1.VerticalAlignment = VerticalAlignment.Center;
                    cell.Check1.Margin = new Thickness(9, 0, 0, 0);
                    Grid.SetRowSpan(cell.Miss1, 2);
                    cell.Miss1.HorizontalAlignment = HorizontalAlignment.Left; cell.Miss1.VerticalAlignment = VerticalAlignment.Center;
                    cell.Miss1.Width = 16; cell.Miss1.Margin = new Thickness(9, 20, 0, 0);
                }

                var g = new Grid();
                g.Children.Add(bands);
                g.Children.Add(cell.Hover);
                g.Children.Add(cell.Chg);
                g.Children.Add(cell.ChgDot);
                g.Children.Add(cell.TodayRing);
                g.Children.Add(cell.Num);
                g.Children.Add(cell.MonthTag);
                g.Children.Add(cell.Label);
                g.Children.Add(cell.Memo);
                g.Children.Add(cell.Sel);
                cell.Root.Child = g;

                var c = cell;
                cell.Root.MouseEnter += (_, _) => c.Hover.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
                cell.Root.MouseLeave += (_, _) => c.Hover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));

                Grid.SetRow(cell.Root, i / 7);
                Grid.SetColumn(cell.Root, i % 7);
                DayGrid.Children.Add(cell.Root);
                cells[i] = cell;
            }
        }

        /// <summary>Goal ids on a date in assignment order (earlier, later) - at most 2.</summary>
        List<Guid> IdsAt(string key)
        {
            var l = new List<Guid>(2);
            if (Days.TryGetValue(key, out var a)) l.Add(a);
            if (Days2.TryGetValue(key, out var b)) l.Add(b);
            return l;
        }

        void SetIds(string key, List<Guid> ids)
        {
            Days.Remove(key); Days2.Remove(key);
            if (ids.Count > 0) Days[key] = ids[0];
            if (ids.Count > 1) Days2[key] = ids[1];
            if (Done.TryGetValue(key, out var dn))
            {
                dn.RemoveAll(x => !ids.Contains(x));
                if (dn.Count == 0) Done.Remove(key);
            }
        }

        Dictionary<string, List<Guid>> Done => Active.Done;

        bool IsDone(string key, Guid id) => Done.TryGetValue(key, out var l) && l.Contains(id);

        bool AnyHave((DateTime lo, DateTime hi) r, Goal goal)
        {
            for (var d = r.lo; d <= r.hi; d = d.AddDays(1))
                if (HasGoal(d, goal.Id)) return true;
            return false;
        }

        /// <summary>True when every selected date that has the goal is already checked done.</summary>
        bool AllDone((DateTime lo, DateTime hi) r, Goal goal)
        {
            for (var d = r.lo; d <= r.hi; d = d.AddDays(1))
                if (HasGoal(d, goal.Id) && !IsDone(DayKey(d), goal.Id)) return false;
            return true;
        }

        void ToggleDone(Goal goal)
        {
            if (SelRange is not { } r) return;
            if (LiveBlocked()) return;
            bool undo = AllDone(r, goal);
            int n = 0;
            var changes = new List<(string, Guid, bool)>();
            for (var d = r.lo; d <= r.hi; d = d.AddDays(1))
            {
                if (!HasGoal(d, goal.Id)) continue;
                var key = DayKey(d);
                if (undo)
                {
                    if (Done.TryGetValue(key, out var l)) { l.Remove(goal.Id); if (l.Count == 0) Done.Remove(key); }
                }
                else
                {
                    if (!Done.TryGetValue(key, out var l)) Done[key] = l = new List<Guid>();
                    if (!l.Contains(goal.Id)) l.Add(goal.Id);
                }
                changes.Add((key, goal.Id, !undo));
                n++;
            }
            selA = selB = null;
            RefreshCells();
            UpdateSelection();
            StatusText.Text = undo ? $"'{goal.Name}' {n}일 완료 해제" : $"'{goal.Name}' {n}일 완료 처리 ✓";
            Save();
        }



        List<Goal> GoalsAt(DateTime d) =>
            IdsAt(DayKey(d)).Select(id => goals.FirstOrDefault(g => g.Id == id)).Where(g => g != null).Select(g => g!).ToList();

        bool HasGoal(DateTime d, Guid id) => IdsAt(DayKey(d)).Contains(id);

        /// <summary>Contiguous runs as "M/d~M/d : N일" lines (year added when not the current year).</summary>
        static string Summarize(List<DateTime>? days)
        {
            if (days == null || days.Count == 0) return "배정된 날짜 없음";
            days.Sort();
            static string F(DateTime d) => d.Year == DateTime.Today.Year ? $"{d.Month}/{d.Day}" : $"{d.Year}/{d.Month}/{d.Day}";

            var lines = new List<string>();
            var start = days[0];
            var prev = days[0];
            void Flush()
            {
                int n = (prev - start).Days + 1;
                lines.Add(n == 1 ? $"{F(start)} : 1일" : $"{F(start)}~{F(prev)} : {n}일");
            }
            foreach (var d in days.Skip(1))
            {
                if ((d - prev).Days == 1) { prev = d; continue; }
                Flush();
                start = prev = d;
            }
            Flush();
            return string.Join("\n", lines);
        }

        void RefreshCells()
        {
            if (cells[0] == null) return;   // not built yet

            var byGoal = new Dictionary<Guid, List<DateTime>>();
            foreach (var key in Days.Keys.Union(Days2.Keys))
            {
                if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
                foreach (var id in IdsAt(key))
                {
                    if (!byGoal.TryGetValue(id, out var list)) byGoal[id] = list = new List<DateTime>();
                    list.Add(day);
                }
            }
            foreach (var g in goals)
            {
                byGoal.TryGetValue(g.Id, out var days);
                g.DayCount = days?.Count ?? 0;
                g.Summary = Summarize(days);
            }

            var focus = ViewMonth;
            var today = DateTime.Today;
            MonthText.Text = $"{focus.Year}년 {focus.Month}월";

            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                var d = startDate.AddDays(i);
                bool sameDate = c.Date == d;
                c.Date = d;

                var gs = GoalsAt(d);
                var g = gs.Count > 0 ? gs[^1] : null;   // label goal = the later-added one
                bool inMonth = d.Month == focus.Month && d.Year == focus.Year;
                int col = i % 7;

                c.Num.Inlines.Clear();
                c.Num.Inlines.Add(new System.Windows.Documents.Run(d.Day.ToString()));
                if (d == today)
                    c.Num.Inlines.Add(new System.Windows.Documents.Run(" ★") { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A)), FontSize = 13 });
                c.Num.Opacity = inMonth ? 1 : 0.4;
                c.Num.Foreground = gs.Count > 0 ? Brushes.White
                    : col == 0 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A))
                    : col == 6 ? new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xFF))
                    : new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA));
                c.MonthTag.Text = d.Day == 1 ? $"{d.Month}월" : "";
                c.MonthTag.Foreground = gs.Count > 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x99, 0xA3, 0xB8));

                string dk = DayKey(d);
                bool past = d < today;
                var dn = gs.Select(x => IsDone(dk, x.Id)).ToList();
                Color Tint(Goal x, bool done) => Color.FromArgb(inMonth ? (byte)0xDD : (byte)0x8C, x.Rgb.R, x.Rgb.G, x.Rgb.B);   // done dates keep the goal colour
                var neutral = Color.FromArgb(inMonth ? (byte)0x16 : (byte)0x08, 255, 255, 255);
                void To(SolidColorBrush b, Color t) =>
                    b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(t, TimeSpan.FromMilliseconds(240)) { EasingFunction = EaseOut });
                static Color Clear(Color x) => Color.FromArgb(0, x.R, x.G, x.B);

                if (gs.Count == 2)
                {   // two goals: split diagonally "/" — earlier goal top-left, later (added) goal bottom-right
                    To(c.Fill, neutral);
                    To(c.Fill1, Clear(c.Fill1.Color));
                    To(c.Fill2, Clear(c.Fill2.Color));
                    DiagTo(c, Tint(gs[0], dn[0]), Tint(gs[1], dn[1]));
                }
                else
                {
                    To(c.Fill, gs.Count == 1 ? Tint(gs[0], dn[0]) : neutral);
                    To(c.Fill1, Clear(c.Fill1.Color));
                    To(c.Fill2, Clear(c.Fill2.Color));
                    DiagTo(c, Clear(c.DiagStops[0].Color), Clear(c.DiagStops[3].Color));
                }
                // the later goal's delay line becomes a short mark in the bottom-right corner when the cell is split
                bool diagSplit = gs.Count == 2;
                c.Miss2.HorizontalAlignment = diagSplit ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
                c.Miss2.Width = diagSplit ? 16 : double.NaN;

                // Progress marks: check = done, red line = past and not done. One goal -> lower slot, two -> one per half.
                void Mark(TextBlock chk, Border miss, bool has, bool done)
                {
                    chk.BeginAnimation(OpacityProperty, new DoubleAnimation(has && done ? 1 : 0, TimeSpan.FromMilliseconds(180)));
                    miss.BeginAnimation(OpacityProperty, new DoubleAnimation(has && !done && past ? 1 : 0, TimeSpan.FromMilliseconds(180)));
                }
                if (gs.Count == 2) { Mark(c.Check1, c.Miss1, true, dn[0]); Mark(c.Check2, c.Miss2, true, dn[1]); }
                else { Mark(c.Check1, c.Miss1, false, false); Mark(c.Check2, c.Miss2, gs.Count == 1, gs.Count == 1 && dn[0]); }

                // Name of the later goal, only on the first day of its contiguous run.
                bool showLabel = g != null && !HasGoal(d.AddDays(-1), g.Id);
                c.Label.Text = showLabel ? g!.Name : "";
                // two goals: the name belongs to the later goal, which owns the bottom-right triangle -> right-aligned
                c.Label.HorizontalAlignment = gs.Count == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                c.Label.TextAlignment = gs.Count == 2 ? TextAlignment.Right : TextAlignment.Left;
                // one goal: the name sits around the middle of the cell (left), clear of the date above and the marks below
                c.Label.VerticalAlignment = gs.Count == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                c.Label.Margin = gs.Count == 2 ? new Thickness(10, 0, 22, 7) : new Thickness(10, 12, 22, 0);
                c.TodayRing.Visibility = d == today ? Visibility.Visible : Visibility.Collapsed;
                Notes.TryGetValue(DayKey(d), out var memo);
                bool hasMemo = !string.IsNullOrWhiteSpace(memo);
                c.MonthTag.Margin = new Thickness(0, 9, hasMemo ? 25 : 9, 0);
                if (gs.Count > 0 || hasMemo)
                {
                    c.Root.ToolTip = BuildDayTip(d, gs, hasMemo ? memo : null);
                    ToolTipService.SetInitialShowDelay(c.Root, 200);
                }
                else c.Root.ClearValue(FrameworkElement.ToolTipProperty);
                if (hasMemo != c.HasMemo || !sameDate)
                    c.Memo.BeginAnimation(OpacityProperty, new DoubleAnimation(hasMemo ? 1 : 0, TimeSpan.FromMilliseconds(220)));
                c.HasMemo = hasMemo;

                var sig = string.Join("|", gs.Select(x => x.Id)) + string.Concat(dn.Select(b => b ? "1" : "0")) + (past ? "p" : "");
                if (sameDate && c.Sig != sig) Pulse(c);
                c.Sig = sig;
            }
            PushSchedule();
        }

        /// <summary>Hover tooltip: one line per goal on the date (name + the date range of its contiguous run), then the memo.</summary>
        ToolTip BuildDayTip(DateTime d, List<Goal> gs, string? memo = null)
        {
            var fg = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA));
            var mutedFg = new SolidColorBrush(Color.FromRgb(0xA9, 0xB3, 0xC7));
            var panel = new StackPanel();
            string F(DateTime x) => x.Year == DateTime.Today.Year ? $"{x.Month}/{x.Day}" : $"{x.Year}/{x.Month}/{x.Day}";

            for (int i = 0; i < gs.Count; i++)
            {
                var g = gs[i];
                var a = d; var b = d;
                while (HasGoal(a.AddDays(-1), g.Id)) a = a.AddDays(-1);
                while (HasGoal(b.AddDays(1), g.Id)) b = b.AddDays(1);
                int n = (b - a).Days + 1;

                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, i == 0 ? 0 : 5, 0, 0) };
                row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = g.Brush, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = fg });
                row.Children.Add(new TextBlock { Text = n == 1 ? $"  {F(a)} : 1일" : $"  {F(a)}~{F(b)} : {n}일", FontSize = 12, Foreground = mutedFg, VerticalAlignment = VerticalAlignment.Center });
                if (IsDone(DayKey(d), g.Id))
                    row.Children.Add(new TextBlock { Text = "  ✓ 완료", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0xCF, 0x97)), VerticalAlignment = VerticalAlignment.Center });
                else if (d < DateTime.Today)
                    row.Children.Add(new TextBlock { Text = "  지연", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(row);
            }
            if (memo != null)
            {
                var memoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, gs.Count > 0 ? 8 : 0, 0, 0) };
                memoRow.Children.Add(new TextBlock { Text = "\uE70F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A)), Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top });
                memoRow.Children.Add(new TextBlock { Text = memo, FontSize = 12.5, Foreground = fg, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
                panel.Children.Add(memoRow);
            }

            return new ToolTip
            {
                Content = panel,
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x21, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)),
                Padding = new Thickness(11, 8, 11, 8),
            };
        }

        /// <summary>
        /// Points the split line of a cell at the middle of its top-right and bottom-left rounded corners (not the hidden square
        /// corners): the line goes through the centre, perpendicular to the gradient, so the gradient runs along the normal of that line.
        /// </summary>
        static void AimDiagonal(LinearGradientBrush b, double w, double h, double radius)
        {
            double k = 2 * radius * (1 - Math.Cos(Math.PI / 4));        // corner cut-back of the two arc mid points, summed
            double vx = h - k, vy = w - k, len = Math.Sqrt(vx * vx + vy * vy);
            if (len < 1) return;
            vx /= len; vy /= len;
            const double half = 50;                                          // 100 px gradient: the 0.497..0.503 step is a crisp edge
            b.StartPoint = new Point(w / 2 - vx * half, h / 2 - vy * half); // earlier goal on the top-left side
            b.EndPoint = new Point(w / 2 + vx * half, h / 2 + vy * half);
        }

        /// <summary>Animates the diagonal split colours (a = earlier goal, top-left; b = later goal, bottom-right).</summary>
        static void DiagTo(DayCell c, Color a, Color b)
        {
            static void Go(GradientStop s, Color t) =>
                s.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(t, TimeSpan.FromMilliseconds(240)) { EasingFunction = EaseOut });
            Go(c.DiagStops[0], a); Go(c.DiagStops[1], a);
            Go(c.DiagStops[2], b); Go(c.DiagStops[3], b);
        }

        static void Pulse(DayCell c)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(340) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromPercent(0.4), EaseOut));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), EaseOut));
            c.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            c.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>Slide + staggered fade-in used when the visible weeks change.</summary>
        void Transition(int dir)
        {
            if (dir != 0)
                GridShift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(dir * 16, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });
            MonthText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));

            for (int i = 0; i < cells.Length; i++)
            {
                var root = cells[i].Root;
                root.BeginAnimation(OpacityProperty, null);
                root.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds((i / 7) * 28 + (i % 7) * 7);
                root.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay });
            }
        }

        void Navigate(DateTime newStart, int dir)
        {
            startDate = newStart;
            RefreshCells();
            UpdateSelection();
            Transition(dir);
        }

        void GoMonth(int delta)
        {
            var first = new DateTime(ViewMonth.Year, ViewMonth.Month, 1).AddMonths(delta);
            Navigate(StartFor(first), delta);
        }

        void PrevMonth_Click(object sender, RoutedEventArgs e) => GoMonth(-1);
        void NextMonth_Click(object sender, RoutedEventArgs e) => GoMonth(1);

        void Today_Click(object sender, RoutedEventArgs e)
        {
            var s = StartFor(DateTime.Today);
            if (s != startDate) Navigate(s, s > startDate ? 1 : -1);
            selA = selB = DateTime.Today;   // "오늘" also selects today
            UpdateSelection();
        }

        // ───────────────────────── selection (mouse down & drag) ─────────────────────────

        DateTime? DateAt(MouseEventArgs e, bool clamp)
        {
            var p = e.GetPosition(DayGrid);
            double w = DayGrid.ActualWidth, h = DayGrid.ActualHeight;
            if (w <= 0 || h <= 0) return null;
            if (!clamp && (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h)) return null;
            int c = Math.Clamp((int)(p.X / w * 7), 0, 6);
            int r = Math.Clamp((int)(p.Y / h * Rows), 0, Rows - 1);
            return startDate.AddDays(r * 7 + c);
        }

        (DateTime lo, DateTime hi)? SelRange =>
            selA is { } a && selB is { } b ? (a <= b ? (a, b) : (b, a)) : null;

        /// <summary>The cell whose memo pencil is under the mouse (a few pixels of slack around the small icon).</summary>
        DayCell? MemoIconAt(MouseEventArgs e)
        {
            foreach (var c in cells)
            {
                if (!c.HasMemo) continue;
                var pt = e.GetPosition(c.Memo);
                if (pt.X >= -6 && pt.Y >= -6 && pt.X <= c.Memo.ActualWidth + 6 && pt.Y <= c.Memo.ActualHeight + 6) return c;
            }
            return null;
        }

        void DayGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (MemoIconAt(e) is { } memoCell)
            {   // clicking the pencil opens that day's memo straight away
                e.Handled = true;
                EditMemo(memoCell.Date);
                return;
            }
            if (DateAt(e, false) is not { } d) return;
            Focus();
            selA = selB = d;
            dragging = true;
            DayGrid.CaptureMouse();
            UpdateSelection();
        }

        void DayGrid_MouseMove(object sender, MouseEventArgs e)
        {
            DayGrid.Cursor = !dragging && MemoIconAt(e) != null ? Cursors.Hand : null;
            if (!dragging) return;
            if (DateAt(e, true) is { } d && d != selB)
            {
                selB = d;
                UpdateSelection();
            }
        }

        void DayGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            DayGrid.ReleaseMouseCapture();
        }

        void DayGrid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DateAt(e, false) is not { } d) return;
            var r = SelRange;
            if (r == null || d < r.Value.lo || d > r.Value.hi)
            {
                selA = selB = d;
                UpdateSelection();
            }
        }

        void DayGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SelRange != null) ShowAssignMenu();
        }

        void DayGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int dir = e.Delta > 0 ? -1 : 1;
            Navigate(startDate.AddDays(7 * dir), dir);
        }

        void ClearSelection()
        {
            selA = selB = null;
            UpdateSelection();
        }

        void UpdateSelection()
        {
            var r = SelRange;
            foreach (var c in cells)
            {
                bool s = r != null && c.Date >= r.Value.lo && c.Date <= r.Value.hi;
                if (s == c.Selected) continue;
                c.Selected = s;
                c.Sel.BeginAnimation(OpacityProperty, new DoubleAnimation(s ? 1 : 0, TimeSpan.FromMilliseconds(110)));
            }

            if (r is { } v)
            {
                int n = (v.hi - v.lo).Days + 1;
                StatusText.Text = n == 1
                    ? $"{v.lo.Month}/{v.lo.Day} 선택 · 우클릭으로 목표 지정 · Esc 해제"
                    : $"{v.lo.Month}/{v.lo.Day} ~ {v.hi.Month}/{v.hi.Day} ({n}일) 선택 · 우클릭으로 목표 지정 · Esc 해제";
            }
            else
            {
                StatusText.Text = "날짜를 클릭하거나 드래그해서 목표선택 가능.";
            }
        }

        // ───────────────────────── assign goal ─────────────────────────

        void ShowAssignMenu()
        {
            if (SelRange is not { } r) return;
            int n = (r.hi - r.lo).Days + 1;

            var menu = new ContextMenu
            {
                Style = (Style)FindResource("DarkMenu"),
                PlacementTarget = DayGrid,
                Placement = PlacementMode.MousePoint,
            };
            var itemStyle = (Style)FindResource("DarkMenuItem");

            menu.Items.Add(new MenuItem
            {
                Header = n == 1 ? $"{r.lo.Month}월 {r.lo.Day}일" : $"{r.lo.Month}/{r.lo.Day} ~ {r.hi.Month}/{r.hi.Day} · {n}일",
                IsEnabled = false,
                Style = itemStyle,
            });
            if (!viewing)
            {
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSep") });

            if (goals.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "오른쪽 패널에서 목표를 먼저 추가하세요", IsEnabled = false, Style = itemStyle });
            }
            foreach (var g in goals)
            {
                var goal = g;
                var item = new MenuItem
                {
                    Header = AllHave(r, g) ? g.Name + "   ✓" : g.Name,
                    Icon = new Ellipse { Width = 13, Height = 13, Fill = g.Brush },
                    Style = itemStyle,
                };
                item.Click += (_, _) => Assign(goal);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSep") });
            var clear = new MenuItem
            {
                Header = "색상 지우기",
                Icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A)) },
                Style = itemStyle,
            };
            clear.Click += (_, _) => Assign(null);
            menu.Items.Add(clear);
            }

            var inSel = goals.Where(x => AnyHave(r, x)).ToList();
            if (inSel.Count > 0)
            {
                menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSep") });
                foreach (var pg in inSel)
                {
                    var goal = pg;
                    bool all = AllDone(r, goal);
                    var doneItem = new MenuItem
                    {
                        Header = (all ? "완료 해제 · " : "완료 체크 · ") + goal.Name,
                        Icon = new TextBlock { Text = "\uE73E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(all ? Color.FromRgb(0x99, 0xA3, 0xB8) : Color.FromRgb(0x6F, 0xCF, 0x97)) },
                        Style = itemStyle,
                    };
                    doneItem.Click += (_, _) => ToggleDone(goal);
                    menu.Items.Add(doneItem);
                }
            }

            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSep") });
            var key = DayKey(r.lo);
            bool has = Notes.ContainsKey(key);
            var memoItem = new MenuItem
            {
                Header = (has ? "메모 편집" : "메모 추가") + (n > 1 ? $" ({r.lo.Month}/{r.lo.Day})" : ""),
                Icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A)) },
                Style = itemStyle,
            };
            var date = r.lo;
            memoItem.Click += (_, _) => EditMemo(date);
            menu.Items.Add(memoItem);

            menu.IsOpen = true;
        }

        void EditMemo(DateTime date)
        {
            var key = DayKey(date);
            Notes.TryGetValue(key, out var cur);
            // a hover tooltip of the cell under the mouse would otherwise stay on top of the dialog
            foreach (var hc in cells) if (hc.Root.IsMouseOver && hc.Root.ToolTip is ToolTip tip) tip.IsOpen = false;
            var text = MemoDialog.Show(this, $"{date.Year}년 {date.Month}월 {date.Day}일 ({"일월화수목금토"[(int)date.DayOfWeek]})", cur ?? "", canDelete: !string.IsNullOrEmpty(cur));
            if (text == null) return;
            if (text.Length == 0 && string.IsNullOrEmpty(cur)) return;   // nothing was written
            if (text.Length == 0 && !ConfirmDialog.Show(this, "메모 삭제",
                    $"{date.Month}월 {date.Day}일의 메모를 삭제할까요?\n작성된 내용이 모두 사라져요.", "삭제")) return;
            if (text.Length == 0) Notes.Remove(key); else Notes[key] = text;
            selA = selB = null;
            RefreshCells();
            UpdateSelection();
            Save();
        }

        /// <summary>Applied to every selected date: null clears; a goal already on all of them is removed;
        /// otherwise it is added (max 2 per date - when full, the later-added goal is replaced).</summary>
        void Assign(Goal? goal)
        {
            if (viewing) return;
            if (SelRange is not { } r) return;
            bool allHave = goal != null && AllHave(r, goal);
            bool replaced = false;
            for (var d = r.lo; d <= r.hi; d = d.AddDays(1))
            {
                var key = DayKey(d);
                var ids = IdsAt(key);
                if (goal == null) ids.Clear();
                else if (allHave) ids.Remove(goal.Id);
                else if (!ids.Contains(goal.Id))
                {
                    if (ids.Count < 2) ids.Add(goal.Id);
                    else { ids[1] = goal.Id; replaced = true; }
                }
                SetIds(key, ids);
            }
            selA = selB = null;
            RefreshCells();
            UpdateSelection();
            if (replaced) StatusText.Text = "한 날짜에는 목표를 최대 2개까지 지정할 수 있어, 나중 목표를 교체했어요.";
            Save();
        }

        bool AllHave((DateTime lo, DateTime hi) r, Goal goal)
        {
            for (var d = r.lo; d <= r.hi; d = d.AddDays(1))
                if (!HasGoal(d, goal.Id)) return false;
            return true;
        }

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Escape) { ClearSelection(); e.Handled = true; }
            else if (e.Key == Key.Delete && SelRange != null) { Assign(null); e.Handled = true; }
        }

        void Save()
        {
            Active.Goals = goals.ToList();
            Persist();
        }
    }
}
