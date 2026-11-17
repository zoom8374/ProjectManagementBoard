using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectManagementBoard.Contracts;
using ProjectManagementBoard.Controls;
using ProjectManagementBoard.Models;
using ProjectManagementBoard.Services;

namespace ProjectManagementBoard
{
    public partial class MainWindow : Window
    {
        private const double SlotGap = 32;
        private const double BoardPadding = 32;

        private const double MinZoom = 0.05, MaxZoom = 2.5;
        private const double ZoomStep = 1.2;
        private const double ZoomStiffness = 18;
        private const double PanFriction = 5;     // inertia decay per second
        private const double MinFlingSpeed = 15;  // px/s below which inertia stops

        // Cards in z-order (last = top-most); this order is also what gets saved.
        private readonly List<ToolModel> _cards;
        private readonly Dictionary<ToolModel, ToolCardView> _views = new();
        private int _topZ;

        // Groups sit below every card (z-index far negative), in list order.
        private const int GroupZBase = -1_000_000;
        private readonly List<GroupModel> _groups;
        private readonly Dictionary<GroupModel, GroupView> _groupViews = new();
        private (List<ToolModel> Cards, List<GroupModel> Groups) _draggedGroupMembers = (new(), new());
        private GroupView? _highlightedGroup;

        /// <summary>Restores the last deleted card or group (toast "실행 취소" / Ctrl+Z).</summary>
        private Action? _undo;
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

        private readonly ToolCatalog _catalog = ToolCatalog.Load();
        private readonly AppSettings _settings;
        private readonly GradientStop _bgTop = new() { Offset = 0 };
        private readonly GradientStop _bgBottom = new() { Offset = 1 };

        // Camera: screen = board * zoom + pan. Zoom eases toward _targetZoom around an anchor point;
        // panning follows the pointer 1:1 and keeps sliding (inertia) after release.
        private double _zoom = 1, _targetZoom = 1, _panX, _panY;
        private Point _zoomAnchorScreen, _zoomAnchorBoard;
        private bool _zoomAnimating, _panning, _cameraTicking;
        private Point? _glideTarget; // pan target when bringing a new item into view
        private readonly DrawingBrush _gridBrush;
        private bool _flying;        // 전체 보기 animation (zoom + center together)
        private Point _flyCenter, _flyTargetCenter;
        private const double FlyStiffness = 8, FitMargin = 48, MinFitZoom = 0.002;
        private const double GlideStiffness = 9, RevealMargin = 40;
        private Point _panStartMouse, _panStartOffset, _lastPanMouse;
        private Vector _panVelocity;
        private readonly Stopwatch _panClock = new();
        private TimeSpan _lastCameraFrame;

        public MainWindow()
        {
            InitializeComponent();
            Icon = RenderLogoIcon();
            _settings = BoardStore.LoadSettings();

            _cards = new List<ToolModel>();
            _groups = new List<GroupModel>();
            if (_settings.LastFilePath is { } last && File.Exists(last))
            {
                try
                {
                    var file = BoardStore.LoadProject(last);
                    _cards.AddRange(file.Tools);
                    _groups.AddRange(file.Groups);
                    if (file.BackgroundColor != null && ColorUtil.TryParse(file.BackgroundColor, out _))
                        _settings.BackgroundColor = file.BackgroundColor;
                    _currentFilePath = last;
                }
                catch (Exception) { _cards.Clear(); _groups.Clear(); }
            }
            var background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            background.GradientStops.Add(_bgTop);
            background.GradientStops.Add(_bgBottom);
            Background = background;
            BackgroundSwatch.Fill = new SolidColorBrush(_bgTop.Color);
            ApplyBackground(_settings.BackgroundColor, animate: false);

            // The dot grid shares the board's transform so it pans/zooms with the cards.
            _gridBrush = ((DrawingBrush)FindResource("DotGrid")).Clone();
            _gridBrush.Transform = Board.RenderTransform;
            Viewport.Background = _gridBrush;
            ApplyDotColor(); // the first ApplyBackground ran before the grid brush existed

            SetSnapMove(_settings.SnapMove, save: false);
            SetLeftPanel(_settings.LeftPanelOpen, animate: false);
            SetTopBar(_settings.TopBarOpen, animate: false);

            _toastTimer.Tick += (_, _) => HideToast();
            _dirtyTimer.Tick += (_, _) => UpdateSaveIndicator();
            Editor.Closed += (_, _) => Board.Focus();

            Loaded += (_, _) =>
            {
                foreach (var group in _groups) AddGroupView(group, TimeSpan.Zero);
                for (int i = 0; i < _cards.Count; i++)
                    AddView(_cards[i], TimeSpan.FromMilliseconds(60 * i)); // staggered entrance
                UpdateGroupCounts();
                UpdateChrome();
                Board.Focus();
                // Views and tools may normalize loaded data; that alone is not an unsaved change, so baseline now.
                TakeSavedBaseline();
                _dirtyTimer.Start();
                if (_catalog.Errors.Count > 0)
                    ShowToast($"관리 툴 {_catalog.Errors.Count}개를 불러오지 못했어요: {_catalog.Errors[0]}", withUndo: false);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitAll);
            };
        }

        /// <summary>Rasterizes the vector AppLogo so it can be the window/taskbar icon (Icon needs a bitmap).</summary>
        private ImageSource RenderLogoIcon()
        {
            const int size = 128;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage((ImageSource)FindResource("AppLogo"), new Rect(0, 0, size, size));
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        #region Title bar (custom chrome)

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// A chromeless maximized window extends past the screen by the resize border, so pad it back in,
        /// and swap the maximize/restore glyph.
        /// </summary>
        private void Window_StateChanged(object? sender, EventArgs e)
        {
            bool maximized = WindowState == WindowState.Maximized;
            var border = SystemParameters.WindowResizeBorderThickness;
            WindowFrame.Margin = maximized
                ? new Thickness(border.Left + 4, border.Top + 4, border.Right + 4, border.Bottom + 4)
                : new Thickness(0);
            WindowFrame.BorderThickness = new Thickness(maximized ? 0 : 1);
            MaximizeButton.Content = maximized ? "" : "";
            MaximizeButton.ToolTip = maximized ? "이전 크기로" : "최대화";
        }

        #endregion

        #region Move mode (자유이동 / 맞춤이동)

        private void SetSnapMove(bool snap, bool save = true)
        {
            BoardSnap.Step = snap ? BoardSnap.GridStep : 0;
            // The selected mode gets the highlighted pill; the other one stays flat.
            ModeSnapBack.Background = snap ? (Brush)FindResource("Fg33") : Brushes.Transparent;
            ModeFreeBack.Background = snap ? Brushes.Transparent : (Brush)FindResource("Fg33");
            ModeSnapButton.FontWeight = snap ? FontWeights.SemiBold : FontWeights.Normal;
            ModeFreeButton.FontWeight = snap ? FontWeights.Normal : FontWeights.SemiBold;
            if (!save) return;
            _settings.SnapMove = snap;
            BoardStore.TrySaveSettings(_settings);
        }

        private void ModeFree_Click(object sender, RoutedEventArgs e) => SetSnapMove(false);

        private void ModeSnap_Click(object sender, RoutedEventArgs e) => SetSnapMove(true);

        #endregion

        #region Panels (left panel / top bar)

        public static readonly DependencyProperty LeftWidthProperty = DependencyProperty.Register(
            nameof(LeftWidth), typeof(double), typeof(MainWindow),
            new PropertyMetadata(200.0, (d, e) => ((MainWindow)d).ApplyLeftWidth((double)e.NewValue)));

        /// <summary>Width of the left panel; animated, and mirrored by the title bar's logo column so both stay one column.</summary>
        public double LeftWidth { get => (double)GetValue(LeftWidthProperty); set => SetValue(LeftWidthProperty, value); }

        private const double LeftPanelFullWidth = 200;
        private bool _leftOpen = true, _topBarOpen = true;
        private RotateTransform? _chevronRotate; // the top-bar tab's arrow (lives inside the button template)

        private void ApplyLeftWidth(double width)
        {
            LeftPanel.Width = width;
            TitleLeftColumn.Width = new GridLength(width);
        }

        private void SetLeftPanel(bool open, bool animate)
        {
            _leftOpen = open;
            var target = open ? LeftPanelFullWidth : 0;
            if (animate)
                BeginAnimation(LeftWidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else
            {
                BeginAnimation(LeftWidthProperty, null);
                LeftWidth = target;
            }
        }

        private void SetTopBar(bool open, bool animate)
        {
            _topBarOpen = open;
            var scale = (ScaleTransform)TopBar.LayoutTransform;
            var duration = TimeSpan.FromMilliseconds(animate ? 240 : 0);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            TopBar.Visibility = Visibility.Visible;
            // Not exactly 0: a zero scale would make the layout transform non-invertible.
            var shrink = new DoubleAnimation(open ? 1 : 0.001, duration) { EasingFunction = ease };
            if (!open)
                shrink.Completed += (_, _) => { if (!_topBarOpen) TopBar.Visibility = Visibility.Collapsed; };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            TopBar.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, duration));
            if (_chevronRotate == null && TopBarToggle.ApplyTemplate() &&
                TopBarToggle.Template.FindName("Chevron", TopBarToggle) is TextBlock chevron)
                chevron.RenderTransform = _chevronRotate = new RotateTransform();
            _chevronRotate?.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 0 : 180, duration) { EasingFunction = ease });
        }

        private void ToggleLeftPanel()
        {
            SetLeftPanel(!_leftOpen, animate: true);
            _settings.LeftPanelOpen = _leftOpen;
            BoardStore.TrySaveSettings(_settings);
        }

        private void ToggleTopBar()
        {
            SetTopBar(!_topBarOpen, animate: true);
            _settings.TopBarOpen = _topBarOpen;
            BoardStore.TrySaveSettings(_settings);
        }

        private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e) => ToggleLeftPanel();

        private void ToggleTopBar_Click(object sender, RoutedEventArgs e) => ToggleTopBar();

        #endregion

        #region Saving / projects

        // Saving is manual (title bar / Ctrl+S); this timer only refreshes the "unsaved" indicator.
        private readonly DispatcherTimer _dirtyTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private string _lastSavedJson = "", _lastSavedGroupsJson = "", _lastSavedBackground = "";
        private bool? _shownDirty;
        private bool _closeConfirmed;

        // The project file being edited (null until first saved: 저장 then asks where to save).
        private string? _currentFilePath;

        private const string FileFilter = "Project Management Board 파일 (*.pmb)|*.pmb|모든 파일 (*.*)|*.*";
        private const string AppTitle = "Project Management Board";

        private string CurrentFileName =>
            _currentFilePath == null ? "새 프로젝트" : System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);

        /// <summary>Asks every docked tool for its current data, so the model (and thus the file / dirty check) is up to date.</summary>
        private void CaptureToolStates()
        {
            foreach (var view in _views.Values) view.CaptureState();
        }

        /// <summary>Unsaved = the board (cards, tool data, groups) or background differs from what was last written.</summary>
        private bool IsDirty
        {
            get
            {
                CaptureToolStates();
                return BoardStore.Serialize(_cards) != _lastSavedJson ||
                       BoardStore.SerializeGroups(_groups) != _lastSavedGroupsJson ||
                       _settings.BackgroundColor != _lastSavedBackground;
            }
        }

        private bool Save() => _currentFilePath == null ? SaveAs() : WriteFile(_currentFilePath);

        private bool SaveAs()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = FileFilter,
                DefaultExt = ProjectFile.Extension,
                AddExtension = true,
                FileName = CurrentFileName + ProjectFile.Extension,
                InitialDirectory = DialogFolder(),
            };
            return dialog.ShowDialog(this) == true && WriteFile(dialog.FileName);
        }

        private bool WriteFile(string path)
        {
            CaptureToolStates();
            try
            {
                BoardStore.SaveProject(path, new ProjectFile
                {
                    Tools = _cards,
                    Groups = _groups,
                    BackgroundColor = _settings.BackgroundColor,
                });
            }
            catch (Exception ex)
            {
                ShowToast($"저장하지 못했어요: {ex.Message}", withUndo: false);
                return false;
            }
            _currentFilePath = path;
            RememberFile(path);
            TakeSavedBaseline();
            ShowToast($"‘{System.IO.Path.GetFileName(path)}’에 저장했어요", withUndo: false);
            return true;
        }

        /// <summary>Runs <paramref name="proceed"/> now, or after the user decides what to do with unsaved changes.</summary>
        private void ConfirmUnsaved(string message, string saveText, Action proceed)
        {
            if (!IsDirty)
            {
                proceed();
                return;
            }
            Editor.Show(new EditRequest("저장하지 않은 변경 사항", Array.Empty<EditField>(), 0,
                Save: () => Dispatcher.InvokeAsync(() => { if (Save()) proceed(); }))
            {
                Message = message,
                SaveText = saveText,
                Discard = () => Dispatcher.InvokeAsync(proceed),
            });
        }

        private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

        private void Save_Click(object sender, RoutedEventArgs e) => Save();

        private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

        private void AddProject_Click(object sender, RoutedEventArgs e) => AddProject();

        private void OpenFile() =>
            ConfirmUnsaved("다른 프로젝트를 불러오기 전에 지금까지의 변경 내용을 저장할까요?", "저장 후 불러오기", ChooseAndOpen);

        private void ChooseAndOpen()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = FileFilter, InitialDirectory = DialogFolder() };
            if (dialog.ShowDialog(this) == true) OpenProjectFile(dialog.FileName);
        }

        private void OpenProjectFile(string path)
        {
            ProjectFile file;
            try
            {
                file = BoardStore.LoadProject(path);
            }
            catch (Exception)
            {
                ShowToast("불러오지 못했어요. 올바른 프로젝트 파일이 아니에요.", withUndo: false);
                return;
            }
            ReplaceBoard(file);
            _currentFilePath = path;
            RememberFile(path);
            TakeSavedBaseline();
            ShowToast($"‘{System.IO.Path.GetFileName(path)}’을 불러왔어요", withUndo: false);
        }

        /// <summary>+ 새 프로젝트: asks where to create a blank project file, then switches the board to it.</summary>
        private void AddProject() =>
            ConfirmUnsaved("새 프로젝트를 만들기 전에 지금까지의 변경 내용을 저장할까요?", "저장 후 새 프로젝트", CreateNewProject);

        private void CreateNewProject()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = FileFilter,
                DefaultExt = ProjectFile.Extension,
                AddExtension = true,
                FileName = "새 프로젝트" + ProjectFile.Extension,
                InitialDirectory = DialogFolder(),
            };
            if (dialog.ShowDialog(this) != true) return;

            var file = new ProjectFile { BackgroundColor = _settings.BackgroundColor };
            try
            {
                BoardStore.SaveProject(dialog.FileName, file);
            }
            catch (Exception ex)
            {
                ShowToast($"프로젝트를 만들지 못했어요: {ex.Message}", withUndo: false);
                return;
            }
            ReplaceBoard(file);
            _currentFilePath = dialog.FileName;
            RememberFile(dialog.FileName);
            TakeSavedBaseline();
            ShowToast($"‘{System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}’ 프로젝트를 만들었어요", withUndo: false);
        }

        /// <summary>Swaps the whole board for a loaded project (views rebuilt, undo cleared, camera refitted).</summary>
        private void ReplaceBoard(ProjectFile file)
        {
            foreach (var view in _views.Values) { view.Detach(); Board.Children.Remove(view); }
            foreach (var view in _groupViews.Values) { view.Detach(); Board.Children.Remove(view); }
            _views.Clear();
            _groupViews.Clear();
            _highlightedGroup = null;
            _undo = null;
            _selectedCard = null;

            _cards.Clear();
            _cards.AddRange(file.Tools);
            _groups.Clear();
            _groups.AddRange(file.Groups);
            if (file.BackgroundColor != null && ColorUtil.TryParse(file.BackgroundColor, out _))
            {
                _settings.BackgroundColor = file.BackgroundColor;
                ApplyBackground(file.BackgroundColor, animate: true);
            }

            foreach (var group in _groups) AddGroupView(group, TimeSpan.Zero);
            for (int i = 0; i < _cards.Count; i++) AddView(_cards[i], TimeSpan.FromMilliseconds(40 * i));
            UpdateGroupCounts();
            UpdateChrome();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitAll);
        }

        /// <summary>Marks the current state as saved (after views and tools normalized their data) and refreshes the title.</summary>
        private void TakeSavedBaseline()
        {
            CaptureToolStates();
            _lastSavedJson = BoardStore.Serialize(_cards);
            _lastSavedGroupsJson = BoardStore.SerializeGroups(_groups);
            _lastSavedBackground = _settings.BackgroundColor;
            UpdateSaveIndicator();
            UpdateFileTitle();
        }

        private void RememberFile(string path)
        {
            _settings.LastFilePath = path;
            BoardStore.TrySaveSettings(_settings);
        }

        private string DialogFolder() =>
            _currentFilePath != null && System.IO.Path.GetDirectoryName(_currentFilePath) is { } dir && Directory.Exists(dir)
                ? dir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        private void UpdateFileTitle()
        {
            FileNameText.Text = _currentFilePath == null ? "새 프로젝트 (아직 저장 안 됨)" : System.IO.Path.GetFileName(_currentFilePath);
            FileNameText.ToolTip = _currentFilePath ?? "저장을 누르면 파일 위치를 고를 수 있어요";
            Title = _currentFilePath == null ? AppTitle : $"{CurrentFileName} - {AppTitle}";
        }

        private void UpdateSaveIndicator()
        {
            bool dirty = IsDirty;
            if (dirty == _shownDirty) return;
            _shownDirty = dirty;
            SaveLabel.Text = dirty ? "저장" : "저장됨";
            UnsavedDot.BeginAnimation(OpacityProperty, new DoubleAnimation(dirty ? 1 : 0, TimeSpan.FromMilliseconds(200)));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_closeConfirmed || !IsDirty) return;

            e.Cancel = true;
            Editor.Show(new EditRequest("저장하지 않은 변경 사항", Array.Empty<EditField>(), 0,
                Save: () =>
                {
                    if (!Save()) return; // keep the window open if writing failed
                    _closeConfirmed = true;
                    Dispatcher.InvokeAsync(Close);
                })
            {
                Message = "닫기 전에 지금까지의 변경 내용을 저장할까요?",
                SaveText = "저장 후 닫기",
                Discard = () =>
                {
                    _closeConfirmed = true;
                    Dispatcher.InvokeAsync(Close);
                },
            });
        }

        #endregion

        #region Cards

        private ToolCardView AddView(ToolModel card, TimeSpan enterDelay)
        {
            var view = new ToolCardView(card, _catalog);
            view.Activated += (_, _) =>
            {
                BringToFront(view);
                _selectedCard = card;
            };
            view.DeleteRequested += (_, _) => DeleteCard(view);
            view.CopyRequested += (_, _) => CopyCard(card);
            view.EditRequested += (_, request) => Editor.Show(request);
            view.DragMoved += (_, _) => HighlightGroupUnder(view.Model);
            view.Released += (_, _) =>
            {
                HighlightGroupUnder(null);
                UpdateGroupCounts();
            };
            Panel.SetZIndex(view, ++_topZ);
            Board.Children.Add(view);
            _views[card] = view;
            view.PlayEnter(enterDelay);
            return view;
        }

        private const string BlankCardOption = "빈 카드";

        /// <summary>+ 새 관리 툴: asks which tool to add (when any are installed in Plugins), then creates its card.</summary>
        /// <param name="at">Board coordinates to center the new card on; null = next free slot in view.</param>
        private void AddCard(Point? at = null)
        {
            var tools = _catalog.Tools;
            if (tools.Count == 0)
            {
                CreateCard(null, at);
                ShowToast("Plugins 폴더에 관리 툴이 없어서 빈 카드를 만들었어요", withUndo: false);
                return;
            }
            var field = new EditField
            {
                Label = "추가할 관리 툴",
                Kind = FieldKind.Choice,
                Options = tools.Select(t => t.DisplayName).Append(BlankCardOption).ToList(),
                Value = tools[0].DisplayName,
            };
            Editor.Show(new EditRequest("새 관리 툴", new[] { field }, 0,
                () => CreateCard(tools.FirstOrDefault(t => t.DisplayName == field.Value), at))
            { SaveText = "추가" });
        }

        private void CreateCard(IBoardTool? tool, Point? at)
        {
            var card = ToolModel.CreateBlank();
            card.ToolId = tool?.Id ?? "";
            card.Title = tool?.DisplayName ?? "";
            var pos = at is { } p
                ? new Point(p.X - card.Width / 2, p.Y - 20)
                : FindFreeSlot(card.Width, card.Height, CardRects());
            card.X = pos.X;
            card.Y = pos.Y;

            _cards.Add(card);
            var view = AddView(card, TimeSpan.Zero);
            if (tool == null) view.BeginInfoEdit(); // an empty card still needs a name
            RevealOnBoard(new Rect(card.X, card.Y, card.Width, card.Height));
            UpdateGroupCounts();
            UpdateChrome();
        }

        private IEnumerable<Rect> CardRects() => _cards.Select(c => new Rect(c.X, c.Y, c.Width, c.Height));

        /// <summary>First grid slot (left→right, top→bottom) of the visible area not overlapping any obstacle.</summary>
        private Point FindFreeSlot(double width, double height, IEnumerable<Rect> obstacles)
        {
            var taken = obstacles.ToList();
            var visible = VisibleBoardRect();
            double left = visible.Left + BoardPadding, top = visible.Top + 20;
            int columns = Math.Max(1, (int)((visible.Width - BoardPadding) / (width + SlotGap)));
            for (int row = 0; row < 50; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    var slot = new Rect(left + col * (width + SlotGap), top + row * (height + SlotGap), width, height);
                    var probe = Rect.Inflate(slot, -20, -20);
                    if (!taken.Any(r => r.IntersectsWith(probe)))
                        return slot.TopLeft;
                }
            }
            int n = _cards.Count % 10;
            return new Point(left + n * 28, top + n * 28);
        }

        private void DeleteCard(ToolCardView view)
        {
            var card = view.Model;
            int index = _cards.IndexOf(card);
            if (index < 0) return;

            _cards.RemoveAt(index);
            _views.Remove(card);
            view.Detach();
            view.PlayExit(() => Board.Children.Remove(view));

            _undo = () =>
            {
                _cards.Insert(Math.Min(index, _cards.Count), card);
                AddView(card, TimeSpan.Zero);
            };
            var name = string.IsNullOrWhiteSpace(card.Title) ? "이름 없는" : $"‘{card.Title}’";
            ShowToast($"{name} 관리 툴을 삭제했어요");
            UpdateGroupCounts();
            UpdateChrome();
        }

        private void Undo()
        {
            if (_undo is not { } undo) return;
            _undo = null;
            undo();
            HideToast();
            UpdateGroupCounts();
            UpdateChrome();
        }

        #region Copy / paste

        // The copied 관리 툴 is kept as a JSON snapshot, so it survives deletes and project switches.
        private string? _copiedCardJson;
        private Point _copiedFrom;  // board position of the original, for pastes without a mouse position
        private int _pasteCount;
        private ToolModel? _selectedCard; // last clicked 관리 툴: the target of Ctrl+C

        private void CopyCard(ToolModel card)
        {
            if (_views.TryGetValue(card, out var source)) source.CaptureState();
            _copiedCardJson = BoardStore.SerializeTool(card);
            _copiedFrom = new Point(card.X, card.Y);
            _pasteCount = 0;
            PasteButton.Visibility = Visibility.Visible;
            var name = string.IsNullOrWhiteSpace(card.Title) ? "이름 없는" : $"‘{card.Title}’";
            ShowToast($"{name} 관리 툴을 복사했어요 · Ctrl+V로 붙여넣기", withUndo: false);
        }

        private void PasteCard()
        {
            if (_copiedCardJson == null || BoardStore.DeserializeTool(_copiedCardJson) is not { } card) return;
            if (!string.IsNullOrWhiteSpace(card.Title)) card.Title += " (복사)";

            // Under the mouse when it is over the board (so it can be dropped straight into a group),
            // otherwise cascaded next to the original.
            _pasteCount++;
            var pos = Viewport.IsMouseOver
                ? (Point)(Mouse.GetPosition(Board) - new Vector(card.Width / 2, 20))
                : _copiedFrom + new Vector(40 * _pasteCount, 40 * _pasteCount);
            card.X = pos.X;
            card.Y = pos.Y;

            _cards.Add(card);
            var view = AddView(card, TimeSpan.Zero);
            _selectedCard = card;
            RevealOnBoard(new Rect(card.X, card.Y, card.Width, card.Height));
            UpdateGroupCounts();
            UpdateChrome();

            _undo = () =>
            {
                if (!_views.ContainsKey(card)) return;
                _cards.Remove(card);
                _views.Remove(card);
                view.Detach();
                view.PlayExit(() => Board.Children.Remove(view));
            };
            ShowToast("관리 툴을 붙여넣었어요");
        }

        private void Paste_Click(object sender, RoutedEventArgs e) => PasteCard();

        #endregion

        private void BringToFront(ToolCardView view)
        {
            if (Panel.GetZIndex(view) == _topZ) return;
            Panel.SetZIndex(view, ++_topZ);
            _cards.Remove(view.Model);
            _cards.Add(view.Model);
        }

        #endregion

        #region Groups

        private GroupView AddGroupView(GroupModel group, TimeSpan enterDelay)
        {
            var view = new GroupView(group);
            view.DeleteRequested += (_, _) => DeleteGroup(view);
            view.EditRequested += (_, request) => Editor.Show(request);
            view.DragStarted += (_, _) => _draggedGroupMembers = MembersOf(group);
            view.Moved += (_, delta) =>
            {
                foreach (var card in _draggedGroupMembers.Cards)
                {
                    card.X += delta.X;
                    card.Y += delta.Y;
                }
                foreach (var child in _draggedGroupMembers.Groups)
                {
                    child.X += delta.X;
                    child.Y += delta.Y;
                }
                // Light up the group this one would be dropped into (never itself or its own children).
                var moving = _draggedGroupMembers.Groups;
                HighlightGroupAt(group.X + group.Width / 2, group.Y + group.Height / 2,
                    g => g != group && !moving.Contains(g) && g.Area > group.Area);
            };
            view.Changed += (_, _) =>
            {
                _draggedGroupMembers = (new(), new());
                HighlightGroupAt(null, null, _ => false);
                RestackGroups();
                UpdateGroupCounts();
            };
            Board.Children.Add(view);
            _groupViews[group] = view;
            RestackGroups();
            view.PlayEnter(enterDelay);
            return view;
        }

        /// <summary>Bigger groups go further back so nested (smaller) groups stay clickable on top.</summary>
        private void RestackGroups()
        {
            var byArea = _groups.OrderByDescending(g => g.Area).ToList();
            for (int i = 0; i < byArea.Count; i++)
                if (_groupViews.TryGetValue(byArea[i], out var v)) Panel.SetZIndex(v, GroupZBase + i);
        }

        /// <summary>Everything that moves with <paramref name="root"/>: nested groups (any depth) and their cards.</summary>
        private (List<ToolModel> Cards, List<GroupModel> Groups) MembersOf(GroupModel root)
        {
            var groups = new List<GroupModel>();
            var pending = new Queue<GroupModel>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                foreach (var child in _groups)
                {
                    if (child == root || groups.Contains(child) || !parent.Contains(child)) continue;
                    groups.Add(child);
                    pending.Enqueue(child);
                }
            }
            var cards = _cards.Where(c => root.Contains(c) || groups.Any(g => g.Contains(c))).ToList();
            return (cards, groups);
        }

        private void AddGroup()
        {
            var group = new GroupModel();
            var obstacles = CardRects().Concat(_groups.Select(g => new Rect(g.X, g.Y, g.Width, g.Height)));
            var pos = FindFreeSlot(group.Width, group.Height, obstacles);
            group.X = pos.X;
            group.Y = pos.Y;

            _groups.Add(group);
            AddGroupView(group, TimeSpan.Zero).EditTitle();
            RevealOnBoard(new Rect(group.X, group.Y, group.Width, group.Height));
            UpdateGroupCounts();
            UpdateChrome();
        }

        /// <summary>Removes only the box; the cards inside stay where they are.</summary>
        private void DeleteGroup(GroupView view)
        {
            var group = view.Model;
            int index = _groups.IndexOf(group);
            if (index < 0) return;

            _groups.RemoveAt(index);
            _groupViews.Remove(group);
            if (_highlightedGroup == view) _highlightedGroup = null;
            view.Detach();
            view.PlayExit(() => Board.Children.Remove(view));

            _undo = () =>
            {
                _groups.Insert(Math.Min(index, _groups.Count), group);
                AddGroupView(group, TimeSpan.Zero);
            };
            var name = string.IsNullOrWhiteSpace(group.Title) ? "이름 없는" : $"‘{group.Title}’";
            ShowToast($"{name} 그룹을 삭제했어요 (관리 툴은 그대로예요)");
            UpdateChrome();
        }

        /// <summary>Highlights the innermost (smallest) eligible group containing the point; null point clears it.</summary>
        private void HighlightGroupAt(double? x, double? y, Func<GroupModel, bool> eligible)
        {
            GroupView? target = null;
            if (x is { } px && y is { } py)
            {
                var group = _groups.Where(g => eligible(g) && g.ContainsPoint(px, py)).MinBy(g => g.Area);
                if (group != null) target = _groupViews[group];
            }
            if (target == _highlightedGroup) return;
            _highlightedGroup?.SetHighlight(false);
            target?.SetHighlight(true);
            _highlightedGroup = target;
        }

        private void HighlightGroupUnder(ToolModel? card)
        {
            if (card == null) HighlightGroupAt(null, null, _ => false);
            else HighlightGroupAt(card.X + card.Width / 2, card.Y + card.Height / 2, _ => true);
        }

        /// <summary>Header counts include everything nested inside each group.</summary>
        private void UpdateGroupCounts()
        {
            foreach (var (group, view) in _groupViews)
            {
                var (cards, groups) = MembersOf(group);
                view.SetMemberCount(cards.Count, groups.Count);
            }
        }

        #endregion

        #region Camera (zoom / pan)

        private Rect VisibleBoardRect() =>
            new(-_panX / _zoom, -_panY / _zoom, Viewport.ActualWidth / _zoom, Viewport.ActualHeight / _zoom);

        private Point ViewportCenter => new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

        /// <summary>Eases the zoom by <paramref name="factor"/>, keeping the board point under <paramref name="screen"/> fixed.</summary>
        private void ZoomAt(Point screen, double factor)
        {
            _zoomAnchorScreen = screen;
            _zoomAnchorBoard = new Point((screen.X - _panX) / _zoom, (screen.Y - _panY) / _zoom);
            // After a far 전체 보기 the zoom can sit below MinZoom; zooming in from there must not snap.
            _targetZoom = Math.Clamp(_targetZoom * factor, Math.Min(MinZoom, _targetZoom), MaxZoom);
            _zoomAnimating = true;
            _flying = false;
            _panVelocity = default;
            _glideTarget = null;
            EnsureCameraTicking();
        }

        /// <summary>Glides the camera so a board rectangle is on screen (centered if it fits). No-op if already visible.</summary>
        private void RevealOnBoard(Rect rect)
        {
            if (VisibleBoardRect().Contains(rect)) return;
            double Axis(double start, double size, double viewport) =>
                size * _zoom <= viewport - 2 * RevealMargin
                    ? viewport / 2 - (start + size / 2) * _zoom
                    : RevealMargin - start * _zoom;
            _glideTarget = new Point(
                Axis(rect.X, rect.Width, Viewport.ActualWidth),
                Axis(rect.Y, rect.Height, Viewport.ActualHeight));
            _panVelocity = default;
            EnsureCameraTicking();
        }

        /// <summary>
        /// 전체 보기: fits every card and group into the viewport (never zooming in past 100%).
        /// An empty board goes back to the origin at 100%.
        /// </summary>
        private void FitAll()
        {
            var rects = CardRects().Concat(_groups.Select(g => new Rect(g.X, g.Y, g.Width, g.Height))).ToList();
            double vw = Viewport.ActualWidth, vh = Viewport.ActualHeight;
            if (vw <= 0 || vh <= 0) return;

            if (rects.Count == 0)
            {
                FlyTo(new Point(vw / 2, vh / 2), 1); // board origin at the top-left again
                return;
            }
            var bounds = rects.Aggregate(Rect.Union);
            double fit = Math.Min((vw - 2 * FitMargin) / bounds.Width, (vh - 2 * FitMargin) / bounds.Height);
            // May go below MinZoom so everything really fits.
            FlyTo(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), Math.Clamp(fit, MinFitZoom, 1));
        }

        /// <summary>Glides the camera so board point <paramref name="center"/> ends up mid-viewport at <paramref name="zoom"/>.</summary>
        private void FlyTo(Point center, double zoom)
        {
            _targetZoom = zoom;
            _flyCenter = new Point((ViewportCenter.X - _panX) / _zoom, (ViewportCenter.Y - _panY) / _zoom);
            _flyTargetCenter = center;
            _flying = true;
            _zoomAnimating = false;
            _glideTarget = null;
            _panVelocity = default;
            EnsureCameraTicking();
        }

        private void FitAll_Click(object sender, RoutedEventArgs e) => FitAll();

        private void EnsureCameraTicking()
        {
            if (_cameraTicking) return;
            _cameraTicking = true;
            _lastCameraFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnCameraFrame;
        }

        private void OnCameraFrame(object? sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _lastCameraFrame) return;
            double dt = _lastCameraFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastCameraFrame).TotalSeconds, 0.05);
            _lastCameraFrame = now;

            if (_flying)
            {
                // Ease zoom and the board point at the viewport center together, so the view
                // glides straight to the target without the content swinging around an anchor.
                double k = 1 - Math.Exp(-FlyStiffness * dt);
                _zoom += (_targetZoom - _zoom) * k;
                _flyCenter += (_flyTargetCenter - _flyCenter) * k;
                if (Math.Abs(_targetZoom - _zoom) < 0.0005 && (_flyTargetCenter - _flyCenter).Length < 0.3)
                {
                    _zoom = _targetZoom;
                    _flyCenter = _flyTargetCenter;
                    _flying = false;
                }
                _panX = ViewportCenter.X - _flyCenter.X * _zoom;
                _panY = ViewportCenter.Y - _flyCenter.Y * _zoom;
            }
            else if (_zoomAnimating)
            {
                _zoom += (_targetZoom - _zoom) * (1 - Math.Exp(-ZoomStiffness * dt));
                if (Math.Abs(_targetZoom - _zoom) < 0.0005)
                {
                    _zoom = _targetZoom;
                    _zoomAnimating = false;
                }
                _panX = _zoomAnchorScreen.X - _zoomAnchorBoard.X * _zoom;
                _panY = _zoomAnchorScreen.Y - _zoomAnchorBoard.Y * _zoom;
            }
            else if (!_panning && _panVelocity != default)
            {
                _panX += _panVelocity.X * dt;
                _panY += _panVelocity.Y * dt;
                _panVelocity *= Math.Exp(-PanFriction * dt);
                if (_panVelocity.Length < MinFlingSpeed) _panVelocity = default;
            }
            else if (!_panning && _glideTarget is { } target)
            {
                double k = 1 - Math.Exp(-GlideStiffness * dt);
                _panX += (target.X - _panX) * k;
                _panY += (target.Y - _panY) * k;
                if (Math.Abs(target.X - _panX) < 0.3 && Math.Abs(target.Y - _panY) < 0.3)
                {
                    (_panX, _panY) = (target.X, target.Y);
                    _glideTarget = null;
                }
            }

            ApplyCamera();

            if (!_flying && !_zoomAnimating && !_panning && _panVelocity == default && _glideTarget == null)
            {
                _cameraTicking = false;
                CompositionTarget.Rendering -= OnCameraFrame;
            }
        }

        private void ApplyCamera()
        {
            BoardScale.ScaleX = BoardScale.ScaleY = _zoom;
            BoardPan.X = _panX;
            BoardPan.Y = _panY;
            ZoomText.Text = $"{Math.Round(_targetZoom * 100)}%";
            // Far zoomed out the dots alias into noise; fade them out between 35% and 15%.
            _gridBrush.Opacity = Math.Clamp((_zoom - 0.15) / 0.2, 0, 1);
        }

        private static bool InToolContent(object? source) =>
            (source as DependencyObject).SelfAndAncestors().Any(x => x is ToolContentHost);

        private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Over a docked tool the plain wheel belongs to the tool (scrolling); Ctrl+wheel still zooms the board.
            if (InToolContent(e.OriginalSource) && Keyboard.Modifiers != ModifierKeys.Control) return;
            ZoomAt(e.GetPosition(Viewport), Math.Pow(ZoomStep, e.Delta / 120.0));
            e.Handled = true;
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource != Viewport && e.OriginalSource != Board) return;
            Board.Focus();

            if (e.ClickCount == 2) AddCard(e.GetPosition(Board));
        }

        /// <summary>Right-button drag pans the board from anywhere (also over cards and groups).</summary>
        private void Viewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // A right-click on a button is that button's own gesture (thumbnail: remove image, folder: open it in
            // Explorer). Starting a pan here would swallow it, so only right-presses elsewhere pan the board.
            if ((e.OriginalSource as DependencyObject).SelfAndAncestors(Viewport).Any(x => x is ButtonBase)) return;
            if (InToolContent(e.OriginalSource)) return; // the tool's own context menu etc.

            if (_zoomAnimating) // finish the zoom so panning starts from a stable camera
            {
                _zoom = _targetZoom;
                _zoomAnimating = false;
                _panX = _zoomAnchorScreen.X - _zoomAnchorBoard.X * _zoom;
                _panY = _zoomAnchorScreen.Y - _zoomAnchorBoard.Y * _zoom;
                ApplyCamera();
            }

            _panning = true;
            _flying = false;
            _panVelocity = default;
            _glideTarget = null;
            _panStartMouse = _lastPanMouse = e.GetPosition(Viewport);
            _panStartOffset = new Point(_panX, _panY);
            _panClock.Restart();
            Mouse.OverrideCursor = Cursors.SizeAll; // also over cards, which set their own cursors
            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(Viewport);
            _panX = _panStartOffset.X + (p.X - _panStartMouse.X);
            _panY = _panStartOffset.Y + (p.Y - _panStartMouse.Y);
            ApplyCamera();

            double dt = _panClock.Elapsed.TotalSeconds;
            if (dt > 0.001)
            {
                _panVelocity = _panVelocity * 0.6 + (p - _lastPanMouse) / dt * 0.4; // smoothed fling speed
                _lastPanMouse = p;
                _panClock.Restart();
            }
        }

        private void Viewport_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_panning) return;
            EndPan();
            e.Handled = true;
        }

        private void Viewport_LostMouseCapture(object sender, MouseEventArgs e) => EndPan();

        private void EndPan()
        {
            if (!_panning) return;
            _panning = false;
            Mouse.OverrideCursor = null;
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();

            if (_panClock.Elapsed.TotalMilliseconds > 80) _panVelocity = default; // pointer had stopped
            if (_panVelocity != default) EnsureCameraTicking();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, ZoomStep);

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, 1 / ZoomStep);

        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, 1 / _targetZoom);

        #endregion

        #region Background color

        private void BackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            var original = _settings.BackgroundColor;
            var field = new EditField
            {
                Label = "색상 (HEX)",
                Kind = FieldKind.Color,
                Swatches = ColorUtil.BackgroundSwatches,
                Value = original,
                Preview = v => ApplyBackground(v, animate: true),
            };
            Editor.Show(new EditRequest("배경 색상", new[] { field }, 0,
                Save: () =>
                {
                    if (!ColorUtil.TryParse(field.Value, out var c))
                    {
                        ApplyBackground(original, animate: true);
                        return;
                    }
                    _settings.BackgroundColor = ColorUtil.ToHex(c);
                    BoardStore.TrySaveSettings(_settings);
                },
                Cancel: () => ApplyBackground(original, animate: true)));
        }

        /// <summary>The board's dot grid follows the theme (white dots on dark, ink dots on light).</summary>
        private void ApplyDotColor()
        {
            if (_gridBrush?.Drawing is DrawingGroup group && group.Children.Count > 1 && group.Children[1] is GeometryDrawing dots)
            {
                var fg = AppTheme.Foreground;
                dots.Brush = new SolidColorBrush(Color.FromArgb(0x1C, fg.R, fg.G, fg.B));
            }
        }

        /// <summary>Diagonal gradient around the chosen color (lighter top-left, darker bottom-right).</summary>
        private void ApplyBackground(string hex, bool animate)
        {
            if (!ColorUtil.TryParse(hex, out var baseColor)) return;
            // Light boards get the ink theme (dark text, dark overlays); dark boards keep white text. The choice is
            // whichever has the better contrast, so the board color itself is always shown as picked.
            bool light = ColorUtil.PrefersDarkText(baseColor);
            AppTheme.Apply(light);
            ApplyDotColor();
            var top = ColorUtil.Shade(baseColor, 0.06);
            var bottom = ColorUtil.Shade(baseColor, light ? -0.10 : -0.18);
            var swatch = (SolidColorBrush)BackgroundSwatch.Fill;
            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(350);
                _bgTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(top, dur));
                _bgBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(bottom, dur));
                swatch.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(baseColor, dur));
            }
            else
            {
                _bgTop.Color = top;
                _bgBottom.Color = bottom;
                swatch.Color = baseColor;
            }
        }

        #endregion

        #region Chrome

        private void UpdateChrome()
        {
            SubtitleText.Text = (_cards.Count == 0 ? "관리 툴 없음" : $"관리 툴 {_cards.Count}개") + (_groups.Count > 0 ? $" · 그룹 {_groups.Count}개" : "");
            EmptyHint.BeginAnimation(OpacityProperty,
                new DoubleAnimation(_cards.Count == 0 && _groups.Count == 0 ? 1 : 0, TimeSpan.FromMilliseconds(250)));
        }

        private void ShowToast(string message, bool withUndo = true)
        {
            ToastText.Text = message;
            ToastUndoButton.Visibility = withUndo ? Visibility.Visible : Visibility.Collapsed;
            Toast.IsHitTestVisible = true;
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            ToastShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            Toast.IsHitTestVisible = false;
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            ToastShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, TimeSpan.FromMilliseconds(200)));
        }

        #endregion

        #region Input

        private void AddCard_Click(object sender, RoutedEventArgs e) => AddCard();

        private void AddGroup_Click(object sender, RoutedEventArgs e) => AddGroup();

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (Editor.IsOpen) return;
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            {
                SaveAs();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.S || e.Key == Key.O)
            {
                if (e.Key == Key.S) Save(); else OpenFile();
                e.Handled = true;
                return;
            }
            if (InToolContent(Keyboard.FocusedElement)) return; // shortcuts inside a tool belong to the tool
            if (e.Key == Key.Z && _undo != null && Keyboard.FocusedElement is not TextBoxBase)
            {
                Undo();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.N:
                    AddCard();
                    break;
                case Key.C when _selectedCard != null && _views.ContainsKey(_selectedCard)
                                && Keyboard.FocusedElement is not TextBoxBase:
                    CopyCard(_selectedCard);
                    break;
                case Key.V when _copiedCardJson != null && Keyboard.FocusedElement is not TextBoxBase:
                    PasteCard();
                    break;
                case Key.OemPlus or Key.Add:
                    ZoomAt(ViewportCenter, ZoomStep);
                    break;
                case Key.OemMinus or Key.Subtract:
                    ZoomAt(ViewportCenter, 1 / ZoomStep);
                    break;
                case Key.D1 or Key.NumPad1:
                    FitAll();
                    break;
                case Key.G:
                    SetSnapMove(!_settings.SnapMove);
                    break;
                case Key.B:
                    ToggleLeftPanel();
                    break;
                case Key.D0 or Key.NumPad0:
                    ZoomAt(ViewportCenter, 1 / _targetZoom);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        #endregion
    }
}
