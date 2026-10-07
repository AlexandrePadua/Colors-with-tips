namespace Pitacor.Core.Protocol
{
    public enum RoomPhase { Lobby, InMatch, MatchEnd }

    /// <summary>Phases of a round while the room is InMatch.</summary>
    public enum MatchPhase { RoundIntro, GiverChoosing, Guessing1, Clue2Decision, Guessing2, Reveal }
}
