namespace ProjectManagementBoard.Controls
{
    /// <summary>
    /// Move mode of the board. Free (Step = 0): cards and groups follow the pointer exactly. Snap: positions and sizes
    /// jump in <see cref="GridStep"/> steps, which makes aligning groups and tools easy.
    /// </summary>
    public static class BoardSnap
    {
        /// <summary>Board units between snap positions.</summary>
        public const double GridStep = 20;

        /// <summary>0 = free move, otherwise the step in use.</summary>
        public static double Step { get; set; }

        public static bool Enabled => Step > 0;

        public static double Apply(double value) => Enabled ? Math.Round(value / Step) * Step : value;
    }
}
