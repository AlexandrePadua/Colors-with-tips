using System.Collections.Generic;
using Pitacor.Core.Board;

namespace Pitacor.Core.Protocol
{
    /// <summary>Outcome of one round, shown at Reveal and kept in the match history.</summary>
    public class RoundResult
    {
        public int RoundNumber { get; set; }
        public string GiverId { get; set; }
        /// <summary>Null when the round was voided before the giver chose.</summary>
        public BoardCoord? Target { get; set; }
        public string TargetHex { get; set; }
        public string Clue1 { get; set; }
        public string Clue2 { get; set; }
        public bool Clue2Passed { get; set; }
        public bool BonusApplied { get; set; }
        public bool Voided { get; set; }
        /// <summary>ReasonCodes value when voided.</summary>
        public string VoidReason { get; set; }
        public List<OptionView> Options { get; set; } = new List<OptionView>();
        public List<MarkerView> Markers { get; set; } = new List<MarkerView>();
        /// <summary>In application order: guessers (rotation order after the giver), then the giver.</summary>
        public List<ScoreLine> Lines { get; set; } = new List<ScoreLine>();

        public RoundResult Clone()
        {
            var copy = (RoundResult)MemberwiseClone();
            copy.Options = new List<OptionView>(Options.Count);
            foreach (var o in Options) copy.Options.Add(o.Clone());
            copy.Markers = new List<MarkerView>(Markers.Count);
            foreach (var m in Markers) copy.Markers.Add(m.Clone());
            copy.Lines = new List<ScoreLine>(Lines.Count);
            foreach (var l in Lines) copy.Lines.Add(l.Clone());
            return copy;
        }
    }

    public class ScoreLine
    {
        public string PlayerId { get; set; }
        public int Points { get; set; }
        /// <summary>Zone of the guesser's best marker (Outside when none). Null on the giver's line.</summary>
        public ScoreZone? Zone { get; set; }
        public bool IsGiver { get; set; }
        /// <summary>Giver line only: guessers whose best marker hit Target or Inner.</summary>
        public int QualifyingGuessers { get; set; }
        /// <summary>Immediate end mode: not applied because someone reached the target score earlier in the tally.</summary>
        public bool Discarded { get; set; }
        /// <summary>Player's total after this line (unchanged when discarded).</summary>
        public int TotalAfter { get; set; }

        public ScoreLine Clone() => (ScoreLine)MemberwiseClone();
    }
}
