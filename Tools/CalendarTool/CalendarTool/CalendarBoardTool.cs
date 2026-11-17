using System.Text.Json;
using System.Windows;
using ProjectManagementBoard.Contracts;

namespace CalendarTool
{
    /// <summary>
    /// Entry point the board discovers in Plugins\CalendarTool\CalendarTool.dll. Port of the standalone ProjectCalendar
    /// (goals, coloured dates, done checks, memos, schedule sheets) without server, login, file, opacity or window features.
    /// </summary>
    public class CalendarBoardTool : IBoardTool, IBoardToolDefaultSize
    {
        public string Id => "calendar";               // saved in project files: never change it
        public string DisplayName => "캘린더";
        public string Description => "목표별 일정 색칠, 완료 체크, 날짜 메모, 여러 일정표 (ProjectCalendar)";

        // The month grid (8 weeks) plus the goal panel need room; the board's default card would be cramped.
        public double DefaultWidth => 1100;
        public double DefaultHeight => 760;

        public IBoardToolInstance CreateInstance() => new Instance();

        private sealed class Instance : IBoardToolInstance
        {
            private static readonly JsonSerializerOptions Options = new();
            private CalendarView _view = new(new CalendarState());

            public FrameworkElement View => _view;

            public string SaveState() => JsonSerializer.Serialize(_view.Snapshot(), Options);

            public void LoadState(string state)
            {
                CalendarState? parsed;
                try { parsed = JsonSerializer.Deserialize<CalendarState>(state, Options); }
                catch (JsonException) { return; }   // unreadable state: keep the fresh calendar
                if (parsed != null) _view = new CalendarView(parsed);
            }
        }
    }
}
