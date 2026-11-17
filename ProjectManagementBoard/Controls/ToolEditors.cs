using ProjectManagementBoard.Models;

namespace ProjectManagementBoard.Controls
{
    /// <summary>Edit-popup definitions for the things on the board (tool name, group name).</summary>
    public static class ToolEditors
    {
        public const int InfoTitle = 0;

        public static EditRequest ToolInfo(ToolModel tool)
        {
            var field = new EditField { Label = "이름", Value = tool.Title };
            var size = SizeField(tool.TitleFontSize);
            return new EditRequest("관리 툴 이름", new[] { field, size }, InfoTitle, () =>
            {
                tool.Title = field.Value.Trim();
                tool.TitleFontSize = ParseSize(size.Value, tool.TitleFontSize);
            });
        }

        public const double MinFontSize = 8, MaxFontSize = 80;

        private static EditField SizeField(double current) =>
            new() { Label = $"글자 크기 ({MinFontSize:0}–{MaxFontSize:0})", Value = current.ToString("0.##") };

        private static double ParseSize(string text, double fallback) =>
            double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? Math.Clamp(v, MinFontSize, MaxFontSize) : fallback;

        public static string GroupPopupTint(GroupModel group) =>
            ColorUtil.ToHex(ColorUtil.Shade(ColorUtil.Parse(group.Color, GroupModel.DefaultColor), -0.55));

        public static EditRequest GroupTitle(GroupModel group)
        {
            var field = new EditField { Label = "그룹 이름", Value = group.Title };
            var size = SizeField(group.TitleFontSize);
            return new EditRequest("그룹 이름", new[] { field, size }, 0, () =>
            {
                group.Title = field.Value.Trim();
                group.TitleFontSize = ParseSize(size.Value, group.TitleFontSize);
            })
            {
                Tint = GroupPopupTint(group),
            };
        }
    }
}
