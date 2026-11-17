using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CheckMemoTool.ViewModels;

namespace CheckMemoTool.Views
{
    public partial class ChecklistView : UserControl
    {
        private const string DragFormat = "CheckMemoTool.Item";

        private readonly ChecklistViewModel _vm;

        private ItemViewModel _priorityTarget;
        private DateTime _menuClosedAt = DateTime.MinValue;

        private ItemViewModel _dragItem;
        private Point _dragStart;
        private bool _dragging;
        private InsertionAdorner _adorner;

        public ChecklistView(ChecklistViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            MenuPopup.Closed += (s, e) => _menuClosedAt = DateTime.Now;
        }

        // Writes any open inline edit back to the model (called before the state is saved).
        public void CommitPendingEdits()
        {
            foreach (ItemViewModel item in _vm.OpenItems.Concat(_vm.DoneItems).ToList())
            {
                if (item.IsEditing) _vm.CommitEdit(item);
            }
        }

        // ---------- 단축키 (F-11, F-14, F-15) ----------

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!ctrl) return;

            switch (e.Key)
            {
                case Key.OemPlus:
                case Key.Add:
                    _vm.Settings.IncreaseFont();
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    _vm.Settings.DecreaseFont();
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    _vm.Settings.ResetFont();
                    e.Handled = true;
                    break;
                case Key.Z:
                    if (CanUseItemUndo() && _vm.UndoDelete()) e.Handled = true;
                    break;
            }
        }

        // 글자 편집 중에는 TextBox 의 되돌리기를 우선
        private bool CanUseItemUndo()
        {
            var tb = Keyboard.FocusedElement as TextBox;
            if (tb == null) return true;
            return tb == InputBox && InputBox.Text.Length == 0;
        }

        // ---------- ⋯ 메뉴 ----------

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            // 열린 메뉴에서 ⋯ 를 다시 누르면 닫히기만 하도록
            if ((DateTime.Now - _menuClosedAt).TotalMilliseconds < 250) return;
            MenuPopup.PlacementTarget = RootGrid;
            MenuPopup.HorizontalOffset = Math.Max(0, RootGrid.ActualWidth - 218);
            MenuPopup.VerticalOffset = 36;
            MenuPopup.IsOpen = true;
        }

        private void FontUp_Click(object sender, RoutedEventArgs e)
        {
            _vm.Settings.IncreaseFont();
        }

        private void FontDown_Click(object sender, RoutedEventArgs e)
        {
            _vm.Settings.DecreaseFont();
        }

        // ---------- 입력란 (F-04) ----------

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ItemViewModel added = _vm.AddItem(InputBox.Text);
            if (added != null)
            {
                InputBox.Clear();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var container = OpenList.ItemContainerGenerator.ContainerFromItem(added) as FrameworkElement;
                    if (container != null) container.BringIntoView();
                }), DispatcherPriority.Loaded);
            }
            e.Handled = true;
        }

        // ---------- 항목 줄 ----------

        private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = sender as FrameworkElement;
            var item = row != null ? row.DataContext as ItemViewModel : null;
            if (item == null || item.IsEditing) return;

            if (e.ClickCount == 2)
            {
                BeginEdit(item);   // F-05 더블클릭 편집
                e.Handled = true;
                return;
            }
            row.Focus();
        }

        private void Row_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            var row = sender as FrameworkElement;
            var item = row != null ? row.DataContext as ItemViewModel : null;
            if (item == null) return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;
            bool alt = (mods & ModifierKeys.Alt) == ModifierKeys.Alt;

            if (alt && (key == Key.Up || key == Key.Down))
            {
                // F-13 같은 중요도 안에서 순서 이동
                if (_vm.MoveByOffset(item, key == Key.Up ? -1 : 1)) FocusRowLater(item);
                e.Handled = true;
            }
            else if (mods == ModifierKeys.None && key == Key.Space)
            {
                _vm.ToggleDone(item);  // F-06
                FocusRowLater(item);
                e.Handled = true;
            }
            else if (mods == ModifierKeys.None && key == Key.Delete)
            {
                ItemViewModel next = Neighbor(item, 1) ?? Neighbor(item, -1);
                _vm.DeleteItem(item);  // F-09
                if (next != null) FocusRowLater(next);
                else InputBox.Focus();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.None && key == Key.F2)
            {
                BeginEdit(item);   // F-05
                e.Handled = true;
            }
            else if (mods == ModifierKeys.None && (key == Key.Up || key == Key.Down))
            {
                ItemViewModel target = Neighbor(item, key == Key.Up ? -1 : 1);
                if (target != null) FocusRow(target);
                e.Handled = true;
            }
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as FrameworkElement;
            var item = box != null ? box.DataContext as ItemViewModel : null;
            if (item == null) return;

            if (e.Key == Key.Enter)
            {
                _vm.CommitEdit(item);
                FocusRowLater(item);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _vm.CancelEdit(item);
                FocusRowLater(item);
                e.Handled = true;
            }
        }

        private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var box = sender as FrameworkElement;
            var item = box != null ? box.DataContext as ItemViewModel : null;
            if (item != null && item.IsEditing) _vm.CommitEdit(item);
        }

        // 항목 편집 시작: 편집 상자에 포커스 + 전체 선택
        private void BeginEdit(ItemViewModel item)
        {
            _vm.BeginEdit(item);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TextBox box = FindInRow<TextBox>(item, "EditBox");
                if (box == null) return;
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }), DispatcherPriority.Input);
        }

        // ---------- 중요도 선택 (F-20) ----------

        private void Dot_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            _priorityTarget = button.DataContext as ItemViewModel;
            if (_priorityTarget == null) return;
            PriorityPopup.PlacementTarget = button;
            PriorityPopup.HorizontalOffset = -170;
            PriorityPopup.VerticalOffset = 2;
            PriorityPopup.IsOpen = true;
        }

        private void PriorityOption_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string tag = button != null ? button.Tag as string : null;
            string priority = tag == "none" ? null : tag;
            ItemViewModel target = _priorityTarget;
            PriorityPopup.IsOpen = false;
            _priorityTarget = null;
            if (target != null) _vm.SetPriority(target, priority);
        }

        // ---------- 완료 영역 (F-10) ----------

        private void ClearDone_Click(object sender, RoutedEventArgs e)
        {
            int count = _vm.DoneCount;
            if (count == 0) return;
            string message = string.Format("완료 항목 {0}개를 모두 삭제할까요?\n(Ctrl+Z로 되돌릴 수 있습니다)", count);
            MessageBoxResult result = MessageBox.Show(Window.GetWindow(this), message, "체크리스트", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result == MessageBoxResult.Yes) _vm.DeleteAllDone();
        }

        // ---------- 드래그 순서 변경 (F-12): 같은 중요도 안에서만 ----------

        private void Handle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var handle = sender as FrameworkElement;
            var item = handle != null ? handle.DataContext as ItemViewModel : null;
            if (item == null || item.IsDone) return;
            _dragItem = item;
            _dragStart = e.GetPosition(this);
            e.Handled = true;
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || _dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragItem = null;
                return;
            }
            Point pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            ItemViewModel item = _dragItem;
            var container = OpenList.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
            _dragging = true;
            try
            {
                DragDrop.DoDragDrop(container ?? OpenList, new DataObject(DragFormat, item), DragDropEffects.Move);
            }
            finally
            {
                _dragging = false;
                _dragItem = null;
                HideAdorner();
            }
        }

        private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) _dragItem = null;
        }

        private void OpenList_DragOver(object sender, DragEventArgs e)
        {
            var item = e.Data.GetDataPresent(DragFormat) ? e.Data.GetData(DragFormat) as ItemViewModel : null;
            if (item == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            AutoScroll(e.GetPosition(Scroller));

            double lineY;
            int index = ComputeInsertIndex(e.GetPosition(OpenList), out lineY);
            if (_vm.CanMoveTo(item, index))
            {
                e.Effects = DragDropEffects.Move;
                ShowAdorner(lineY);
            }
            else
            {
                // 다른 중요도 위치: 금지 커서
                e.Effects = DragDropEffects.None;
                HideAdorner();
            }
            e.Handled = true;
        }

        private void OpenList_DragLeave(object sender, DragEventArgs e)
        {
            Point p = e.GetPosition(OpenList);
            bool inside = p.X >= 0 && p.Y >= 0 && p.X <= OpenList.ActualWidth && p.Y <= OpenList.ActualHeight;
            if (!inside) HideAdorner();
        }

        private void OpenList_Drop(object sender, DragEventArgs e)
        {
            var item = e.Data.GetDataPresent(DragFormat) ? e.Data.GetData(DragFormat) as ItemViewModel : null;
            HideAdorner();
            if (item == null) return;
            double lineY;
            int index = ComputeInsertIndex(e.GetPosition(OpenList), out lineY);
            if (_vm.MoveWithinGroup(item, index)) FocusRowLater(item);
            e.Handled = true;
        }

        // 마우스 위치 → OpenItems 기준 "이 앞에 넣기" 위치
        private int ComputeInsertIndex(Point pos, out double lineY)
        {
            lineY = 0;
            int count = _vm.OpenItems.Count;
            for (int i = 0; i < count; i++)
            {
                var c = OpenList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (c == null) continue;
                Point top = c.TranslatePoint(new Point(0, 0), OpenList);
                double h = c.ActualHeight;
                if (pos.Y < top.Y + h / 2)
                {
                    lineY = top.Y;
                    return i;
                }
                lineY = top.Y + h;
            }
            return count;
        }

        private void AutoScroll(Point posInScroller)
        {
            const double edge = 24;
            if (posInScroller.Y < edge) Scroller.LineUp();
            else if (posInScroller.Y > Scroller.ActualHeight - edge) Scroller.LineDown();
        }

        private void ShowAdorner(double y)
        {
            if (_adorner == null)
            {
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(OpenList);
                if (layer == null) return;
                _adorner = new InsertionAdorner(OpenList);
                layer.Add(_adorner);
            }
            _adorner.Y = y;
        }

        private void HideAdorner()
        {
            if (_adorner == null) return;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(OpenList);
            if (layer != null) layer.Remove(_adorner);
            _adorner = null;
        }

        // ---------- 포커스 이동 ----------

        private ItemViewModel Neighbor(ItemViewModel item, int offset)
        {
            var all = _vm.OpenItems.Concat(_vm.DoneItems).ToList();
            int index = all.IndexOf(item);
            if (index < 0) return null;
            int target = index + offset;
            if (target < 0 || target >= all.Count) return null;
            return all[target];
        }

        private void FocusRowLater(ItemViewModel item)
        {
            Dispatcher.BeginInvoke(new Action(() => FocusRow(item)), DispatcherPriority.Loaded);
        }

        private void FocusRow(ItemViewModel item)
        {
            FrameworkElement row = FindInRow<FrameworkElement>(item, "Row");
            if (row == null) return;
            row.Focus();
            row.BringIntoView();
        }

        // 항목 템플릿 안의 이름 붙은 요소 찾기
        private T FindInRow<T>(ItemViewModel item, string name) where T : class
        {
            ItemsControl list = item.IsDone ? DoneList : OpenList;
            var presenter = list.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
            if (presenter == null) return null;
            presenter.ApplyTemplate();
            DataTemplate template = presenter.ContentTemplate;
            if (template == null) return null;
            return template.FindName(name, presenter) as T;
        }
    }
}
