using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Rules;

namespace Pitacor.Core.Protocol
{
    /// <summary>Everything one player may see about the room. Built per player by RoomEngine.BuildView.</summary>
    public class RoomView
    {
        public string RoomCode { get; set; }
        public string YouId { get; set; }
        public string HostId { get; set; }
        public RoomPhase RoomPhase { get; set; }
        public GameSettings Settings { get; set; }
        public BoardSize Board { get; set; }
        /// <summary>In rotation order.</summary>
        public List<PlayerView> Players { get; set; } = new List<PlayerView>();
        /// <summary>Null in the lobby.</summary>
        public MatchView Match { get; set; }
        /// <summary>Last messages, oldest first.</summary>
        public List<ChatMessageView> Chat { get; set; } = new List<ChatMessageView>();
        public long ServerNowMs { get; set; }
        /// <summary>Increases on every state change of the room.</summary>
        public int Version { get; set; }
    }

    public class PlayerView
    {
        public string Id { get; set; }
        public string Nickname { get; set; }
        public AvatarSpec Avatar { get; set; }
        public int Score { get; set; }
        public bool IsHost { get; set; }
        public bool Connected { get; set; }
        public bool IsGiver { get; set; }
        /// <summary>Confirmed a marker in the current guessing phase (public in every visibility mode).</summary>
        public bool HasConfirmed { get; set; }
        /// <summary>Joined mid-match: spectator until the next round starts.</summary>
        public bool PendingJoin { get; set; }
        public bool Ready { get; set; }
        public int TimesAsGiver { get; set; }
    }

    public class MatchView
    {
        public int RoundNumber { get; set; }
        public MatchPhase Phase { get; set; }
        public string GiverId { get; set; }
        /// <summary>Server time when the current phase (or turn) ends. Null without limit, in overtime, while paused
        /// or while waiting for the giver to come back.</summary>
        public long? PhaseEndsAtMs { get; set; }
        /// <summary>Time left in the phase at ServerNowMs; frozen while paused or waiting for the giver.</summary>
        public long? PhaseRemainingMs { get; set; }
        public bool Overtime { get; set; }
        public bool Paused { get; set; }
        /// <summary>The giver dropped during GiverChoosing/Clue2Decision; the phase timer is on hold.</summary>
        public bool GiverAway { get; set; }
        /// <summary>When the giver's reconnection grace ends (null when not waiting or while paused).</summary>
        public long? GiverGraceEndsAtMs { get; set; }
        public string Clue1 { get; set; }
        public string Clue2 { get; set; }
        public bool Clue2Passed { get; set; }
        /// <summary>The 4 options: giver only until Reveal, everyone at Reveal.</summary>
        public List<OptionView> Options { get; set; }
        /// <summary>Giver only until Reveal, everyone at Reveal (unless voided).</summary>
        public BoardCoord? TargetCoord { get; set; }
        public string TargetHex { get; set; }
        /// <summary>Markers of the current round this player may see.</summary>
        public List<MarkerView> Markers { get; set; } = new List<MarkerView>();
        /// <summary>This player's provisional (unconfirmed) cell.</summary>
        public BoardCoord? YourSelection { get; set; }
        public string CurrentTurnPlayerId { get; set; }
        /// <summary>Set during Reveal and at MatchEnd.</summary>
        public RoundResult LastRound { get; set; }
        /// <summary>Set at MatchEnd.</summary>
        public List<RoundResult> History { get; set; }
        /// <summary>Set at MatchEnd (several ids = shared victory).</summary>
        public List<string> Winners { get; set; }
    }

    public class MarkerView
    {
        public string PlayerId { get; set; }
        public BoardCoord Coord { get; set; }
        /// <summary>1 for the first guessing phase, 2 for the second.</summary>
        public int Index { get; set; }

        public MarkerView Clone() => (MarkerView)MemberwiseClone();
    }

    public class OptionView
    {
        public int Index { get; set; }
        public BoardCoord Coord { get; set; }
        public string Hex { get; set; }

        public OptionView Clone() => (OptionView)MemberwiseClone();
    }

    public class ChatMessageView
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        /// <summary>Masked text. For system messages, an EventTypes code (e.g. "PlayerJoined") for the client to localize.</summary>
        public string Text { get; set; }
        public long AtMs { get; set; }
        public bool IsSystem { get; set; }

        public ChatMessageView Clone() => (ChatMessageView)MemberwiseClone();
    }
}
