using System.Collections.Generic;
using Pitacor.Core.Rules;

namespace Pitacor.Core.Text
{
    public enum ClueKind
    {
        /// <summary>Exactly one word.</summary>
        First,
        /// <summary>One or two words.</summary>
        Second,
    }

    public enum ClueRejectReason
    {
        None,
        Empty,
        TooManyWords,
        TooLong,
        InvalidCharacters,
        TooShort,
        ColorName,
        LightDarkOrDirection,
        PlayerName,
        Repeated,
        HexLike,
        Profanity,
    }

    public class ClueContext
    {
        public Language Language { get; set; } = Language.PtBR;
        public bool BlockLightDarkDirectionsNames { get; set; } = true;
        public bool FilterProfanity { get; set; }
        public bool UltraHard { get; set; }
        public List<string> PlayerNicknames { get; set; } = new List<string>();
        /// <summary>Normalized clues already used in the match, excluding the current round's first clue.</summary>
        public List<string> PreviousClues { get; set; } = new List<string>();
    }

    public class ClueValidationResult
    {
        public bool Ok { get; set; }
        public ClueRejectReason Reason { get; set; }
        public string OffendingWord { get; set; }
        /// <summary>Lower-case, accent-free, single-spaced form used for comparisons.</summary>
        public string Normalized { get; set; }
        /// <summary>Trimmed, single-spaced text exactly as typed (accents and case preserved).</summary>
        public string Display { get; set; }
    }
}
