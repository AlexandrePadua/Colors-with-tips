using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Protocol;

namespace Pitacor.Core.Match
{
    /// <summary>Mutable state of the match in progress (internal to RoomEngine).</summary>
    internal sealed class MatchState
    {
        // Phase and timers. Deadlines are absolute server ms; while Paused nothing expires and every deadline is
        // shifted by the pause length on resume.
        public int RoundNumber;
        public MatchPhase Phase;
        /// <summary>Deadline of the phase (or of the current turn). Null: no limit, overtime, or suspended by the giver grace.</summary>
        public long? PhaseEndsAtMs;
        public bool Overtime;
        public bool Paused;
        public long PausedAtMs;
        /// <summary>The giver is disconnected during GiverChoosing/Clue2Decision: the phase timer is on hold.</summary>
        public bool GraceActive;
        public long GraceEndsAtMs;
        public long? GraceSavedRemainingMs;
        public long RevealMinEndsAtMs;

        // Rotation. The player list is rotated at match start so the first giver is at index 0: a pass of the rotation
        // is one walk through the list, and mid-match joiners are appended at its end.
        public int LastGiverIndex = -1;
        public int PassNumber = 1;
        /// <summary>Giver turns credited to mid-match joiners for the passes they missed (CompleteCycle fairness).</summary>
        public Dictionary<string, int> GiverCredit = new Dictionary<string, int>();

        // Current round.
        public string GiverId;
        public List<BoardCoord> Options = new List<BoardCoord>();
        public BoardCoord? Target;
        public string TargetHex;
        public string Clue1;
        public string Clue1Normalized;
        public string Clue2;
        public string Clue2Normalized;
        public bool Clue2Passed;
        public bool BonusApplied;
        /// <summary>Active guessers in rotation order starting after the giver (players who leave are removed).</summary>
        public List<string> Guessers = new List<string>();
        public int PlayersAtRoundStart;
        /// <summary>Confirmed markers of the round, in confirmation order.</summary>
        public List<MarkerView> Markers = new List<MarkerView>();
        public Dictionary<string, BoardCoord> Selections = new Dictionary<string, BoardCoord>();
        /// <summary>Guessers who placed a marker in the current guessing phase.</summary>
        public HashSet<string> Confirmed = new HashSet<string>();
        /// <summary>Turn-based: players still to play after the current one.</summary>
        public List<string> TurnQueue = new List<string>();
        public string CurrentTurnPlayerId;
        /// <summary>Turn-based: skipped while disconnected; re-queued if they come back during the same phase.</summary>
        public HashSet<string> SkippedTurns = new HashSet<string>();

        // Whole match.
        public RoundResult LastRound;
        public List<RoundResult> History = new List<RoundResult>();
        public HashSet<BoardCoord> UsedTargets = new HashSet<BoardCoord>();
        /// <summary>Normalized clues accepted in the match.</summary>
        public List<string> UsedClues = new List<string>();
        public bool EndAfterReveal;
        public string ImmediateWinnerId;
        /// <summary>CompleteCycle: rotation frozen when the target score was first reached, and the giver count each must reach.</summary>
        public List<string> CycleMembers;
        public int CycleGiverTimes;
        public List<string> Winners;

        public void ResetRound()
        {
            PhaseEndsAtMs = null;
            Overtime = false;
            GraceActive = false;
            GraceSavedRemainingMs = null;
            GiverId = null;
            Options = new List<BoardCoord>();
            Target = null;
            TargetHex = null;
            Clue1 = null;
            Clue1Normalized = null;
            Clue2 = null;
            Clue2Normalized = null;
            Clue2Passed = false;
            BonusApplied = false;
            Guessers = new List<string>();
            PlayersAtRoundStart = 0;
            Markers = new List<MarkerView>();
            Selections.Clear();
            Confirmed.Clear();
            TurnQueue.Clear();
            CurrentTurnPlayerId = null;
            SkippedTurns.Clear();
            LastRound = null;
        }
    }
}
