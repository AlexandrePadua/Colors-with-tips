using System;

namespace Pitacor.Core.Board
{
    /// <summary>Access to the generated palettes (see Tools/PaletteGen and PaletteData.cs).</summary>
    public static class Palette
    {
        public static string[] ForSize(BoardSize size)
        {
            if (size == BoardSize.Normal) return PaletteData.NormalHex;
            if (size == BoardSize.UltraHard) return PaletteData.UltraHardHex;
            throw new ArgumentException("No palette for board size " + size, nameof(size));
        }

        public static string GetHex(BoardSize size, BoardCoord coord) => ForSize(size)[coord.ToIndex(size)];
    }
}
