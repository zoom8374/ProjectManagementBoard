using System.Windows.Controls;

namespace ProjectManagementBoard.Controls
{
    /// <summary>
    /// Marker container that holds a tool's own view inside a card. The board treats everything under it as the tool's:
    /// mouse presses, right-clicks, plain wheel and shortcuts go to the tool instead of dragging/panning/zooming the board.
    /// </summary>
    public class ToolContentHost : Border { }
}
