using System.Collections.Generic;
using Pitacor.Core.Board;

namespace Pitacor.Core.Match
{
    /// <summary>Draws the giver's options: distinct cells, spread apart, never a target already used in the match.</summary>
    public static class OptionPicker
    {
        public const int MaxAttempts = 200;

        /// <summary>
        /// Random sampling with every constraint for up to <see cref="MaxAttempts"/> draws; then relaxes, first the spread
        /// (keeping unused targets), then everything but distinctness. Returns fewer cells only when the board is smaller
        /// than <paramref name="count"/>.
        /// </summary>
        public static List<BoardCoord> Pick(BoardSize size, int count, int minDistance, ICollection<BoardCoord> usedTargets, IRandomSource rng)
        {
            var picked = new List<BoardCoord>(count);
            int cells = size.CellCount;
            if (cells <= 0 || count <= 0) return picked;

            for (int attempt = 0; attempt < MaxAttempts && picked.Count < count; attempt++)
            {
                var c = BoardCoord.FromIndex(rng.Next(0, cells), size);
                if (picked.Contains(c) || IsUsed(c, usedTargets) || TooClose(c, picked, minDistance)) continue;
                picked.Add(c);
            }
            if (picked.Count < count) FillByScan(size, count, picked, usedTargets, rng);
            if (picked.Count < count) FillByScan(size, count, picked, null, rng);
            return picked;
        }

        private static void FillByScan(BoardSize size, int count, List<BoardCoord> picked, ICollection<BoardCoord> avoid, IRandomSource rng)
        {
            int cells = size.CellCount;
            int start = rng.Next(0, cells);
            for (int i = 0; i < cells && picked.Count < count; i++)
            {
                var c = BoardCoord.FromIndex((start + i) % cells, size);
                if (picked.Contains(c) || IsUsed(c, avoid)) continue;
                picked.Add(c);
            }
        }

        private static bool IsUsed(BoardCoord c, ICollection<BoardCoord> used) => used != null && used.Contains(c);

        private static bool TooClose(BoardCoord c, List<BoardCoord> picked, int minDistance)
        {
            foreach (var p in picked)
                if (BoardGeometry.Distance(p, c) < minDistance) return true;
            return false;
        }
    }
}
