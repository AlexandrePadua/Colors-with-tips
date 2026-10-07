using System;
using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Protocol;
using Pitacor.Core.Rules;

namespace Pitacor.Core.Match
{
    // Per-player views (spec 4.5). Everything returned is a fresh copy: callers may serialize or keep it.
    public sealed partial class RoomEngine
    {
        /// <summary>
        /// What <paramref name="forPlayerId"/> may see. Before the reveal the target and the options exist only in the
        /// giver's view; live markers follow the visibility mode (spectators and pending joiners see none).
        /// Unknown ids get a spectator view.
        /// </summary>
        public RoomView BuildView(string forPlayerId, long nowMs)
        {
            var view = new RoomView
            {
                RoomCode = RoomCode,
                YouId = forPlayerId,
                HostId = _hostId,
                RoomPhase = _phase,
                Settings = _settings.Clone(),
                Board = Board,
                ServerNowMs = nowMs,
                Version = _version,
            };
            bool inMatch = _phase == RoomPhase.InMatch;
            foreach (var p in _players)
            {
                view.Players.Add(new PlayerView
                {
                    Id = p.Id,
                    Nickname = p.Nickname,
                    Avatar = p.Avatar == null ? new AvatarSpec() : p.Avatar.Clone(),
                    Score = p.Score,
                    IsHost = p.IsHost,
                    Connected = p.Connected,
                    IsGiver = inMatch && p.Id == _m.GiverId,
                    HasConfirmed = inMatch && IsGuessingPhase(_m.Phase) && _m.Confirmed.Contains(p.Id),
                    PendingJoin = p.PendingJoin,
                    Ready = p.Ready,
                    TimesAsGiver = p.TimesAsGiver,
                });
            }
            foreach (var message in _chat) view.Chat.Add(message.Clone());
            if (_phase != RoomPhase.Lobby && _m != null) view.Match = BuildMatchView(forPlayerId, nowMs);
            return view;
        }

        private MatchView BuildMatchView(string viewerId, long now)
        {
            var m = _m;
            bool ended = _phase == RoomPhase.MatchEnd;
            bool reveal = !ended && m.Phase == MatchPhase.Reveal;
            bool live = !ended && !reveal && m.Phase != MatchPhase.RoundIntro;
            bool isGiver = viewerId != null && viewerId == m.GiverId;
            bool isGuesser = viewerId != null && m.Guessers.Contains(viewerId);

            var mv = new MatchView
            {
                RoundNumber = m.RoundNumber,
                Phase = m.Phase,
                GiverId = m.GiverId,
                Overtime = !ended && m.Overtime,
                Paused = !ended && m.Paused,
                GiverAway = !ended && m.GraceActive,
                Clue1 = m.Clue1,
                Clue2 = m.Clue2,
                Clue2Passed = m.Clue2Passed,
            };

            if (!ended)
            {
                if (m.Paused)
                {
                    mv.PhaseRemainingMs = m.PhaseEndsAtMs.HasValue ? Math.Max(0, m.PhaseEndsAtMs.Value - m.PausedAtMs) : (long?)null;
                    if (m.GraceActive) mv.PhaseRemainingMs = m.GraceSavedRemainingMs;
                }
                else if (m.GraceActive)
                {
                    mv.PhaseRemainingMs = m.GraceSavedRemainingMs;
                    mv.GiverGraceEndsAtMs = m.GraceEndsAtMs;
                }
                else if (m.PhaseEndsAtMs.HasValue)
                {
                    mv.PhaseEndsAtMs = m.PhaseEndsAtMs;
                    mv.PhaseRemainingMs = Math.Max(0, m.PhaseEndsAtMs.Value - now);
                }
                if (IsGuessingPhase(m.Phase) && _settings.GuessTiming == GuessTiming.TurnBased)
                    mv.CurrentTurnPlayerId = m.CurrentTurnPlayerId;
            }

            if (live && isGiver)
            {
                mv.Options = new List<OptionView>(m.Options.Count);
                for (int i = 0; i < m.Options.Count; i++)
                    mv.Options.Add(new OptionView { Index = i, Coord = m.Options[i], Hex = Palette.GetHex(Board, m.Options[i]) });
                mv.TargetCoord = m.Target;
                mv.TargetHex = m.TargetHex;
            }
            else if ((reveal || ended) && m.LastRound != null)
            {
                mv.Options = new List<OptionView>(m.LastRound.Options.Count);
                foreach (var o in m.LastRound.Options) mv.Options.Add(o.Clone());
                mv.TargetCoord = m.LastRound.Target;
                mv.TargetHex = m.LastRound.TargetHex;
            }

            if (reveal)
            {
                foreach (var marker in m.Markers) mv.Markers.Add(marker.Clone());
            }
            else if (live)
            {
                bool seesAll = isGiver || (isGuesser && _settings.GuessVisibility == GuessVisibility.Visible);
                foreach (var marker in m.Markers)
                    if (seesAll || (isGuesser && marker.PlayerId == viewerId)) mv.Markers.Add(marker.Clone());
                if (isGuesser && IsGuessingPhase(m.Phase) && m.Selections.TryGetValue(viewerId, out BoardCoord selection))
                    mv.YourSelection = selection;
            }

            if ((reveal || ended) && m.LastRound != null) mv.LastRound = m.LastRound.Clone();
            if (ended)
            {
                mv.History = new List<RoundResult>(m.History.Count);
                foreach (var round in m.History) mv.History.Add(round.Clone());
                mv.Winners = m.Winners == null ? new List<string>() : new List<string>(m.Winners);
            }
            return mv;
        }
    }
}
