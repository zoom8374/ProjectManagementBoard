using System.Collections.Generic;

namespace CheckMemoTool.Models
{
    /// <summary>One checklist item as stored in the board project file.</summary>
    public class ItemData
    {
        public string Id { get; set; }
        public string Text { get; set; }

        // "high" / "medium" / "low" / null (none)
        public string Priority { get; set; }
        public bool IsDone { get; set; }

        // order inside the same priority group
        public int Order { get; set; }

        // ISO 8601 strings (e.g. 2026-11-03T09:00:00+09:00)
        public string CreatedAt { get; set; }
        public string DoneAt { get; set; }
    }

    /// <summary>Everything one checklist card keeps: this is what SaveState/LoadState (de)serialize.</summary>
    public class ChecklistState
    {
        public double FontSize { get; set; } = FontLimits.DefaultFontSize;
        public List<ItemData> Items { get; set; } = new List<ItemData>();
    }

    public static class FontLimits
    {
        public const double DefaultFontSize = 13;   // pt
        public const double MinFontSize = 10;
        public const double MaxFontSize = 24;
    }
}
