using System;

namespace Pitacor.Core.Board
{
    /// <summary>Board dimensions in cells. Rows are lettered from A, columns numbered from 1.</summary>
    [Serializable]
    public struct BoardSize : IEquatable<BoardSize>
    {
        public int Cols { get; set; }
        public int Rows { get; set; }

        public BoardSize(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
        }

        public int CellCount => Cols * Rows;

        public static BoardSize Normal => new BoardSize(30, 16);
        public static BoardSize UltraHard => new BoardSize(15, 8);

        public bool Equals(BoardSize other) => Cols == other.Cols && Rows == other.Rows;
        public override bool Equals(object obj) => obj is BoardSize other && Equals(other);
        public override int GetHashCode() => (Cols * 397) ^ Rows;
        public static bool operator ==(BoardSize a, BoardSize b) => a.Equals(b);
        public static bool operator !=(BoardSize a, BoardSize b) => !a.Equals(b);
        public override string ToString() => Cols + "x" + Rows;
    }
}
