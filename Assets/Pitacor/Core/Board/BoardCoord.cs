using System;

namespace Pitacor.Core.Board
{
    /// <summary>Zero-based cell coordinate. Label format is row letter + 1-based column, e.g. Row 10, Col 24 = "K25".</summary>
    [Serializable]
    public struct BoardCoord : IEquatable<BoardCoord>
    {
        public int Col { get; set; }
        public int Row { get; set; }

        public BoardCoord(int col, int row)
        {
            Col = col;
            Row = row;
        }

        public int ToIndex(BoardSize size) => Row * size.Cols + Col;

        public static BoardCoord FromIndex(int index, BoardSize size) => new BoardCoord(index % size.Cols, index / size.Cols);

        public string ToLabel() => ((char)('A' + Row)).ToString() + (Col + 1);

        public static bool TryParse(string label, BoardSize size, out BoardCoord coord)
        {
            coord = default;
            if (string.IsNullOrWhiteSpace(label)) return false;
            label = label.Trim().ToUpperInvariant();
            if (label.Length < 2) return false;
            int row = label[0] - 'A';
            if (!int.TryParse(label.Substring(1), out int colNumber)) return false;
            var parsed = new BoardCoord(colNumber - 1, row);
            if (!BoardGeometry.Contains(size, parsed)) return false;
            coord = parsed;
            return true;
        }

        public bool Equals(BoardCoord other) => Col == other.Col && Row == other.Row;
        public override bool Equals(object obj) => obj is BoardCoord other && Equals(other);
        public override int GetHashCode() => (Col * 397) ^ Row;
        public static bool operator ==(BoardCoord a, BoardCoord b) => a.Equals(b);
        public static bool operator !=(BoardCoord a, BoardCoord b) => !a.Equals(b);
        public override string ToString() => ToLabel();
    }
}
