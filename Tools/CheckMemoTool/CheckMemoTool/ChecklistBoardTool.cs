using System;
using System.Text.Json;
using System.Windows;
using CheckMemoTool.Models;
using CheckMemoTool.ViewModels;
using CheckMemoTool.Views;
using ProjectManagementBoard.Contracts;

namespace CheckMemoTool
{
    /// <summary>Entry point the board discovers in Plugins\CheckMemoTool\CheckMemoTool.dll. Port of the standalone CheckMemo floating checklist.</summary>
    public class ChecklistBoardTool : IBoardTool, IBoardToolFileExtension
    {
        public string Id => "checkmemo";              // saved in project files: never change it
        public string DisplayName => "체크리스트";
        public string Description => "중요도·완료 구분이 있는 체크리스트 (CheckMemo)";

        public string FileExtension => ".pmck";      // individually saved files ('관리 툴 개별 저장'); never change it

        public IBoardToolInstance CreateInstance() => new Instance();

        private sealed class Instance : IBoardToolInstance, IBoardToolFontSize
        {
            public void ChangeFontSize(int step)
            {
                if (step > 0) _vm.Settings.IncreaseFont(); else if (step < 0) _vm.Settings.DecreaseFont();
            }

            private ChecklistViewModel _vm = new ChecklistViewModel(new AppSettingsViewModel());
            private ChecklistView _view;

            public Instance()
            {
                _view = new ChecklistView(_vm);
            }

            public FrameworkElement View => _view;

            public string SaveState()
            {
                _view.CommitPendingEdits();
                return JsonSerializer.Serialize(_vm.ToState());
            }

            public void LoadState(string state)
            {
                ChecklistState parsed;
                try { parsed = JsonSerializer.Deserialize<ChecklistState>(state); }
                catch (JsonException) { return; }   // unreadable state: keep the empty list
                if (parsed == null) return;

                _vm = ChecklistViewModel.FromState(parsed);
                _view = new ChecklistView(_vm);
            }
        }
    }
}
