using System.Collections.Generic;
using Pitacor.Core.Protocol;
using Pitacor.Core.Rules;
using Pitacor.Core.Text;

namespace Pitacor.Core.Match
{
    // Lobby commands and the match lifecycle (start, end, rematch).
    public sealed partial class RoomEngine
    {
        private void DoUpdateSettings(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            if (_phase != RoomPhase.Lobby) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            if (cmd.Settings == null) { Reject(r, p, ErrorCodes.InvalidSettings, "Settings"); return; }

            var s = cmd.Settings.Clone().Clamp();
            // Picking another named preset loads its points; otherwise the preset follows the point values
            // (any edited point field turns it into "custom").
            if (s.PresetId != _settings.PresetId && s.PresetId != GamePresets.Custom) GamePresets.Apply(s, s.PresetId);
            else s.PresetId = GamePresets.Detect(s);
            _settings = s;
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.SettingsChanged, p.Id));
        }

        private void DoReorderPlayers(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            if (_phase != RoomPhase.Lobby) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            var order = cmd.Order;
            if (order == null || order.Count != _players.Count) { Reject(r, p, ErrorCodes.InvalidCommand, "Order"); return; }

            var reordered = new List<PlayerState>(order.Count);
            foreach (var id in order)
            {
                var q = Find(id);
                if (q == null || reordered.Contains(q)) { Reject(r, p, ErrorCodes.InvalidCommand, "Order"); return; }
                reordered.Add(q);
            }
            _players.Clear();
            _players.AddRange(reordered);
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.PlayersReordered, p.Id));
        }

        private void DoUpdateProfile(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            if (_phase != RoomPhase.Lobby) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            string nick = p.Nickname;
            if (cmd.Nickname != null)
            {
                string clean = NicknameRules.Sanitize(cmd.Nickname, _settings.IsPublic);
                if (string.IsNullOrEmpty(clean)) { Reject(r, p, ErrorCodes.InvalidNickname); return; }
                nick = UniqueNickname(clean, p.Id);
            }
            p.Nickname = nick;
            if (cmd.Avatar != null) p.Avatar = AvatarSpec.Sanitize(cmd.Avatar);
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.ProfileUpdated, p.Id, p.Nickname));
        }

        private void DoStartMatch(PlayerState p, long now, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            if (_phase != RoomPhase.Lobby) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            if (ConnectedCount < MinPlayers) { Reject(r, p, ErrorCodes.NotEnoughPlayers, MinPlayers.ToString()); return; }

            // Rotate the list so the first giver is at index 0 (same circular order, see MatchState).
            int first = FirstGiverIndex();
            var rotated = new List<PlayerState>(_players.Count);
            for (int i = 0; i < _players.Count; i++) rotated.Add(_players[(first + i) % _players.Count]);
            _players.Clear();
            _players.AddRange(rotated);
            _firstGiverAnchorId = _players[0].Id;

            foreach (var q in _players)
            {
                q.Score = 0;
                q.TimesAsGiver = 0;
                q.PendingJoin = false;
                q.Ready = false;
            }
            _m = new MatchState();
            _phase = RoomPhase.InMatch;
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.MatchStarted, _players[0].Id));
            StartRound(now, r);
        }

        /// <summary>After a previous match: the player after its first giver. First match: a random connected player.</summary>
        private int FirstGiverIndex()
        {
            int anchor = IndexOf(_firstGiverAnchorId);
            if (anchor >= 0) return (anchor + 1) % _players.Count;
            var connected = new List<int>();
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].Connected) connected.Add(i);
            return connected[_rng.Next(0, connected.Count)];
        }

        private void DoHostEndMatch(PlayerState p, long now, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            if (_phase != RoomPhase.InMatch) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            EndMatch(now, r, ReasonCodes.HostEnded);
        }

        private void DoRematch(PlayerState p, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            if (_phase != RoomPhase.MatchEnd) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            _phase = RoomPhase.Lobby;
            _m = null;
            foreach (var q in _players)
            {
                q.Score = 0;
                q.TimesAsGiver = 0;
                q.PendingJoin = false;
                q.Ready = false;
            }
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.ReturnedToLobby, p.Id));
        }

        private void EndMatch(long now, EngineResult r, string reason)
        {
            var m = _m;
            _phase = RoomPhase.MatchEnd;
            // A round interrupted by HostEndMatch is dropped: nothing of it is shown or scored.
            m.Phase = MatchPhase.Reveal;
            if (m.LastRound == null && m.History.Count > 0) m.LastRound = m.History[m.History.Count - 1];
            m.PhaseEndsAtMs = null;
            m.Overtime = false;
            m.Paused = false;
            m.GraceActive = false;
            m.CurrentTurnPlayerId = null;
            m.TurnQueue.Clear();
            m.Selections.Clear();
            if (m.ImmediateWinnerId != null)
            {
                m.Winners = new List<string> { m.ImmediateWinnerId };
            }
            else
            {
                var scores = new List<KeyValuePair<string, int>>(_players.Count);
                foreach (var q in _players) scores.Add(new KeyValuePair<string, int>(q.Id, q.Score));
                m.Winners = Scoring.Winners(scores);
            }
            foreach (var q in _players) q.Ready = false;
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.MatchEnded, null, string.Join(",", m.Winners), m.Winners.Count, reason));
        }
    }
}
