using Pitacor.Core.Board;

namespace Pitacor.Core.Protocol
{
    /// <summary>
    /// Something that happened, for client effects (sounds, toasts, animations). The authoritative state is RoomView;
    /// events never carry anything the recipient may not see (the engine filters them per player).
    /// </summary>
    public class GameEvent
    {
        public string Type { get; set; }
        public string PlayerId { get; set; }
        public BoardCoord? Coord { get; set; }
        public string Text { get; set; }
        public int Value { get; set; }
        public string Code { get; set; }

        public GameEvent Clone() => (GameEvent)MemberwiseClone();
    }

    /// <summary>Stable values for GameEvent.Type. Field usage is noted where it is not obvious.</summary>
    public static class EventTypes
    {
        /// <summary>PlayerId, Text = nickname, Value = 1 when joining mid-match as pending.</summary>
        public const string PlayerJoined = "PlayerJoined";
        public const string PlayerLeft = "PlayerLeft";
        public const string PlayerConnected = "PlayerConnected";
        public const string PlayerDisconnected = "PlayerDisconnected";
        /// <summary>PlayerId = new host.</summary>
        public const string HostChanged = "HostChanged";
        public const string SettingsChanged = "SettingsChanged";
        public const string PlayersReordered = "PlayersReordered";
        /// <summary>PlayerId, Text = nickname after the update.</summary>
        public const string ProfileUpdated = "ProfileUpdated";
        public const string MatchStarted = "MatchStarted";
        /// <summary>PlayerId = giver, Value = round number.</summary>
        public const string RoundStarted = "RoundStarted";
        /// <summary>Code = MatchPhase name, Value = round number.</summary>
        public const string PhaseChanged = "PhaseChanged";
        /// <summary>Turn-based guessing: PlayerId = player whose turn starts.</summary>
        public const string TurnChanged = "TurnChanged";
        /// <summary>PlayerId = giver, Text = clue, Value = 1 or 2.</summary>
        public const string ClueGiven = "ClueGiven";
        /// <summary>PlayerId = giver, Value = 1 when the bonus applies (explicit pass), Code = reason (ReasonCodes).</summary>
        public const string Clue2Passed = "Clue2Passed";
        /// <summary>PlayerId = guesser, Value = marker index (1/2), Code = "forced" when confirmed by the timer.
        /// Coord only for recipients allowed to see that marker.</summary>
        public const string GuessConfirmed = "GuessConfirmed";
        /// <summary>WarnOnly timer ran out. Code = MatchPhase name, PlayerId = current turn (turn-based guessing).</summary>
        public const string Overtime = "Overtime";
        /// <summary>PlayerId = giver, Code = reason (ReasonCodes).</summary>
        public const string RoundVoided = "RoundVoided";
        /// <summary>PlayerId = giver, Value = round number. Details are in MatchView.LastRound.</summary>
        public const string RoundScored = "RoundScored";
        public const string GamePaused = "GamePaused";
        public const string GameResumed = "GameResumed";
        /// <summary>Text = comma-separated winner ids, Value = winner count, Code = reason (ReasonCodes).</summary>
        public const string MatchEnded = "MatchEnded";
        public const string ReturnedToLobby = "ReturnedToLobby";
        /// <summary>PlayerId = author, Text = masked text, Value = message id.</summary>
        public const string ChatMessage = "ChatMessage";
        /// <summary>PlayerId, Code = reaction code.</summary>
        public const string Reaction = "Reaction";
        /// <summary>PlayerId = kicked player, Code = ReasonCodes.KickedByHost or ReasonCodes.VoteKick.</summary>
        public const string PlayerKicked = "PlayerKicked";
        /// <summary>PlayerId = target, Value = votes so far, Code = votes needed.</summary>
        public const string VoteKickStarted = "VoteKickStarted";
        /// <summary>PlayerId = target, Value = votes so far, Code = votes needed.</summary>
        public const string VoteKickVote = "VoteKickVote";
        /// <summary>Server only (never sent to clients): PlayerId = reporter, Code = reported player id, Text = reason.</summary>
        public const string ReportFiled = "ReportFiled";
        public const string Error = "Error";
    }

    /// <summary>Stable reason codes used in GameEvent.Code and RoundResult.VoidReason.</summary>
    public static class ReasonCodes
    {
        public const string Explicit = "Explicit";
        public const string Timeout = "Timeout";
        public const string GiverAway = "GiverAway";
        public const string GiverLeft = "GiverLeft";
        public const string HostSkipped = "HostSkipped";
        public const string TargetReached = "TargetReached";
        public const string CycleComplete = "CycleComplete";
        public const string HostEnded = "HostEnded";
        public const string KickedByHost = "KickedByHost";
        public const string VoteKick = "VoteKick";
    }
}
