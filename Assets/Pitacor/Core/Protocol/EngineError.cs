namespace Pitacor.Core.Protocol
{
    /// <summary>A rejected action, addressed to the player who attempted it.</summary>
    public class EngineError
    {
        public string PlayerId { get; set; }
        /// <summary>One of <see cref="ErrorCodes"/>.</summary>
        public string Code { get; set; }
        /// <summary>Machine-readable detail. For InvalidClue: "ClueRejectReason:word".</summary>
        public string Detail { get; set; }
    }

    /// <summary>Stable error codes. The client maps them to localized messages.</summary>
    public static class ErrorCodes
    {
        public const string NotHost = "NotHost";
        public const string WrongPhase = "WrongPhase";
        public const string NotYourTurn = "NotYourTurn";
        public const string NotGuesser = "NotGuesser";
        public const string NotGiver = "NotGiver";
        public const string AlreadyConfirmed = "AlreadyConfirmed";
        public const string CellOccupied = "CellOccupied";
        public const string OutOfBoard = "OutOfBoard";
        public const string InvalidClue = "InvalidClue";
        public const string ChatLockedForGiver = "ChatLockedForGiver";
        public const string NotEnoughPlayers = "NotEnoughPlayers";
        public const string RoomFull = "RoomFull";
        public const string Banned = "Banned";
        public const string InvalidSettings = "InvalidSettings";
        public const string InvalidNickname = "InvalidNickname";
        /// <summary>Malformed or unknown command (missing field, bad index, unknown Type...).</summary>
        public const string InvalidCommand = "InvalidCommand";
        public const string PlayerNotFound = "PlayerNotFound";
        /// <summary>Action not permitted in this context (e.g. vote-kick in a private room, kicking yourself).</summary>
        public const string NotAllowed = "NotAllowed";
        /// <summary>The match is paused (not enough connected players).</summary>
        public const string Paused = "Paused";
        public const string RateLimited = "RateLimited";
        public const string Unknown = "Unknown";
    }
}
