using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Protocol;

namespace Pitacor.Core.Rules
{
    /// <summary>What a round produced, as far as scoring is concerned.</summary>
    public class ScoringInput
    {
        public BoardCoord Target { get; set; }
        public string GiverId { get; set; }
        /// <summary>Guessers to score, in tally order (rotation order starting after the giver).</summary>
        public List<string> GuesserIds { get; set; } = new List<string>();
        /// <summary>Giver + active guessers when the round started (drives the 3-player rule). 0 = 1 + GuesserIds.Count.</summary>
        public int PlayersAtRoundStart { get; set; }
        /// <summary>Confirmed markers of the round (any player; markers of non-guessers are ignored).</summary>
        public List<MarkerView> Markers { get; set; } = new List<MarkerView>();
        /// <summary>The giver explicitly passed the second clue.</summary>
        public bool BonusApplied { get; set; }
    }

    public class ScoringOutcome
    {
        /// <summary>Lines in tally order with Discarded/TotalAfter filled in.</summary>
        public List<ScoreLine> Lines { get; } = new List<ScoreLine>();
        /// <summary>Every score after the round (players without a line keep their previous score).</summary>
        public Dictionary<string, int> ScoresAfter { get; } = new Dictionary<string, int>();
        /// <summary>Someone is at or above the target score after this round.</summary>
        public bool TargetReached { get; set; }
        /// <summary>Immediate end mode: the single player who crossed the target first in the tally.</summary>
        public string ImmediateWinnerId { get; set; }
    }

    /// <summary>Pure scoring rules (see docs/tech/core-spec.md section 4.4).</summary>
    public static class Scoring
    {
        public static int PointsForZone(ScoreZone zone, GameSettings s)
        {
            switch (zone)
            {
                case ScoreZone.Target: return s.PointsTarget;
                case ScoreZone.Inner: return s.PointsInner;
                case ScoreZone.Ring: return s.PointsRing;
                default: return 0;
            }
        }

        /// <summary>Best zone among the markers; Outside when there are none.</summary>
        public static ScoreZone BestZone(BoardCoord target, IEnumerable<BoardCoord> markers)
        {
            var best = ScoreZone.Outside;
            foreach (var m in markers)
            {
                var zone = BoardGeometry.Zone(target, m);
                if (zone < best) best = zone;
            }
            return best;
        }

        /// <summary>The giver scores for each guesser whose best marker is on the target or in the 3x3 frame.</summary>
        public static bool CountsForGiver(ScoreZone zone) => zone == ScoreZone.Target || zone == ScoreZone.Inner;

        public static int GiverPointsPerGuesser(int playersInRound, GameSettings s) =>
            playersInRound == 3 ? s.GiverPointsPerGuesserThreePlayers : s.GiverPointsPerGuesser;

        /// <summary>Raw lines (guessers in input order, then the giver), not yet applied to any score.</summary>
        public static List<ScoreLine> ComputeLines(ScoringInput input, GameSettings s)
        {
            var lines = new List<ScoreLine>(input.GuesserIds.Count + 1);
            int qualifying = 0;
            var own = new List<BoardCoord>(2);
            foreach (var guesserId in input.GuesserIds)
            {
                own.Clear();
                foreach (var m in input.Markers)
                    if (m.PlayerId == guesserId) own.Add(m.Coord);
                var zone = BestZone(input.Target, own);
                if (CountsForGiver(zone)) qualifying++;
                lines.Add(new ScoreLine { PlayerId = guesserId, Zone = zone, Points = PointsForZone(zone, s) });
            }

            int players = input.PlayersAtRoundStart > 0 ? input.PlayersAtRoundStart : 1 + input.GuesserIds.Count;
            int giverPoints = qualifying * GiverPointsPerGuesser(players, s);
            if (input.BonusApplied) giverPoints *= s.SkipBonusMultiplier;
            lines.Add(new ScoreLine { PlayerId = input.GiverId, IsGiver = true, QualifyingGuessers = qualifying, Points = giverPoints });
            return lines;
        }

        /// <summary>
        /// Applies lines in order according to s.EndMode. Immediate: the first line that takes a player from below to at
        /// or above s.TargetScore wins alone and every later line is discarded. Other modes apply every line.
        /// The input lines are not modified.
        /// </summary>
        public static ScoringOutcome Apply(IList<ScoreLine> lines, IReadOnlyDictionary<string, int> scoresBefore, GameSettings s)
        {
            var outcome = new ScoringOutcome();
            if (scoresBefore != null)
                foreach (var kv in scoresBefore) outcome.ScoresAfter[kv.Key] = kv.Value;

            bool stopped = false;
            foreach (var source in lines)
            {
                var line = source.Clone();
                outcome.ScoresAfter.TryGetValue(line.PlayerId, out int before);
                if (stopped)
                {
                    line.Discarded = true;
                    line.TotalAfter = before;
                    outcome.Lines.Add(line);
                    continue;
                }
                int after = before + line.Points;
                outcome.ScoresAfter[line.PlayerId] = after;
                line.Discarded = false;
                line.TotalAfter = after;
                outcome.Lines.Add(line);
                if (s.EndMode == EndMode.Immediate && before < s.TargetScore && after >= s.TargetScore)
                {
                    outcome.ImmediateWinnerId = line.PlayerId;
                    stopped = true;
                }
            }

            foreach (var kv in outcome.ScoresAfter)
                if (kv.Value >= s.TargetScore) outcome.TargetReached = true;
            return outcome;
        }

        public static ScoringOutcome ScoreRound(ScoringInput input, IReadOnlyDictionary<string, int> scoresBefore, GameSettings s) =>
            Apply(ComputeLines(input, s), scoresBefore, s);

        /// <summary>Every player with the highest score (ties share the victory). Empty when nobody scored.</summary>
        public static List<string> Winners(IEnumerable<KeyValuePair<string, int>> scores)
        {
            var winners = new List<string>();
            int best = 0;
            foreach (var kv in scores)
            {
                if (kv.Value > best)
                {
                    best = kv.Value;
                    winners.Clear();
                    winners.Add(kv.Key);
                }
                else if (kv.Value == best && best > 0)
                {
                    winners.Add(kv.Key);
                }
            }
            return winners;
        }
    }
}
