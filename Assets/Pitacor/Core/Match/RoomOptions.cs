using Pitacor.Core.Rules;

namespace Pitacor.Core.Match
{
    /// <summary>Per-room engine parameters chosen by the server (not by players).</summary>
    public class RoomOptions
    {
        /// <summary>Starting settings (null = defaults). Cloned and clamped by the engine.</summary>
        public GameSettings InitialSettings { get; set; }

        /// <summary>Dev only: minimum connected players to start/keep playing. 0 = GameSettings.MinPlayers.</summary>
        public int MinPlayersOverride { get; set; }
    }

    /// <summary>Fixed durations of the match flow, in milliseconds.</summary>
    public static class MatchTimings
    {
        public const long RoundIntroMs = 2500;
        public const long RevealMinMs = 5000;
        public const long RevealMaxMs = 20000;
        public const long VoidedRevealMs = 3000;
        /// <summary>How long a dropped giver may take to come back before the phase is forced.</summary>
        public const long GiverGraceMs = 60000;
        /// <summary>How long a dropped host keeps the role before it passes to the oldest connected player.</summary>
        public const long HostGraceMs = 60000;
    }

    /// <summary>Size limits enforced by the engine.</summary>
    public static class RoomLimits
    {
        public const int ChatMaxLength = 200;
        public const int ChatHistory = 50;
        public const int ReportMaxLength = 500;
        public const int OptionCount = 4;
        public const int OptionSpreadNormal = 4;
        public const int OptionSpreadUltraHard = 2;
    }
}
