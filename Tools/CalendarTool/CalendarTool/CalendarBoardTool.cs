using System.Text.Json;
using System.Windows;
using ProjectManagementBoard.Contracts;

namespace CalendarTool
{
    /// <summary>
    /// Entry point the board discovers in Plugins\CalendarTool\CalendarTool.dll. Port of the standalone ProjectCalendar
    /// (goals, coloured dates, done checks, memos, schedule sheets) without server, login, file, opacity or window features.
    /// </summary>
    public class CalendarBoardTool : IBoardTool, IBoardToolDefaultSize, IBoardToolFileExtension
    {
        public string Id => "calendar";               // saved in project files: never change it
        public string DisplayName => "캘린더";
        public string Description => "목표별 일정 색칠, 완료 체크, 날짜 메모, 여러 일정표 (ProjectCalendar)";

        // The month grid (8 weeks) plus the goal panel need room; the board's default card would be cramped.
        public double DefaultWidth => 1100;
        public double DefaultHeight => 760;

        public string FileExtension => ".pmcd";      // individually saved files ('관리 툴 개별 저장'); never change it

        public IBoardToolInstance CreateInstance() => new Instance();

        private sealed class Instance : IBoardToolInstance, IBoardToolResizeRequest
        {
            private static readonly JsonSerializerOptions Options = new();
            private CalendarView _view = null!;

            public Instance() => Attach(new CalendarView(new CalendarState()));

            /// <summary>Opening / closing the Schedule panel grows / shrinks the card by the panel's height.</summary>
            public event EventHandler<Vector>? ResizeRequested;

            private void Attach(CalendarView view)
            {
                _view = view;
                view.HeightChangeRequested += dh => ResizeRequested?.Invoke(this, new Vector(0, dh));
            }

            public FrameworkElement View => _view;

            public string SaveState() => JsonSerializer.Serialize(_view.Snapshot(), Options);

            public void LoadState(string state)
            {
                CalendarState? parsed;
                try { parsed = JsonSerializer.Deserialize<CalendarState>(state, Options); }
                catch (JsonException) { return; }   // unreadable state: keep the fresh calendar
                if (parsed != null) Attach(new CalendarView(parsed));
            }
        }
    }
}
