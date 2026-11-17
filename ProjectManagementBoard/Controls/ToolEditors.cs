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
            return new EditRequest("관리 툴 이름", new[] { field }, InfoTitle, () => tool.Title = field.Value.Trim());
        }

        public static string GroupPopupTint(GroupModel group) =>
            ColorUtil.ToHex(ColorUtil.Shade(ColorUtil.Parse(group.Color, GroupModel.DefaultColor), -0.55));

        public static EditRequest GroupTitle(GroupModel group)
        {
            var field = new EditField { Label = "그룹 이름", Value = group.Title };
            return new EditRequest("그룹 이름", new[] { field }, 0, () => group.Title = field.Value.Trim())
            {
                Tint = GroupPopupTint(group),
            };
        }
    }
}
