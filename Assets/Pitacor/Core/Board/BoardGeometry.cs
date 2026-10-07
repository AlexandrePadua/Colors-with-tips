using System;

namespace Pitacor.Core.Board
{
    /// <summary>Scoring zones around the target: the target itself, the 3x3 frame, the 5x5 ring, and everything else.</summary>
    public enum ScoreZone
    {
        Target = 0,
        Inner = 1,
        Ring = 2,
        Outside = 3,
    }

    public static class BoardGeometry
    {
        public static bool Contains(BoardSize size, BoardCoord c) =>
            c.Col >= 0 && c.Row >= 0 && c.Col < size.Cols && c.Row < size.Rows;

        /// <summary>Chebyshev distance: diagonal neighbours are at distance 1.</summary>
        public static int Distance(BoardCoord a, BoardCoord b) =>
            Math.Max(Math.Abs(a.Col - b.Col), Math.Abs(a.Row - b.Row));

        public static ScoreZone Zone(BoardCoord target, BoardCoord guess)
        {
            switch (Distance(target, guess))
            {
                case 0: return ScoreZone.Target;
                case 1: return ScoreZone.Inner;
                case 2: return ScoreZone.Ring;
                default: return ScoreZone.Outside;
            }
        }
    }
}
