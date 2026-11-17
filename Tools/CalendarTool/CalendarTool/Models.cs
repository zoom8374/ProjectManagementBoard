using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace CalendarTool
{
    /// <summary>A big-picture goal (e.g. "개발") that date cells can be colored with.</summary>
    public class Goal : INotifyPropertyChanged
    {
        string _name = "";
        string _hex = "#4F8EF7";
        int _dayCount;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; On(); }
        }

        public string Hex
        {
            get => _hex;
            set
            {
                if (_hex == value) return;
                _hex = value;
                On(); On(nameof(Brush)); On(nameof(Rgb));
            }
        }

        [JsonIgnore]
        public int DayCount
        {
            get => _dayCount;
            set { if (_dayCount == value) return; _dayCount = value; On(); }
        }

        string _summary = "";

        /// <summary>Date ranges assigned to this goal, one per line, e.g. "11/3~11/9 : 7일".</summary>
        [JsonIgnore]
        public string Summary
        {
            get => _summary;
            set { if (_summary == value) return; _summary = value; On(); }
        }

        [JsonIgnore]
        public Color Rgb => (Color)ColorConverter.ConvertFromString(_hex);

        [JsonIgnore]
        public Brush Brush
        {
            get { var b = new SolidColorBrush(Rgb); b.Freeze(); return b; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void On([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>One schedule sheet (tab): its own goals and per-date colour assignments.</summary>
    public class Sheet
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "일정표";
        /// <summary>Optional tab accent colour (#RRGGBB).</summary>
        public string? ColorHex { get; set; }
        public List<Goal> Goals { get; set; } = new();
        /// <summary>"yyyy-MM-dd" -> goal id. One goal per date.</summary>
        public Dictionary<string, Guid> Days { get; set; } = new();
        /// <summary>Second (later-added) goal of a date; a date holds at most two goals (Days + Days2).</summary>
        public Dictionary<string, Guid> Days2 { get; set; } = new();
        /// <summary>"yyyy-MM-dd" -> ids of the goals checked done on that date.</summary>
        public Dictionary<string, List<Guid>> Done { get; set; } = new();
        /// <summary>"yyyy-MM-dd" -> memo text.</summary>
        public Dictionary<string, string> Notes { get; set; } = new();

        /// <summary>Deep copy with fresh goal ids (day assignments are remapped).</summary>
        public Sheet Clone(string name, bool keepDays)
        {
            var map = new Dictionary<Guid, Guid>();
            var copy = new Sheet { Name = name, ColorHex = ColorHex };
            foreach (var g in Goals)
            {
                var ng = new Goal { Name = g.Name, Hex = g.Hex };
                map[g.Id] = ng.Id;
                copy.Goals.Add(ng);
            }
            if (keepDays)
                foreach (var kv in Days)
                    if (map.TryGetValue(kv.Value, out var nid)) copy.Days[kv.Key] = nid;
            if (keepDays)
                foreach (var kv in Days2)
                    if (map.TryGetValue(kv.Value, out var nid2)) copy.Days2[kv.Key] = nid2;
            if (keepDays)
                foreach (var kv in Done)
                {
                    var l = kv.Value.Where(map.ContainsKey).Select(x => map[x]).ToList();
                    if (l.Count > 0) copy.Done[kv.Key] = l;
                }
            if (keepDays)
                foreach (var kv in Notes) copy.Notes[kv.Key] = kv.Value;
            return copy;
        }
    }

    /// <summary>
    /// Everything one calendar card keeps. Serialized by <c>SaveState</c> into the board project file (no file of its own).
    /// </summary>
    public class CalendarState
    {
        public List<Sheet> Sheets { get; set; } = new();
        public int ActiveSheet { get; set; }
        public bool PanelOpen { get; set; } = true;

        /// <summary>Makes a loaded (or new) state usable: at least one sheet, active index in range.</summary>
        public CalendarState Normalize()
        {
            Sheets ??= new();
            if (Sheets.Count == 0) Sheets.Add(SeedSheet());
            foreach (var s in Sheets)
            {
                s.Goals ??= new(); s.Days ??= new(); s.Days2 ??= new(); s.Done ??= new(); s.Notes ??= new();
            }
            ActiveSheet = Math.Clamp(ActiveSheet, 0, Sheets.Count - 1);
            return this;
        }

        /// <summary>The first sheet of a new calendar card, with a few starter goals.</summary>
        public static Sheet SeedSheet() => new()
        {
            Name = "기본 일정표",
            Goals =
            {
                new Goal { Name = "기획", Hex = "#4F8EF7" },
                new Goal { Name = "개발", Hex = "#6FCF97" },
                new Goal { Name = "테스트", Hex = "#27AE8F" },
                new Goal { Name = "릴리즈", Hex = "#EF5B5B" },
            }
        };
    }
}
