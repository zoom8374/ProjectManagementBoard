using System;
using System.Text.Json;
using ProjectManagementBoard.Contracts;

namespace MemoTool
{
    /// <summary>Entry point the board discovers in Plugins\MemoTool\MemoTool.dll.</summary>
    public class MemoBoardTool : IBoardTool, IBoardToolFileExtension
    {
        public string Id => "memo";                 // saved in project files: never change it
        public string DisplayName => "메모장";
        public string Description => "간단한 텍스트 메모";

        public string FileExtension => ".pmmo";      // individually saved files ('관리 툴 개별 저장'); never change it

        public IBoardToolInstance CreateInstance() => new MemoInstance();

        private sealed class MemoInstance : IBoardToolInstance, IBoardToolFontSize
        {
            // The state is the plain memo text. Only when the font size differs from the default is it stored as
            // Marker + JSON, so untouched memos keep the old format (and old states without the marker still load).
            private const string Marker = "\u0001pmmo1\u0001";
            private const double DefaultSize = 19, MinSize = 9, MaxSize = 48;

            private readonly MemoView _view = new();

            public System.Windows.FrameworkElement View => _view;

            public string SaveState() =>
                Math.Abs(_view.Editor.FontSize - DefaultSize) < 0.01
                    ? _view.Editor.Text
                    : Marker + JsonSerializer.Serialize(new MemoState { Text = _view.Editor.Text, FontSize = _view.Editor.FontSize });

            public void LoadState(string state)
            {
                if (state.StartsWith(Marker, StringComparison.Ordinal))
                {
                    try
                    {
                        var s = JsonSerializer.Deserialize<MemoState>(state.Substring(Marker.Length));
                        if (s != null)
                        {
                            _view.Editor.Text = s.Text ?? "";
                            _view.Editor.FontSize = Math.Clamp(s.FontSize, MinSize, MaxSize);
                            return;
                        }
                    }
                    catch (JsonException) { }
                }
                _view.Editor.Text = state;
            }

            public void ChangeFontSize(int step) =>
                _view.Editor.FontSize = Math.Clamp(Math.Round(_view.Editor.FontSize) + step, MinSize, MaxSize);
        }

        private sealed class MemoState
        {
            public string? Text { get; set; }
            public double FontSize { get; set; } = 19;
        }
    }
}
