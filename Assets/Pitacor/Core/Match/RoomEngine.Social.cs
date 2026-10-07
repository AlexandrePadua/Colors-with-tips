using System.Collections.Generic;
using Pitacor.Core.Protocol;
using Pitacor.Core.Text;

namespace Pitacor.Core.Match
{
    // Chat, reactions and moderation (spec 4.6).
    public sealed partial class RoomEngine
    {
        /// <summary>The giver stays silent from choosing the color until the reveal.</summary>
        private bool IsGiverSilenced(PlayerState p)
        {
            if (_phase != RoomPhase.InMatch || p.Id != _m.GiverId) return false;
            var phase = _m.Phase;
            return phase == MatchPhase.GiverChoosing || phase == MatchPhase.Guessing1
                || phase == MatchPhase.Clue2Decision || phase == MatchPhase.Guessing2;
        }

        private void DoSendChat(PlayerState p, ClientCommand cmd, long now, EngineResult r)
        {
            string text = (cmd.Text ?? string.Empty).Trim();
            if (text.Length == 0) { Reject(r, p, ErrorCodes.InvalidCommand, "Text"); return; }
            if (text.Length > RoomLimits.ChatMaxLength) { Reject(r, p, ErrorCodes.InvalidCommand, "TooLong"); return; }
            if (IsGiverSilenced(p)) { Reject(r, p, ErrorCodes.ChatLockedForGiver); return; }

            string masked = MaskChat(text);
            var message = new ChatMessageView { Id = ++_chatCounter, PlayerId = p.Id, Text = masked, AtMs = now };
            AddChat(message);
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.ChatMessage, p.Id, masked, message.Id));
        }

        /// <summary>
        /// While a clue may be on the table (GiverChoosing to Guessing2): masks the current clue words and basic color
        /// names of the room language. Public rooms: masks profanity at all times.
        /// </summary>
        private string MaskChat(string text)
        {
            string result = text;
            if (_phase == RoomPhase.InMatch && _m.Phase != MatchPhase.RoundIntro && _m.Phase != MatchPhase.Reveal)
            {
                var clueWords = new List<string>();
                AddWords(clueWords, _m.Clue1Normalized);
                AddWords(clueWords, _m.Clue2Normalized);
                if (clueWords.Count > 0) result = TextFilter.MaskWords(result, clueWords);
                // Shared word rules: hyphenated words ("verde-agua") and combining accents are one word.
                result = TextFilter.MaskColorNames(result, _settings.Language);
            }
            if (_settings.IsPublic) result = TextFilter.MaskProfanity(result, _settings.Language);
            return result;
        }

        private static void AddWords(List<string> words, string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return;
            foreach (var w in normalized.Split(' '))
                if (w.Length > 0) words.Add(w);
        }

        private void DoSendReaction(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            if (!ReactionCodes.IsValid(cmd.ReactionCode)) { Reject(r, p, ErrorCodes.InvalidCommand, "ReactionCode"); return; }
            if (IsGiverSilenced(p)) { Reject(r, p, ErrorCodes.ChatLockedForGiver); return; }
            r.AddPublic(Ev(EventTypes.Reaction, p.Id, null, 0, cmd.ReactionCode));
        }

        /// <summary>Resolves the target of a moderation command, or adds the error and returns null.</summary>
        private PlayerState ModerationTarget(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            var target = Find(cmd.TargetPlayerId);
            if (target == null) { Reject(r, p, ErrorCodes.PlayerNotFound); return null; }
            if (target == p) { Reject(r, p, ErrorCodes.NotAllowed, "Self"); return null; }
            return target;
        }

        private void DoKick(PlayerState p, ClientCommand cmd, long now, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            var target = ModerationTarget(p, cmd, r);
            if (target == null) return;
            BanAndRemove(target, ReasonCodes.KickedByHost, now, r);
        }

        /// <summary>Public rooms only. Passes when the votes exceed half of the connected players other than the target.</summary>
        private void DoVoteKick(PlayerState p, ClientCommand cmd, long now, EngineResult r)
        {
            if (!_settings.IsPublic) { Reject(r, p, ErrorCodes.NotAllowed, "PrivateRoom"); return; }
            var target = ModerationTarget(p, cmd, r);
            if (target == null) return;

            bool first = !_voteKicks.TryGetValue(target.Id, out var votes);
            if (first)
            {
                votes = new HashSet<string>();
                _voteKicks[target.Id] = votes;
            }
            if (!votes.Add(p.Id)) return;

            int electorate = 0;
            int count = 0;
            foreach (var q in _players)
            {
                if (q == target || !q.Connected) continue;
                electorate++;
                if (votes.Contains(q.Id)) count++;
            }
            int needed = electorate / 2 + 1;
            if (count >= needed)
            {
                BanAndRemove(target, ReasonCodes.VoteKick, now, r);
                return;
            }
            r.AddPublic(Ev(first ? EventTypes.VoteKickStarted : EventTypes.VoteKickVote, target.Id, null, count, needed.ToString()));
        }

        private void BanAndRemove(PlayerState target, string reason, long now, EngineResult r)
        {
            _banned.Add(target.Id);
            r.AddPublic(Ev(EventTypes.PlayerKicked, target.Id, null, 0, reason));
            AddSystemChat(EventTypes.PlayerKicked, target.Id, now);
            RemovePlayer(target, now, r);
        }

        private void DoReport(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            var target = ModerationTarget(p, cmd, r);
            if (target == null) return;
            string reason = (cmd.Text ?? string.Empty).Trim();
            if (reason.Length > RoomLimits.ReportMaxLength) reason = reason.Substring(0, RoomLimits.ReportMaxLength);
            r.AddServerOnly(Ev(EventTypes.ReportFiled, p.Id, reason, 0, target.Id));
        }
    }
}
