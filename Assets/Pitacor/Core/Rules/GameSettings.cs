using System;

namespace Pitacor.Core.Rules
{
    /// <summary>Room settings. Editable by the host in the lobby only; locked during a match.</summary>
    [Serializable]
    public class GameSettings
    {
        public const int MinPlayers = 3;
        public const int MaxPlayers = 10;

        public GuessTiming GuessTiming { get; set; } = GuessTiming.Simultaneous;
        public GuessVisibility GuessVisibility { get; set; } = GuessVisibility.Hidden;
        public int ClueTimerSeconds { get; set; } = 90;
        public int GuessTimerSeconds { get; set; } = 45;
        public TimerExpiry TimerExpiry { get; set; } = TimerExpiry.Force;
        public int TargetScore { get; set; } = 25;
        public EndMode EndMode { get; set; } = EndMode.CompleteCycle;

        public int PointsTarget { get; set; } = 3;
        public int PointsInner { get; set; } = 2;
        public int PointsRing { get; set; } = 1;
        public int GiverPointsPerGuesser { get; set; } = 1;
        public int GiverPointsPerGuesserThreePlayers { get; set; } = 2;
        public int SkipBonusMultiplier { get; set; } = 2;

        public bool BlockLightDarkDirectionsNames { get; set; } = true;
        public bool UltraHard { get; set; }
        public Language Language { get; set; } = Language.PtBR;
        public bool IsPublic { get; set; }
        public string PresetId { get; set; } = GamePresets.Classic;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        /// <summary>Forces every field into its allowed range. Returns this instance for chaining.</summary>
        public GameSettings Clamp()
        {
            // Enums arrive from JSON and may hold undefined numbers (e.g. EndMode 42 would never end the match).
            var defaults = new GameSettings();
            if (!Enum.IsDefined(typeof(GuessTiming), GuessTiming)) GuessTiming = defaults.GuessTiming;
            if (!Enum.IsDefined(typeof(GuessVisibility), GuessVisibility)) GuessVisibility = defaults.GuessVisibility;
            if (!Enum.IsDefined(typeof(TimerExpiry), TimerExpiry)) TimerExpiry = defaults.TimerExpiry;
            if (!Enum.IsDefined(typeof(EndMode), EndMode)) EndMode = defaults.EndMode;
            if (!Enum.IsDefined(typeof(Language), Language)) Language = defaults.Language;
            ClueTimerSeconds = ClampTimer(ClueTimerSeconds, 15, 300);
            GuessTimerSeconds = ClampTimer(GuessTimerSeconds, 10, 300);
            TargetScore = Math.Max(5, Math.Min(200, TargetScore));
            PointsTarget = ClampPoints(PointsTarget);
            PointsInner = ClampPoints(PointsInner);
            PointsRing = ClampPoints(PointsRing);
            GiverPointsPerGuesser = ClampPoints(GiverPointsPerGuesser);
            GiverPointsPerGuesserThreePlayers = ClampPoints(GiverPointsPerGuesserThreePlayers);
            SkipBonusMultiplier = Math.Max(1, Math.Min(5, SkipBonusMultiplier));
            if (!GamePresets.IsKnown(PresetId)) PresetId = GamePresets.Custom;
            return this;
        }

        private static int ClampTimer(int seconds, int min, int max) => seconds <= 0 ? 0 : Math.Max(min, Math.Min(max, seconds));

        private static int ClampPoints(int points) => Math.Max(0, Math.Min(10, points));
    }
}
