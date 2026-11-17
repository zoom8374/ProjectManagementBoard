using ProjectManagementBoard.Contracts;

namespace MemoTool
{
    /// <summary>Entry point the board discovers in Plugins\MemoTool\MemoTool.dll.</summary>
    public class MemoBoardTool : IBoardTool
    {
        public string Id => "memo";                 // saved in project files: never change it
        public string DisplayName => "메모장";
        public string Description => "간단한 텍스트 메모";

        public IBoardToolInstance CreateInstance() => new MemoInstance();

        private sealed class MemoInstance : IBoardToolInstance
        {
            private readonly MemoView _view = new();

            public System.Windows.FrameworkElement View => _view;
            public string SaveState() => _view.Editor.Text;
            public void LoadState(string state) => _view.Editor.Text = state;
        }
    }
}
