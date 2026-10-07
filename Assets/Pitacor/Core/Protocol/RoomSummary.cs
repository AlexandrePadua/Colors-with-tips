using Pitacor.Core.Rules;

namespace Pitacor.Core.Protocol
{
    /// <summary>One entry of the public room list.</summary>
    public class RoomSummary
    {
        public string RoomCode { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public RoomPhase Phase { get; set; }
        public Language Language { get; set; }
        public bool IsPublic { get; set; }
    }
}
