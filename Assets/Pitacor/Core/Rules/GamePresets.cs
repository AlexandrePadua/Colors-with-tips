namespace Pitacor.Core.Rules
{
    /// <summary>Scoring presets. Changing any point field in the lobby turns the preset into "custom".</summary>
    public static class GamePresets
    {
        public const string Classic = "classic";
        public const string Generous = "generous";
        public const string Bullseye = "bullseye";
        public const string Custom = "custom";

        private static readonly string[] Named = { Classic, Generous, Bullseye };

        public static bool IsKnown(string id) => id == Classic || id == Generous || id == Bullseye || id == Custom;

        /// <summary>Applies a preset's point values to the settings. Unknown or "custom" ids leave the points unchanged.</summary>
        public static void Apply(GameSettings s, string id)
        {
            switch (id)
            {
                case Classic: SetPoints(s, 3, 2, 1, 1, 2, 2); break;
                case Generous: SetPoints(s, 5, 3, 1, 2, 3, 2); break;
                case Bullseye: SetPoints(s, 5, 1, 0, 1, 2, 3); break;
                default: return;
            }
            s.PresetId = id;
        }

        /// <summary>Returns the preset id whose point values match the settings, or "custom".</summary>
        public static string Detect(GameSettings s)
        {
            foreach (var id in Named)
            {
                var probe = new GameSettings();
                Apply(probe, id);
                if (probe.PointsTarget == s.PointsTarget && probe.PointsInner == s.PointsInner && probe.PointsRing == s.PointsRing
                    && probe.GiverPointsPerGuesser == s.GiverPointsPerGuesser
                    && probe.GiverPointsPerGuesserThreePlayers == s.GiverPointsPerGuesserThreePlayers
                    && probe.SkipBonusMultiplier == s.SkipBonusMultiplier)
                    return id;
            }
            return Custom;
        }

        private static void SetPoints(GameSettings s, int target, int inner, int ring, int giver, int giverThree, int bonus)
        {
            s.PointsTarget = target;
            s.PointsInner = inner;
            s.PointsRing = ring;
            s.GiverPointsPerGuesser = giver;
            s.GiverPointsPerGuesserThreePlayers = giverThree;
            s.SkipBonusMultiplier = bonus;
        }
    }
}
