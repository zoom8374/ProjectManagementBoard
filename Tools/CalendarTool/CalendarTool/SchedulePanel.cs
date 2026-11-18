using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CalendarTool
{
    /// <summary>One goal as a Gantt row: its dates and which of them are checked done.</summary>
    public sealed class ScheduleRow
    {
        public string Name = "";
        public Color Color;
        public List<DateTime> Days = new();
        public HashSet<DateTime> Done = new();
    }

    public sealed class ScheduleData
    {
        public string SheetName = "";
        public List<ScheduleRow> Rows = new();
        public Dictionary<DateTime, string> Memos = new();
    }

    /// <summary>
    /// Read-only Gantt view of the current sheet: a task table on the left, day bars on the right. Shown as a panel under the
    /// calendar (the standalone app opened it as a separate window). Styles come from the calendar control.
    /// </summary>
    public class SchedulePanel : Border
    {
        const double RowH = 38, MonthH = 24, DayH = 28, BarH = 20;
        const double FixedCols = 244;   // days + start + end + status columns; the task column takes the rest of the table
        static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        static readonly Color Accent = Color.FromRgb(0x7C, 0x9C, 0xFF);
        static readonly Color Green = Color.FromRgb(0x6F, 0xCF, 0x97);
        static readonly Color Red = Color.FromRgb(0xFF, 0x6B, 0x6B);
        static readonly Color Blue = Color.FromRgb(0x4F, 0x8E, 0xF7);
        static readonly Color Gray = Color.FromRgb(0x6F, 0x7A, 0x90);

        readonly TextBlock subtitle, emptyText;
        readonly StackPanel leftRows = new();
        readonly Canvas headerCanvas = new(), bodyCanvas = new();
        readonly ScrollViewer headerSv, leftSv, bodySv;
        readonly Border dayChip, weekChip;
        readonly Grid leftHeader;
        readonly List<Grid> leftRowGrids = new();
        ColumnDefinition leftCol = null!;
        double nameW = 128;      // width of the task column (auto-fits the longest name unless the user dragged it)
        bool userSized;

        ScheduleData? data;
        double dw = 24;          // pixels per day (zoom)
        string lastSig = "";
        DateTime rangeStart;
        DispatcherTimer? scrollTimer;

        /// <summary>The close button was pressed: the calendar collapses the panel.</summary>
        public event Action? CloseRequested;

        /// <summary>
        /// Measured height of everything except the task rows (top bar, headers, padding, the timeline's horizontal scrollbar),
        /// or 0 until the panel has been laid out.
        /// </summary>
        public double ChromeHeight =>
            ActualHeight > 0 && bodySv.ActualHeight > 0
                ? ActualHeight - bodySv.ActualHeight + SystemParameters.HorizontalScrollBarHeight
                : 0;

        public double TaskRowHeight => RowH;

        public SchedulePanel(FrameworkElement host)
        {
            TextElement.SetForeground(this, new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA)));

            var muted = new SolidColorBrush(Color.FromRgb(0x99, 0xA3, 0xB8));

            // ── top bar
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "Schedule", FontSize = 18, FontWeight = FontWeights.SemiBold });
            subtitle = new TextBlock { FontSize = 11.5, Foreground = muted, Margin = new Thickness(0, 2, 0, 0) };
            titles.Children.Add(subtitle);

            dayChip = Chip("일", () => SetZoom(24));
            weekChip = Chip("주", () => SetZoom(10));
            var todayBtn = Chip("오늘", () => ScrollToToday(true));
            var zoom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            zoom.Children.Add(dayChip); zoom.Children.Add(weekChip);
            zoom.Children.Add(new Border { Width = 8 });
            zoom.Children.Add(todayBtn);

            var close = new Button { Style = (Style)host.FindResource("CloseBtn"), Content = "", ToolTip = "Schedule 닫기", FontSize = 15 };
            close.Click += (_, _) => CloseRequested?.Invoke();
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            right.Children.Add(zoom);
            right.Children.Add(close);

            var top = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 10) };
            top.Children.Add(titles);
            top.Children.Add(right);

            // ── left table header + rows, right timeline header + body
            leftHeader = new Grid { Height = MonthH + DayH, Width = nameW + FixedCols };
            leftHeader.Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
            AddCells(leftHeader, new[] { "작업", "일수", "시작일", "종료일", "상태" }, header: true);

            headerSv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = headerCanvas, Focusable = false };
            headerCanvas.Height = MonthH + DayH;
            headerCanvas.HorizontalAlignment = HorizontalAlignment.Left; headerCanvas.VerticalAlignment = VerticalAlignment.Top;
            bodyCanvas.HorizontalAlignment = HorizontalAlignment.Left; bodyCanvas.VerticalAlignment = VerticalAlignment.Top;   // a fixed-size canvas would otherwise be centred in the scroll area

            leftRows.Margin = new Thickness(0, 0, 0, 18);   // same height the timeline's horizontal scrollbar takes
            leftSv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = leftRows, Focusable = false };

            bodySv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = bodyCanvas };
            bodySv.ScrollChanged += (_, e) =>
            {
                headerSv.ScrollToHorizontalOffset(e.HorizontalOffset);
                leftSv.ScrollToVerticalOffset(e.VerticalOffset);
            };
            leftSv.PreviewMouseWheel += (_, e) => { bodySv.ScrollToVerticalOffset(bodySv.VerticalOffset - e.Delta); e.Handled = true; };
            bodySv.PreviewMouseWheel += (_, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
                bodySv.ScrollToHorizontalOffset(bodySv.HorizontalOffset - e.Delta);
                e.Handled = true;
            };

            emptyText = new TextBlock
            {
                Text = "표시할 일정이 없어요\n달력에서 날짜에 목표를 지정하면 여기에 나타나요", TextAlignment = TextAlignment.Center,
                Foreground = muted, FontSize = 13, LineHeight = 20, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };

            var grid = new Grid();
            leftCol = new ColumnDefinition { Width = new GridLength(nameW + FixedCols) };
            grid.ColumnDefinitions.Add(leftCol);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumnSpan(top, 2);
            Place(grid, top, 0, 0);
            Place(grid, leftHeader, 1, 0);
            Place(grid, headerSv, 1, 1);
            Place(grid, leftSv, 2, 0);
            Place(grid, bodySv, 2, 1);
            Grid.SetColumnSpan(emptyText, 2); Place(grid, emptyText, 2, 0);

            // drag handle on the right edge of the table: resize the task column; double-click = fit to the longest name
            var handleLine = new Border { Width = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B)), IsHitTestVisible = false };
            var handle = new Border
            {
                Width = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -6, 0), Background = Brushes.Transparent,
                Cursor = Cursors.SizeWE, Child = handleLine, ToolTip = "드래그해서 표 너비 조절 · 더블클릭하면 글자에 맞춰요",
            };
            Panel.SetZIndex(handle, 10);
            Grid.SetRow(handle, 1); Grid.SetRowSpan(handle, 2); Grid.SetColumn(handle, 0);
            grid.Children.Add(handle);
            void Glow(byte a) => ((SolidColorBrush)handleLine.Background).BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Color.FromArgb(a, Accent.R, Accent.G, Accent.B), TimeSpan.FromMilliseconds(140)));
            handleLine.Background = new SolidColorBrush(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B));
            handle.MouseEnter += (_, _) => Glow(0xAA);
            handle.MouseLeave += (_, _) => { if (!handle.IsMouseCaptured) Glow(0x00); };
            double dragX = 0, dragStart = 0;
            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    userSized = false;
                    if (data != null) nameW = AutoFitName(data.Rows);
                    ApplyLeftWidth();
                    e.Handled = true;
                    return;
                }
                dragX = e.GetPosition(this).X;
                dragStart = nameW;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (!handle.IsMouseCaptured) return;
                nameW = Math.Clamp(dragStart + (e.GetPosition(this).X - dragX), 80, 720);
                userSized = true;
                ApplyLeftWidth();
            };
            handle.MouseLeftButtonUp += (_, _) => { handle.ReleaseMouseCapture(); Glow(0x00); };

            Padding = new Thickness(16, 12, 12, 10);
            Child = grid;
            RefreshChips();
        }

        static void Place(Grid g, UIElement e, int row, int col)
        {
            Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e);
        }

        // ───────────── small controls

        Border Chip(string text, Action click)
        {
            var b = new Border
            {
                Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(11),
                Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)),
                Child = new TextBlock { Text = text, FontSize = 12 },
            };
            b.MouseLeftButtonUp += (_, _) => click();
            return b;
        }

        void Tint(Border b, bool on)
        {
            var target = on ? Color.FromArgb(0x55, 0x7C, 0x9C, 0xFF) : Color.FromArgb(0x16, 255, 255, 255);
            var brush = new SolidColorBrush(((SolidColorBrush)b.Background).Color);
            b.Background = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(160)) { EasingFunction = Ease });
            ((TextBlock)b.Child).Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xE3));
        }

        void RefreshChips()
        {
            Tint(dayChip, dw >= 20);
            Tint(weekChip, dw < 20);
        }

        void SetZoom(double pixelsPerDay)
        {
            if (Math.Abs(dw - pixelsPerDay) < 0.1) return;
            // keep the date at the left edge of the view where it is
            var leftDate = rangeStart.AddDays(bodySv.HorizontalOffset / dw);
            dw = pixelsPerDay;
            RefreshChips();
            Rebuild(animate: false);
            bodySv.ScrollToHorizontalOffset(Math.Max(0, (leftDate - rangeStart).TotalDays * dw));
        }

        void AddCells(Grid g, string[] texts, bool header)
        {
            double[] widths = { nameW, 48, 80, 80, 36 };
            for (int i = 0; i < widths.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i] + (i == 4 ? 0 : 0)) });
            for (int i = 0; i < texts.Length; i++)
            {
                var t = new TextBlock
                {
                    Text = texts[i], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB3, 0xC7)),
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(i == 0 ? 12 : 0, 0, 0, 0),
                };
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
        }

        // ───────────── table width

        /// <summary>Width the task column needs to show the longest name completely.</summary>
        double AutoFitName(IEnumerable<ScheduleRow> rows)
        {
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            double max = 0;
            foreach (var r in rows)
                max = Math.Max(max, new FormattedText(r.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 13, Brushes.White, ppd).Width);
            return Math.Clamp(Math.Ceiling(max) + 42, 128, 720);   // + colour dot and paddings
        }

        void ApplyLeftWidth()
        {
            double total = nameW + FixedCols;
            leftCol.Width = new GridLength(total);
            leftHeader.Width = total;
            leftHeader.ColumnDefinitions[0].Width = new GridLength(nameW);
            foreach (var r in leftRowGrids)
            {
                r.Width = total;
                r.ColumnDefinitions[0].Width = new GridLength(nameW);
            }
        }

        // ───────────── data

        /// <summary>Replaces what is shown. Bars animate in only when the sheet or its task list changed.</summary>
        public void SetData(ScheduleData d)
        {
            data = d;
            string sig = d.SheetName + "|" + string.Join(",", d.Rows.Select(r => r.Name));
            bool animate = sig != lastSig;
            lastSig = sig;
            Rebuild(animate);
            if (animate) ScrollToToday(false);
        }

        static string D(DateTime d) => $"{d.Year}-{d.Month}-{d.Day}";

        static (string text, Color color) StatusOf(ScheduleRow r)
        {
            var today = DateTime.Today;
            if (r.Days.Count == 0) return ("일정 없음", Gray);
            if (r.Days.All(r.Done.Contains)) return ("완료", Green);
            if (r.Days.Any(d => d < today && !r.Done.Contains(d))) return ("지연", Red);
            return r.Days[0] <= today ? ("진행 중", Blue) : ("예정", Gray);
        }

        void Rebuild(bool animate)
        {
            if (data == null) return;
            var rows = data.Rows;
            subtitle.Text = data.SheetName;
            var today = DateTime.Today;

            var all = rows.SelectMany(r => r.Days).ToList();
            emptyText.Visibility = rows.Count == 0 || all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // visible range: whole weeks around the data and today
            var min = all.Count > 0 ? all.Min() : today;
            var max = all.Count > 0 ? all.Max() : today;
            if (today < min) min = today;
            if (today > max) max = today;
            var start = min.AddDays(-7 - (int)min.AddDays(-7).DayOfWeek);
            var end = max.AddDays(14);
            if ((end - start).Days < 56) end = start.AddDays(56);
            rangeStart = start;
            int days = (end - start).Days + 1;
            double width = days * dw;

            // ── left table
            leftRows.Children.Clear();
            leftRowGrids.Clear();
            if (!userSized) nameW = AutoFitName(rows);
            ApplyLeftWidth();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var (stText, stColor) = StatusOf(row);
                var g = new Grid { Height = RowH, Width = nameW + FixedCols, Background = new SolidColorBrush(Color.FromArgb(r % 2 == 0 ? (byte)0x0C : (byte)0x00, 255, 255, 255)) };
                AddCells(g, new string[0], false);
                leftRowGrids.Add(g);

                var nameCell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
                nameCell.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(row.Color), Margin = new Thickness(0, 0, 8, 0) });
                nameCell.Children.Add(new TextBlock { Text = row.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(nameCell, 0); g.Children.Add(nameCell);

                void Cell(int col, string text)
                {
                    var t = new TextBlock { Text = text, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xE8)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(t, col); g.Children.Add(t);
                }
                Cell(1, row.Days.Count == 0 ? "-" : row.Days.Count.ToString());
                Cell(2, row.Days.Count == 0 ? "-" : D(row.Days[0]));
                Cell(3, row.Days.Count == 0 ? "-" : D(row.Days[^1]));

                var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(stColor), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = stText };
                Grid.SetColumn(dot, 4); g.Children.Add(dot);

                leftRows.Children.Add(g);
                if (animate)
                {
                    g.Opacity = 0;
                    g.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(r * 40) });
                }
            }

            // ── timeline header
            headerCanvas.Children.Clear();
            headerCanvas.Width = width;
            var hdrBg = new Rectangle { Width = width, Height = MonthH + DayH, Fill = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
            headerCanvas.Children.Add(hdrBg);

            // month row
            for (var d = start; d <= end;)
            {
                var monthEnd = new DateTime(d.Year, d.Month, 1).AddMonths(1).AddDays(-1);
                if (monthEnd > end) monthEnd = end;
                double x = (d - start).Days * dw, w = ((monthEnd - d).Days + 1) * dw;
                var label = new TextBlock { Text = $"{d.Year}년 {d.Month}월", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xE3)), Margin = new Thickness(8, 4, 0, 0) };
                var cell = new Border { Width = w, Height = MonthH, BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 1, 0), Child = label };
                Canvas.SetLeft(cell, x);
                headerCanvas.Children.Add(cell);
                d = monthEnd.AddDays(1);
            }

            // day row
            for (int i = 0; i < days; i++)
            {
                var d = start.AddDays(i);
                bool weekStart = d.DayOfWeek == DayOfWeek.Sunday;
                string text = dw >= 20 ? d.Day.ToString() : (weekStart ? $"{d.Month}/{d.Day}" : "");
                if (text.Length == 0 && !data.Memos.ContainsKey(d)) continue;

                var fg = d.DayOfWeek == DayOfWeek.Sunday ? Color.FromRgb(0xFF, 0x8A, 0x8A) : d.DayOfWeek == DayOfWeek.Saturday ? Color.FromRgb(0x8A, 0xB4, 0xFF) : Color.FromRgb(0xC9, 0xD1, 0xE3);
                var t = new TextBlock { Text = text, FontSize = dw >= 20 ? 12 : 11, Foreground = new SolidColorBrush(fg), HorizontalAlignment = dw >= 20 ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(dw >= 20 ? 0 : 4, 0, 0, 0) };
                var host = new Border { Width = dw >= 20 ? dw : dw * 7, Height = DayH, Child = t };
                if (d == today)
                {
                    host.Background = new SolidColorBrush(Color.FromArgb(0x55, Accent.R, Accent.G, Accent.B));
                    host.CornerRadius = new CornerRadius(6);
                    t.FontWeight = FontWeights.Bold; t.Foreground = Brushes.White;
                }
                if (data.Memos.TryGetValue(d, out var memo))
                {
                    var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFA)) };
                    tip.Inlines.Add(new Run($"{d.Month}/{d.Day} 메모") { Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xB3, 0xC7)) });
                    tip.Inlines.Add(new LineBreak());
                    tip.Inlines.Add(new Run(memo));
                    host.ToolTip = new ToolTip
                    {
                        Content = tip, Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x21, 0x30)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), Padding = new Thickness(10, 7, 10, 7),
                    };
                    var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4A)), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
                    host.Child = null;   // detach the label before re-parenting it into the marker grid
                    host.Child = new Grid { Children = { t, dot } };
                }
                Canvas.SetLeft(host, i * dw);
                Canvas.SetTop(host, MonthH);
                headerCanvas.Children.Add(host);
            }

            // ── timeline body
            bodyCanvas.Children.Clear();
            bodyCanvas.Width = width;
            double height = Math.Max(rows.Count * RowH, 1);
            bodyCanvas.Height = height;

            for (int i = 0; i < days; i++)
            {
                var d = start.AddDays(i);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    var shade = new Rectangle { Width = dw, Height = height, Fill = new SolidColorBrush(Color.FromArgb(0x0E, 255, 255, 255)) };
                    Canvas.SetLeft(shade, i * dw);
                    bodyCanvas.Children.Add(shade);
                }
                if (dw >= 20 || d.DayOfWeek == DayOfWeek.Sunday)
                {
                    var line = new Rectangle { Width = 1, Height = height, Fill = new SolidColorBrush(Color.FromArgb(d.DayOfWeek == DayOfWeek.Sunday ? (byte)0x26 : (byte)0x10, 255, 255, 255)) };
                    Canvas.SetLeft(line, i * dw);
                    bodyCanvas.Children.Add(line);
                }
            }
            for (int r = 0; r < rows.Count; r++)
            {
                var sep = new Rectangle { Width = width, Height = 1, Fill = new SolidColorBrush(Color.FromArgb(0x16, 255, 255, 255)) };
                Canvas.SetTop(sep, (r + 1) * RowH - 1);
                bodyCanvas.Children.Add(sep);
            }

            // bars: contiguous runs, split into done / not-done segments
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Days.Count == 0) continue;
                double top = r * RowH + (RowH - BarH) / 2;
                int idx = 0;
                (int days, DateTime from)? best = null;
                while (idx < row.Days.Count)
                {
                    int runStart = idx;
                    while (idx + 1 < row.Days.Count && (row.Days[idx + 1] - row.Days[idx]).Days == 1) idx++;
                    int runEnd = idx;
                    idx++;

                    var runFrom = row.Days[runStart]; var runTo = row.Days[runEnd];
                    int runDays = runEnd - runStart + 1, runDone = row.Days.Skip(runStart).Take(runDays).Count(row.Done.Contains);
                    string tip = $"{row.Name}\n{D(runFrom)} ~ {D(runTo)} · {runDays}일 · 완료 {runDone}일";

                    int s = runStart;
                    while (s <= runEnd)
                    {
                        bool done = row.Done.Contains(row.Days[s]);
                        int e = s;
                        while (e + 1 <= runEnd && row.Done.Contains(row.Days[e + 1]) == done) e++;
                        bool delayed = !done && row.Days[e] < today;

                        double x = (row.Days[s] - start).Days * dw;
                        double w = (e - s + 1) * dw;
                        var c = row.Color;
                        var bar = new Border
                        {
                            Width = Math.Max(3, w - 1), Height = BarH,
                            CornerRadius = new CornerRadius(s == runStart ? 6 : 0, e == runEnd ? 6 : 0, e == runEnd ? 6 : 0, s == runStart ? 6 : 0),
                            Background = new SolidColorBrush(done ? Color.FromRgb(c.R, c.G, c.B) : Color.FromArgb(0x55, c.R, c.G, c.B)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(done ? (byte)0x00 : (byte)0xAA, c.R, c.G, c.B)),
                            BorderThickness = new Thickness(done ? 0 : 1),
                            ToolTip = tip,
                            RenderTransformOrigin = new Point(0, 0.5),
                            RenderTransform = new ScaleTransform(1, 1),
                        };
                        if (done && w >= 18)
                            bar.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
                        Canvas.SetLeft(bar, x + 0.5);
                        Canvas.SetTop(bar, top);
                        bodyCanvas.Children.Add(bar);

                        if (delayed)
                        {
                            var miss = new Rectangle { Width = Math.Max(3, w - 1), Height = 2.5, Fill = new SolidColorBrush(Red), RadiusX = 1, RadiusY = 1 };
                            Canvas.SetLeft(miss, x + 0.5);
                            Canvas.SetTop(miss, top + BarH + 2);
                            bodyCanvas.Children.Add(miss);
                        }

                        if (animate)
                        {
                            var st = (ScaleTransform)bar.RenderTransform;
                            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(r * 45 + (s - runStart) * 6), EasingFunction = Ease };
                            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                        }
                        s = e + 1;
                    }

                    // remember the longest run: the name is drawn once, left-aligned at its start
                    if (best == null || runDays > best.Value.days) best = (runDays, runFrom);
                }

                // task name left-aligned at the start of its longest run; it may extend past a short bar
                if (best is { } bst)
                {
                    var name = new TextBlock
                    {
                        Text = row.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, IsHitTestVisible = false,
                        Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black },
                    };
                    name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));   // on the canvas, so it is never clipped at the bar edges
                    Canvas.SetLeft(name, (bst.from - start).Days * dw + 8);   // left-aligned at the start of the bar
                    Canvas.SetTop(name, top + (BarH - name.DesiredSize.Height) / 2);
                    bodyCanvas.Children.Add(name);
                }
            }

            // today line
            if (today >= start && today <= end)
            {
                var line = new Rectangle { Width = 2, Height = height, Fill = new SolidColorBrush(Color.FromArgb(0xCC, Accent.R, Accent.G, Accent.B)), IsHitTestVisible = false };
                Canvas.SetLeft(line, (today - start).Days * dw + dw / 2 - 1);
                bodyCanvas.Children.Add(line);
            }
        }

        // ───────────── scrolling

        void ScrollToToday(bool animated)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var today = DateTime.Today;
                var target = Math.Max(0, (today - rangeStart).TotalDays * dw - bodySv.ViewportWidth / 3);
                if (!animated) { AnimateScroll(target); return; }
                AnimateScroll(target);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Eased horizontal scroll to <paramref name="target"/> (about 380 ms).</summary>
        void AnimateScroll(double target)
        {
            scrollTimer?.Stop();
            double from = bodySv.HorizontalOffset;
            if (Math.Abs(target - from) < 2) { bodySv.ScrollToHorizontalOffset(target); return; }
            var t0 = DateTime.UtcNow;
            scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            scrollTimer.Tick += (_, _) =>
            {
                double k = Math.Min(1, (DateTime.UtcNow - t0).TotalMilliseconds / 380);
                double e = 1 - Math.Pow(1 - k, 3);
                bodySv.ScrollToHorizontalOffset(from + (target - from) * e);
                if (k >= 1) scrollTimer?.Stop();
            };
            scrollTimer.Start();
        }
    }
}
