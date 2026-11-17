using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CheckMemoTool.Models;
using CheckMemoTool.Services;

namespace CheckMemoTool.ViewModels
{
    // 리스트 창 하나 (PRD 4장 F-01 ~ F-22)
    public class ChecklistViewModel : ObservableObject
    {
        private List<ItemViewModel> _lastDeleted;

        // 저장이 필요한 변경이 생기면 발생
        public event EventHandler Changed;

        public AppSettingsViewModel Settings { get; private set; }

        // 미완료: 상 → 중 → 하 → 없음, 그룹 안에서는 Order 순
        public ObservableCollection<ItemViewModel> OpenItems { get; private set; }

        // 완료: 최근 완료가 위
        public ObservableCollection<ItemViewModel> DoneItems { get; private set; }

        public RelayCommand ToggleDoneCommand { get; private set; }
        public RelayCommand DeleteItemCommand { get; private set; }

        public ChecklistViewModel(AppSettingsViewModel settings)
        {
            Settings = settings;
            settings.Changed += (s, e) => RaiseChanged();
            OpenItems = new ObservableCollection<ItemViewModel>();
            DoneItems = new ObservableCollection<ItemViewModel>();
            DoneItems.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged("DoneCount");
                OnPropertyChanged("HasDone");
            };
            ToggleDoneCommand = new RelayCommand(p => ToggleDone(p as ItemViewModel));
            DeleteItemCommand = new RelayCommand(p => DeleteItem(p as ItemViewModel));
        }

        public int DoneCount
        {
            get { return DoneItems.Count; }
        }

        public bool HasDone
        {
            get { return DoneItems.Count > 0; }
        }

        public bool CanUndo
        {
            get { return _lastDeleted != null && _lastDeleted.Count > 0; }
        }

        // ---------- 항목 추가·수정 (F-04, F-05) ----------

        // 중요도 '없음'으로 미완료 영역 맨 아래에 추가. 빈 문자열은 무시
        public ItemViewModel AddItem(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0) return null;

            ItemViewModel item = ItemViewModel.Create(value);
            item.Order = NextOrder(null);
            OpenItems.Add(item);
            Resort();
            RaiseChanged();
            return item;
        }

        public void BeginEdit(ItemViewModel item)
        {
            if (item == null) return;
            item.EditText = item.Text;
            item.IsEditing = true;
        }

        public void CommitEdit(ItemViewModel item)
        {
            if (item == null || !item.IsEditing) return;
            string text = (item.EditText ?? string.Empty).Trim();
            item.IsEditing = false;
            if (text.Length > 0 && text != item.Text)
            {
                item.Text = text;
                RaiseChanged();
            }
        }

        public void CancelEdit(ItemViewModel item)
        {
            if (item == null) return;
            item.IsEditing = false;
        }

        // ---------- 완료 (F-06, F-07, F-08) ----------

        public void ToggleDone(ItemViewModel item)
        {
            if (item == null) return;
            if (item.IsEditing) CommitEdit(item);

            if (!item.IsDone)
            {
                OpenItems.Remove(item);
                item.IsDone = true;
                item.DoneAt = TimeText.Now();
                DoneItems.Insert(0, item);
                NormalizeOrders();
            }
            else
            {
                DoneItems.Remove(item);
                item.IsDone = false;
                item.DoneAt = null;
                item.Order = NextOrder(item.Priority);   // 자기 중요도 그룹 맨 아래
                OpenItems.Add(item);
                Resort();
            }
            RaiseChanged();
        }

        // ---------- 중요도 (F-20, F-21, F-22) ----------

        public void SetPriority(ItemViewModel item, string priority)
        {
            if (item == null) return;
            string p = Priority.Normalize(priority);
            if (item.Priority == p) return;

            if (!item.IsDone)
            {
                item.Order = NextOrder(p);   // 새 그룹 맨 아래
                item.Priority = p;
                Resort();
            }
            else
            {
                item.Priority = p;           // 완료 영역은 완료 시각순 유지
            }
            RaiseChanged();
        }

        // ---------- 삭제·되돌리기 (F-09, F-10, F-11) ----------

        public void DeleteItem(ItemViewModel item)
        {
            if (item == null) return;
            bool removed = OpenItems.Remove(item) || DoneItems.Remove(item);
            if (!removed) return;
            item.IsEditing = false;
            _lastDeleted = new List<ItemViewModel> { item };
            NormalizeOrders();
            OnPropertyChanged("CanUndo");
            RaiseChanged();
        }

        public void DeleteAllDone()
        {
            if (DoneItems.Count == 0) return;
            _lastDeleted = DoneItems.ToList();
            DoneItems.Clear();
            OnPropertyChanged("CanUndo");
            RaiseChanged();
        }

        // 마지막 삭제 1건(일괄 삭제면 그 묶음) 복원. 앱 실행 중에만 유지
        public bool UndoDelete()
        {
            if (!CanUndo) return false;
            foreach (ItemViewModel item in _lastDeleted)
            {
                if (item.IsDone) DoneItems.Add(item);
                else OpenItems.Add(item);
            }
            _lastDeleted = null;
            Resort();
            SortDone();
            OnPropertyChanged("CanUndo");
            RaiseChanged();
            return true;
        }

        // ---------- 순서 변경 (F-12, F-13): 같은 중요도 그룹 안에서만 ----------

        // insertIndex: OpenItems 기준 "이 위치 앞에 넣기" (0 ~ Count)
        public bool CanMoveTo(ItemViewModel item, int insertIndex)
        {
            if (item == null || item.IsDone) return false;
            if (!OpenItems.Contains(item)) return false;   // 다른 창의 항목은 받지 않음
            int start, end;
            if (!GetGroupRange(item.PriorityRank, out start, out end)) return false;
            return insertIndex >= start && insertIndex <= end + 1;
        }

        public bool MoveWithinGroup(ItemViewModel item, int insertIndex)
        {
            if (!CanMoveTo(item, insertIndex)) return false;
            int from = OpenItems.IndexOf(item);
            int to = insertIndex > from ? insertIndex - 1 : insertIndex;
            if (to == from) return false;
            OpenItems.Move(from, to);
            NormalizeOrders();
            RaiseChanged();
            return true;
        }

        // Alt+↑ (-1) / Alt+↓ (+1)
        public bool MoveByOffset(ItemViewModel item, int offset)
        {
            if (item == null || item.IsDone) return false;
            int from = OpenItems.IndexOf(item);
            if (from < 0) return false;
            int start, end;
            if (!GetGroupRange(item.PriorityRank, out start, out end)) return false;
            int to = from + offset;
            if (to < start || to > end) return false;
            OpenItems.Move(from, to);
            NormalizeOrders();
            RaiseChanged();
            return true;
        }

        // ---------- 정렬 ----------

        private bool GetGroupRange(int rank, out int start, out int end)
        {
            start = -1;
            end = -1;
            for (int i = 0; i < OpenItems.Count; i++)
            {
                if (OpenItems[i].PriorityRank != rank) continue;
                if (start < 0) start = i;
                end = i;
            }
            return start >= 0;
        }

        private int NextOrder(string priority)
        {
            int rank = Priority.Rank(priority);
            int max = -1;
            foreach (ItemViewModel i in OpenItems)
            {
                if (i.PriorityRank == rank && i.Order > max) max = i.Order;
            }
            return max + 1;
        }

        // 미완료 영역을 중요도 → Order 순으로 제자리 정렬 (컨테이너를 유지하려고 Move 사용)
        private void Resort()
        {
            List<ItemViewModel> sorted = OpenItems
                .OrderBy(i => i.PriorityRank)
                .ThenBy(i => i.Order)
                .ToList();
            MoveInto(OpenItems, sorted);
            NormalizeOrders();
        }

        private void SortDone()
        {
            List<ItemViewModel> sorted = DoneItems
                .OrderByDescending(i => i.DoneTime)
                .ToList();
            MoveInto(DoneItems, sorted);
        }

        private static void MoveInto(ObservableCollection<ItemViewModel> target, List<ItemViewModel> sorted)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                int current = target.IndexOf(sorted[i]);
                if (current != i) target.Move(current, i);
            }
        }

        // 그룹마다 0, 1, 2 … 로 다시 매김
        private void NormalizeOrders()
        {
            var counters = new Dictionary<int, int>();
            foreach (ItemViewModel item in OpenItems)
            {
                int n;
                counters.TryGetValue(item.PriorityRank, out n);
                item.Order = n;
                counters[item.PriorityRank] = n + 1;
            }
        }

        // ---------- state (SaveState / LoadState of the board tool) ----------

        public static ChecklistViewModel FromState(ChecklistState state)
        {
            var settings = new AppSettingsViewModel();
            settings.FontSize = state.FontSize;
            var vm = new ChecklistViewModel(settings);
            foreach (ItemData d in state.Items ?? new List<ItemData>())
            {
                ItemViewModel item = ItemViewModel.FromData(d);
                if (item.IsDone) vm.DoneItems.Add(item);
                else vm.OpenItems.Add(item);
            }
            vm.Resort();
            vm.SortDone();
            return vm;
        }

        public ChecklistState ToState()
        {
            var state = new ChecklistState { FontSize = Settings.FontSize };
            foreach (ItemViewModel i in OpenItems) state.Items.Add(i.ToData());
            foreach (ItemViewModel i in DoneItems) state.Items.Add(i.ToData());
            return state;
        }

        public void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
