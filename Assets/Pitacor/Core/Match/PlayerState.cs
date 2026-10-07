using Pitacor.Core.Protocol;

namespace Pitacor.Core.Match
{
    /// <summary>Server-side player record. Owned by RoomEngine: read it, never modify it from outside.</summary>
    public class PlayerState
    {
        public string Id { get; set; }
        public string Nickname { get; set; }
        public AvatarSpec Avatar { get; set; }
        public bool IsHost { get; set; }
        public bool Connected { get; set; }
        public long DisconnectedAtMs { get; set; }
        public int Score { get; set; }
        /// <summary>Rounds this player was assigned as giver in the current match (voided rounds included).</summary>
        public int TimesAsGiver { get; set; }
        /// <summary>Joined mid-match: spectator until the next round starts.</summary>
        public bool PendingJoin { get; set; }
        public bool Ready { get; set; }
        public long JoinOrder { get; set; }
    }
}
