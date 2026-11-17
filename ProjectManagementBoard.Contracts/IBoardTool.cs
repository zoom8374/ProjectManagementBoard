using System.Windows;

namespace ProjectManagementBoard.Contracts
{
    /// <summary>
    /// Entry point of a tool DLL. The board finds every public, parameterless-constructor class implementing this
    /// interface in <c>Plugins\**\*.dll</c> and lists it under "+ 새 관리 툴". One instance of this class is a
    /// factory: each card created from it gets its own <see cref="IBoardToolInstance"/>.
    /// </summary>
    public interface IBoardTool
    {
        /// <summary>Stable unique id (e.g. "memo"). Saved in the project file to find the tool again — never change it.</summary>
        string Id { get; }

        /// <summary>Name shown in the tool picker and as the default card title.</summary>
        string DisplayName { get; }

        /// <summary>One-line description shown in the tool picker.</summary>
        string Description { get; }

        /// <summary>Creates the content for one new card.</summary>
        IBoardToolInstance CreateInstance();
    }

    /// <summary>The live content of one card. Must be a WPF element so it zooms/moves/resizes with the board.</summary>
    public interface IBoardToolInstance
    {
        /// <summary>The element docked into the card. It is sized by the card (stretch), so don't set a fixed Width/Height.</summary>
        FrameworkElement View { get; }

        /// <summary>Serializes the tool's own data (e.g. memo text) for the board project file. Return "" if there is none.</summary>
        string SaveState();

        /// <summary>Restores what <see cref="SaveState"/> returned. Called once, right after creation, only when a saved state exists.</summary>
        void LoadState(string state);
    }

    /// <summary>
    /// Optional, implemented next to <see cref="IBoardTool"/>: the size (board units, title row included) of a newly
    /// added card for this tool. Without it the board uses its default card size.
    /// </summary>
    public interface IBoardToolDefaultSize
    {
        double DefaultWidth { get; }
        double DefaultHeight { get; }
    }
}
