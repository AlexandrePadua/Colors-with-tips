using System;
using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Protocol;
using Pitacor.Core.Rules;
using Pitacor.Core.Text;

namespace Pitacor.Core.Match
{
    // Round flow: phases, timers, pause, giver grace, guesses, clues, reveal and scoring.
    public sealed partial class RoomEngine
    {
        public const string ForcedCode = "forced";
        private const int SettleGuard = 64;

        // ------------------------------------------------------------------ settle loop

        /// <summary>
        /// Applies every state-driven consequence up to <paramref name="now"/>: pause/resume, giver grace, skipped turns,
        /// early phase ends and timer expirations. Phases started here begin at <paramref name="now"/>.
        /// </summary>
        private void Settle(long now, EngineResult r)
        {
            if (_phase != RoomPhase.InMatch) return;
            UpdatePause(now, r);
            for (int i = 0; i < SettleGuard; i++)
            {
                if (_phase != RoomPhase.InMatch || _m.Paused) return;
                if (!Step(now, r)) return;
                r.StateChanged = true;
            }
        }

        /// <summary>Applies one pending consequence. Returns false when nothing is due.</summary>
        private bool Step(long now, EngineResult r)
        {
            var m = _m;
            switch (m.Phase)
            {
                case MatchPhase.RoundIntro:
                    if (now < m.PhaseEndsAtMs) return false;
                    EnterGiverChoosing(now, r);
                    return true;
                case MatchPhase.GiverChoosing:
                case MatchPhase.Clue2Decision:
                    return StepGiverPhase(now, r);
                case MatchPhase.Guessing1:
                case MatchPhase.Guessing2:
                    return _settings.GuessTiming == GuessTiming.Simultaneous ? StepSimultaneous(now, r) : StepTurnBased(now, r);
                case MatchPhase.Reveal:
                    if (now < m.RevealMinEndsAtMs || (now < m.PhaseEndsAtMs && !AllConnectedReady())) return false;
                    EndReveal(now, r);
                    return true;
            }
            return false;
        }

        /// <summary>Pauses the match while fewer than MinPlayers members are connected (pending joiners count: they play
        /// from the next round). Resuming shifts every deadline by the pause length.</summary>
        private void UpdatePause(long now, EngineResult r)
        {
            var m = _m;
            bool shortHanded = ConnectedCount < MinPlayers;
            if (shortHanded && !m.Paused)
            {
                m.Paused = true;
                m.PausedAtMs = now;
                r.StateChanged = true;
                r.AddPublic(Ev(EventTypes.GamePaused));
            }
            else if (!shortHanded && m.Paused)
            {
                long delta = now - m.PausedAtMs;
                if (m.PhaseEndsAtMs.HasValue) m.PhaseEndsAtMs = m.PhaseEndsAtMs.Value + delta;
                m.RevealMinEndsAtMs += delta;
                if (m.GraceActive) m.GraceEndsAtMs += delta;
                m.Paused = false;
                r.StateChanged = true;
                r.AddPublic(Ev(EventTypes.GameResumed));
            }
        }

        /// <summary>Start time for new timers: while paused, the pause start (the resume shift then lands them right).</summary>
        private long Clock(long now) => _m.Paused ? _m.PausedAtMs : now;

        private long? DeadlineIn(long now, int seconds) => seconds > 0 ? Clock(now) + seconds * 1000L : (long?)null;

        private void EnterOvertime(EngineResult r)
        {
            _m.Overtime = true;
            _m.PhaseEndsAtMs = null;
            r.AddPublic(Ev(EventTypes.Overtime, _m.CurrentTurnPlayerId, null, _m.RoundNumber, _m.Phase.ToString()));
        }

        private void AnnouncePhase(EngineResult r)
        {
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.PhaseChanged, null, null, _m.RoundNumber, _m.Phase.ToString()));
        }

        // ------------------------------------------------------------------ round start

        private void StartRound(long now, EngineResult r)
        {
            var m = _m;
            if (_players.Count == 0) return;
            m.ResetRound();
            m.RoundNumber++;

            var activated = new List<string>();
            foreach (var q in _players)
            {
                q.Ready = false;
                if (!q.PendingJoin) continue;
                q.PendingJoin = false;
                activated.Add(q.Id);
            }

            int idx = m.LastGiverIndex + 1;
            if (idx >= _players.Count)
            {
                idx = 0;
                m.PassNumber++;
            }
            // Joiners sit at the end of the list, so they give during the current pass: credit the passes they missed.
            foreach (var id in activated) m.GiverCredit[id] = m.PassNumber - 1;

            var giver = _players[idx];
            m.LastGiverIndex = idx;
            m.GiverId = giver.Id;
            giver.TimesAsGiver++;
            for (int k = 1; k < _players.Count; k++) m.Guessers.Add(_players[(idx + k) % _players.Count].Id);
            m.PlayersAtRoundStart = _players.Count;

            int spread = _settings.UltraHard ? RoomLimits.OptionSpreadUltraHard : RoomLimits.OptionSpreadNormal;
            m.Options = OptionPicker.Pick(Board, RoomLimits.OptionCount, spread, m.UsedTargets, _rng);

            m.Phase = MatchPhase.RoundIntro;
            m.PhaseEndsAtMs = Clock(now) + MatchTimings.RoundIntroMs;
            r.AddPublic(Ev(EventTypes.RoundStarted, giver.Id, null, m.RoundNumber));
            AnnouncePhase(r);
        }

        private void EnterGiverChoosing(long now, EngineResult r)
        {
            _m.Phase = MatchPhase.GiverChoosing;
            _m.Overtime = false;
            _m.PhaseEndsAtMs = DeadlineIn(now, _settings.ClueTimerSeconds);
            AnnouncePhase(r);
        }

        // ------------------------------------------------------------------ giver phases (clue 1, clue 2 decision)

        private bool StepGiverPhase(long now, EngineResult r)
        {
            var m = _m;
            var giver = Find(m.GiverId);
            if (giver == null) return false; // cannot happen: a leaving giver voids the round
            if (!giver.Connected && !m.GraceActive)
            {
                m.GraceActive = true;
                m.GraceSavedRemainingMs = m.PhaseEndsAtMs.HasValue ? Math.Max(0, m.PhaseEndsAtMs.Value - now) : (long?)null;
                m.PhaseEndsAtMs = null;
                // The tolerance counts from the drop, so a giver gone for long loses the turn right away.
                m.GraceEndsAtMs = giver.DisconnectedAtMs + MatchTimings.GiverGraceMs;
                return true;
            }
            if (giver.Connected && m.GraceActive)
            {
                m.GraceActive = false;
                m.PhaseEndsAtMs = m.GraceSavedRemainingMs.HasValue ? now + m.GraceSavedRemainingMs.Value : (long?)null;
                m.GraceSavedRemainingMs = null;
                return true;
            }
            if (m.GraceActive)
            {
                if (now < m.GraceEndsAtMs) return false;
                ForceGiverPhase(now, r, ReasonCodes.GiverAway);
                return true;
            }
            if (!m.PhaseEndsAtMs.HasValue || now < m.PhaseEndsAtMs.Value) return false;
            if (_settings.TimerExpiry == TimerExpiry.Force) ForceGiverPhase(now, r, ReasonCodes.Timeout);
            else EnterOvertime(r);
            return true;
        }

        /// <summary>Force consequence: no first clue voids the round; no decision on the second clue passes without bonus.</summary>
        private void ForceGiverPhase(long now, EngineResult r, string reason)
        {
            if (_m.Phase == MatchPhase.GiverChoosing) VoidRound(now, r, reason);
            else PassClue2(now, r, false, reason);
        }

        private ClueContext ClueContextFor(List<string> previousClues)
        {
            var nicknames = new List<string>(_players.Count);
            foreach (var q in _players) nicknames.Add(q.Nickname);
            return new ClueContext
            {
                Language = _settings.Language,
                BlockLightDarkDirectionsNames = _settings.BlockLightDarkDirectionsNames,
                FilterProfanity = _settings.IsPublic,
                UltraHard = _settings.UltraHard,
                PlayerNicknames = nicknames,
                PreviousClues = previousClues,
            };
        }

        private static string ClueRejectDetail(ClueValidationResult res) =>
            (res == null ? ClueRejectReason.Empty : res.Reason) + ":" + (res == null ? null : res.OffendingWord);

        /// <summary>Common checks for round commands. Returns false (with the error added) when the command is not allowed.</summary>
        private bool CheckRound(PlayerState p, EngineResult r, MatchPhase phase, bool giverOnly)
        {
            if (_phase != RoomPhase.InMatch || _m.Phase != phase) { Reject(r, p, ErrorCodes.WrongPhase); return false; }
            if (giverOnly && p.Id != _m.GiverId) { Reject(r, p, ErrorCodes.NotGiver); return false; }
            if (_m.Paused) { Reject(r, p, ErrorCodes.Paused); return false; }
            return true;
        }

        private void DoSubmitClue1(PlayerState p, ClientCommand cmd, long now, EngineResult r)
        {
            if (!CheckRound(p, r, MatchPhase.GiverChoosing, true)) return;
            var m = _m;
            if (!cmd.OptionIndex.HasValue || cmd.OptionIndex.Value < 0 || cmd.OptionIndex.Value >= m.Options.Count)
            {
                Reject(r, p, ErrorCodes.InvalidCommand, "OptionIndex");
                return;
            }
            var res = ClueValidator.Validate(cmd.Text, ClueKind.First, ClueContextFor(new List<string>(m.UsedClues)));
            if (res == null || !res.Ok) { Reject(r, p, ErrorCodes.InvalidClue, ClueRejectDetail(res)); return; }

            var target = m.Options[cmd.OptionIndex.Value];
            m.Target = target;
            m.TargetHex = Palette.GetHex(Board, target);
            m.UsedTargets.Add(target);
            m.Clue1 = res.Display;
            m.Clue1Normalized = res.Normalized;
            m.UsedClues.Add(res.Normalized);
            r.AddPublic(Ev(EventTypes.ClueGiven, p.Id, m.Clue1, 1));
            EnterGuessing(MatchPhase.Guessing1, now, r);
        }

        private void DoSubmitClue2(PlayerState p, ClientCommand cmd, long now, EngineResult r)
        {
            if (!CheckRound(p, r, MatchPhase.Clue2Decision, true)) return;
            var m = _m;
            // The second clue may repeat the round's own first clue, not any other clue of the match.
            var previous = new List<string>(m.UsedClues);
            previous.Remove(m.Clue1Normalized);
            var res = ClueValidator.Validate(cmd.Text, ClueKind.Second, ClueContextFor(previous));
            if (res == null || !res.Ok) { Reject(r, p, ErrorCodes.InvalidClue, ClueRejectDetail(res)); return; }

            m.Clue2 = res.Display;
            m.Clue2Normalized = res.Normalized;
            m.UsedClues.Add(res.Normalized);
            r.AddPublic(Ev(EventTypes.ClueGiven, p.Id, m.Clue2, 2));
            EnterGuessing(MatchPhase.Guessing2, now, r);
        }

        private void DoPassClue2(PlayerState p, long now, EngineResult r)
        {
            if (!CheckRound(p, r, MatchPhase.Clue2Decision, true)) return;
            PassClue2(now, r, true, ReasonCodes.Explicit);
        }

        /// <summary>Only an explicit pass earns the bonus; timeouts, drops and host skips do not.</summary>
        private void PassClue2(long now, EngineResult r, bool bonus, string reason)
        {
            _m.Clue2Passed = true;
            _m.BonusApplied = bonus;
            r.AddPublic(Ev(EventTypes.Clue2Passed, _m.GiverId, null, bonus ? 1 : 0, reason));
            EnterReveal(now, r);
        }

        private void EnterClue2Decision(long now, EngineResult r)
        {
            _m.Phase = MatchPhase.Clue2Decision;
            _m.Overtime = false;
            _m.PhaseEndsAtMs = DeadlineIn(now, _settings.ClueTimerSeconds);
            AnnouncePhase(r);
        }

        // ------------------------------------------------------------------ guessing

        private static bool IsGuessingPhase(MatchPhase phase) => phase == MatchPhase.Guessing1 || phase == MatchPhase.Guessing2;

        private void EnterGuessing(MatchPhase phase, long now, EngineResult r)
        {
            var m = _m;
            m.Phase = phase;
            m.Overtime = false;
            // The giver grace belongs to the giver phases; a clue accepted while it was active must not carry it over.
            m.GraceActive = false;
            m.GraceSavedRemainingMs = null;
            m.Confirmed.Clear();
            m.Selections.Clear();
            m.SkippedTurns.Clear();
            m.TurnQueue.Clear();
            m.CurrentTurnPlayerId = null;
            AnnouncePhase(r);
            if (_settings.GuessTiming == GuessTiming.Simultaneous)
            {
                m.PhaseEndsAtMs = DeadlineIn(now, _settings.GuessTimerSeconds);
                return;
            }
            // Clockwise from the player after the giver; the second lap runs counter-clockwise from the one before.
            m.TurnQueue.AddRange(m.Guessers);
            if (phase == MatchPhase.Guessing2) m.TurnQueue.Reverse();
            AdvanceTurn(now, r);
        }

        private bool StepSimultaneous(long now, EngineResult r)
        {
            var m = _m;
            if (AllConnectedGuessersConfirmed())
            {
                CloseGuessing(now, r, false);
                return true;
            }
            if (!m.PhaseEndsAtMs.HasValue || now < m.PhaseEndsAtMs.Value) return false;
            if (_settings.TimerExpiry == TimerExpiry.Force) CloseGuessing(now, r, true);
            else EnterOvertime(r);
            return true;
        }

        private bool StepTurnBased(long now, EngineResult r)
        {
            var m = _m;
            if (m.SkippedTurns.Count > 0)
            {
                foreach (var id in m.Guessers)
                {
                    if (!m.SkippedTurns.Contains(id) || !IsConnected(id) || m.Confirmed.Contains(id)) continue;
                    m.SkippedTurns.Remove(id);
                    m.TurnQueue.Add(id);
                    r.StateChanged = true;
                }
            }

            string current = m.CurrentTurnPlayerId;
            if (current == null || m.Confirmed.Contains(current) || !m.Guessers.Contains(current))
            {
                AdvanceTurn(now, r);
                return true;
            }
            if (!IsConnected(current))
            {
                m.SkippedTurns.Add(current);
                AdvanceTurn(now, r);
                return true;
            }
            if (!m.PhaseEndsAtMs.HasValue || now < m.PhaseEndsAtMs.Value) return false;
            if (_settings.TimerExpiry == TimerExpiry.Force)
            {
                AutoConfirm(current, r);
                AdvanceTurn(now, r);
            }
            else
            {
                EnterOvertime(r);
            }
            return true;
        }

        /// <summary>Gives the turn to the next connected guesser in the queue, or closes the phase when nobody is left.</summary>
        private void AdvanceTurn(long now, EngineResult r)
        {
            var m = _m;
            m.CurrentTurnPlayerId = null;
            m.Overtime = false;
            m.PhaseEndsAtMs = null;
            while (m.TurnQueue.Count > 0)
            {
                string id = m.TurnQueue[0];
                m.TurnQueue.RemoveAt(0);
                if (!m.Guessers.Contains(id) || m.Confirmed.Contains(id)) continue;
                if (!IsConnected(id))
                {
                    m.SkippedTurns.Add(id);
                    continue;
                }
                m.CurrentTurnPlayerId = id;
                m.PhaseEndsAtMs = DeadlineIn(now, _settings.GuessTimerSeconds);
                r.StateChanged = true;
                r.AddPublic(Ev(EventTypes.TurnChanged, id, null, m.RoundNumber, m.Phase.ToString()));
                return;
            }
            CloseGuessing(now, r, false);
        }

        private bool AllConnectedGuessersConfirmed()
        {
            foreach (var id in _m.Guessers)
                if (!_m.Confirmed.Contains(id) && IsConnected(id)) return false;
            return true;
        }

        /// <summary>
        /// Ends the guessing phase. Only a forced end (timer with Force, or HostSkipPhase) confirms the free provisional
        /// cell of unconfirmed guessers; an early end (every connected guesser confirmed) leaves them without a marker.
        /// </summary>
        private void CloseGuessing(long now, EngineResult r, bool forced)
        {
            var m = _m;
            if (forced)
                foreach (var id in m.Guessers)
                    if (!m.Confirmed.Contains(id)) AutoConfirm(id, r);
            m.CurrentTurnPlayerId = null;
            m.TurnQueue.Clear();
            m.SkippedTurns.Clear();
            m.Selections.Clear();
            if (m.Phase == MatchPhase.Guessing1) EnterClue2Decision(now, r);
            else EnterReveal(now, r);
        }

        private void AutoConfirm(string playerId, EngineResult r)
        {
            if (!_m.Selections.TryGetValue(playerId, out var cell)) return;
            _m.Selections.Remove(playerId);
            if (IsOccupied(cell)) return;
            PlaceMarker(playerId, cell, true, r);
        }

        private bool IsOccupied(BoardCoord cell)
        {
            foreach (var marker in _m.Markers)
                if (marker.Coord == cell) return true;
            return false;
        }

        private void PlaceMarker(string playerId, BoardCoord cell, bool forced, EngineResult r)
        {
            var m = _m;
            var marker = new MarkerView { PlayerId = playerId, Coord = cell, Index = m.Phase == MatchPhase.Guessing2 ? 2 : 1 };
            m.Markers.Add(marker);
            m.Confirmed.Add(playerId);
            m.Selections.Remove(playerId);
            r.StateChanged = true;

            var full = Ev(EventTypes.GuessConfirmed, playerId, null, marker.Index, forced ? ForcedCode : null);
            full.Coord = cell;
            var redacted = full.Clone();
            redacted.Coord = null;
            r.AddRestricted(full, redacted, LiveMarkerViewers(playerId));
        }

        /// <summary>Who may see a marker of <paramref name="ownerId"/> before the reveal (spec 4.5).</summary>
        private HashSet<string> LiveMarkerViewers(string ownerId)
        {
            var viewers = new HashSet<string> { _m.GiverId, ownerId };
            if (_settings.GuessVisibility == GuessVisibility.Visible)
                foreach (var id in _m.Guessers) viewers.Add(id);
            return viewers;
        }

        /// <summary>Checks shared by SelectCell and ConfirmGuess. Returns the cell, or null after adding the error.</summary>
        private BoardCoord? CheckGuess(PlayerState p, ClientCommand cmd, EngineResult r, bool confirming)
        {
            var m = _m;
            if (_phase != RoomPhase.InMatch || !IsGuessingPhase(m.Phase)) { Reject(r, p, ErrorCodes.WrongPhase); return null; }
            if (!m.Guessers.Contains(p.Id)) { Reject(r, p, ErrorCodes.NotGuesser); return null; }
            if (m.Paused) { Reject(r, p, ErrorCodes.Paused); return null; }
            if (m.Confirmed.Contains(p.Id)) { Reject(r, p, ErrorCodes.AlreadyConfirmed); return null; }
            if (confirming && _settings.GuessTiming == GuessTiming.TurnBased && m.CurrentTurnPlayerId != p.Id)
            {
                Reject(r, p, ErrorCodes.NotYourTurn);
                return null;
            }
            if (!cmd.Coord.HasValue) { Reject(r, p, ErrorCodes.InvalidCommand, "Coord"); return null; }
            var cell = cmd.Coord.Value;
            if (!BoardGeometry.Contains(Board, cell)) { Reject(r, p, ErrorCodes.OutOfBoard); return null; }
            if (confirming && IsOccupied(cell)) { Reject(r, p, ErrorCodes.CellOccupied); return null; }
            return cell;
        }

        private void DoSelectCell(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            var cell = CheckGuess(p, cmd, r, false);
            if (!cell.HasValue) return;
            _m.Selections[p.Id] = cell.Value;
            r.StateChanged = true;
        }

        private void DoConfirmGuess(PlayerState p, ClientCommand cmd, EngineResult r)
        {
            var cell = CheckGuess(p, cmd, r, true);
            if (!cell.HasValue) return;
            PlaceMarker(p.Id, cell.Value, false, r);
            // Phase/turn advance happens in Settle.
        }

        // ------------------------------------------------------------------ host controls during the round

        private void DoHostSkipPhase(PlayerState p, long now, EngineResult r)
        {
            if (!IsHost(p)) { Reject(r, p, ErrorCodes.NotHost); return; }
            var m = _m;
            if (_phase != RoomPhase.InMatch || m.Phase == MatchPhase.RoundIntro || m.Phase == MatchPhase.Reveal)
            {
                Reject(r, p, ErrorCodes.WrongPhase);
                return;
            }
            if (m.Paused) { Reject(r, p, ErrorCodes.Paused); return; }
            // Allowed once nothing is counting down: overtime (WarnOnly), no time limit, or the giver grace.
            if (m.PhaseEndsAtMs.HasValue) { Reject(r, p, ErrorCodes.NotAllowed, "TimerRunning"); return; }

            r.StateChanged = true;
            switch (m.Phase)
            {
                case MatchPhase.GiverChoosing:
                    VoidRound(now, r, ReasonCodes.HostSkipped);
                    break;
                case MatchPhase.Clue2Decision:
                    PassClue2(now, r, false, ReasonCodes.HostSkipped);
                    break;
                default:
                    if (_settings.GuessTiming == GuessTiming.Simultaneous)
                    {
                        CloseGuessing(now, r, true);
                    }
                    else
                    {
                        if (m.CurrentTurnPlayerId != null) AutoConfirm(m.CurrentTurnPlayerId, r);
                        AdvanceTurn(now, r);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ reveal and scoring

        private RoundResult NewRoundResult()
        {
            var m = _m;
            var result = new RoundResult
            {
                RoundNumber = m.RoundNumber,
                GiverId = m.GiverId,
                Target = m.Target,
                TargetHex = m.TargetHex,
                Clue1 = m.Clue1,
                Clue2 = m.Clue2,
                Clue2Passed = m.Clue2Passed,
                BonusApplied = m.BonusApplied,
            };
            var board = Board;
            for (int i = 0; i < m.Options.Count; i++)
                result.Options.Add(new OptionView { Index = i, Coord = m.Options[i], Hex = Palette.GetHex(board, m.Options[i]) });
            foreach (var marker in m.Markers) result.Markers.Add(marker.Clone());
            return result;
        }

        private void BeginReveal(long now, long minMs, long maxMs, RoundResult result)
        {
            var m = _m;
            m.History.Add(result);
            m.LastRound = result;
            m.Phase = MatchPhase.Reveal;
            m.Overtime = false;
            m.GraceActive = false;
            m.GraceSavedRemainingMs = null;
            m.CurrentTurnPlayerId = null;
            m.TurnQueue.Clear();
            m.SkippedTurns.Clear();
            m.Selections.Clear();
            long start = Clock(now);
            m.RevealMinEndsAtMs = start + minMs;
            m.PhaseEndsAtMs = start + maxMs;
            foreach (var q in _players) q.Ready = false;
        }

        private void EnterReveal(long now, EngineResult r)
        {
            var m = _m;
            var result = NewRoundResult();
            var input = new ScoringInput
            {
                Target = m.Target.Value,
                GiverId = m.GiverId,
                GuesserIds = new List<string>(m.Guessers),
                PlayersAtRoundStart = m.PlayersAtRoundStart,
                Markers = m.Markers,
                BonusApplied = m.BonusApplied,
            };
            var before = new Dictionary<string, int>();
            foreach (var q in _players) before[q.Id] = q.Score;
            var outcome = Scoring.ScoreRound(input, before, _settings);
            foreach (var q in _players)
                if (outcome.ScoresAfter.TryGetValue(q.Id, out int score)) q.Score = score;
            result.Lines.AddRange(outcome.Lines);

            switch (_settings.EndMode)
            {
                case EndMode.Immediate:
                    if (outcome.ImmediateWinnerId != null)
                    {
                        m.EndAfterReveal = true;
                        m.ImmediateWinnerId = outcome.ImmediateWinnerId;
                    }
                    break;
                case EndMode.EndOfRound:
                    if (outcome.TargetReached) m.EndAfterReveal = true;
                    break;
                case EndMode.CompleteCycle:
                    if (outcome.TargetReached && m.CycleMembers == null) FreezeCycle();
                    break;
            }

            BeginReveal(now, MatchTimings.RevealMinMs, MatchTimings.RevealMaxMs, result);
            AnnouncePhase(r);
            r.AddPublic(Ev(EventTypes.RoundScored, m.GiverId, null, m.RoundNumber));
        }

        /// <summary>Nobody scores; the giver's turn still counts (TimesAsGiver was incremented at round start).</summary>
        private void VoidRound(long now, EngineResult r, string reason)
        {
            var result = NewRoundResult();
            result.Voided = true;
            result.VoidReason = reason;
            BeginReveal(now, MatchTimings.VoidedRevealMs, MatchTimings.VoidedRevealMs, result);
            r.AddPublic(Ev(EventTypes.RoundVoided, result.GiverId, null, result.RoundNumber, reason));
            AnnouncePhase(r);
        }

        private bool AllConnectedReady()
        {
            foreach (var q in _players)
                if (q.Connected && !q.Ready) return false;
            return true;
        }

        private void DoSetReady(PlayerState p, EngineResult r)
        {
            if (_phase != RoomPhase.InMatch || _m.Phase != MatchPhase.Reveal) { Reject(r, p, ErrorCodes.WrongPhase); return; }
            if (p.Ready) return;
            p.Ready = true;
            r.StateChanged = true;
        }

        private void EndReveal(long now, EngineResult r)
        {
            if (_m.EndAfterReveal) EndMatch(now, r, ReasonCodes.TargetReached);
            else if (_settings.EndMode == EndMode.CompleteCycle && CycleComplete()) EndMatch(now, r, ReasonCodes.CycleComplete);
            else StartRound(now, r);
        }

        private int AdjustedGiverTimes(PlayerState q) =>
            q.TimesAsGiver + (_m.GiverCredit.TryGetValue(q.Id, out int credit) ? credit : 0);

        /// <summary>CompleteCycle: freezes the active rotation and the giver count every member must reach.</summary>
        private void FreezeCycle()
        {
            var m = _m;
            m.CycleMembers = new List<string>();
            m.CycleGiverTimes = 0;
            foreach (var q in _players)
            {
                if (q.PendingJoin) continue;
                m.CycleMembers.Add(q.Id);
                m.CycleGiverTimes = Math.Max(m.CycleGiverTimes, AdjustedGiverTimes(q));
            }
        }

        private bool CycleComplete()
        {
            var m = _m;
            if (m.CycleMembers == null) return false;
            foreach (var id in m.CycleMembers)
            {
                var q = Find(id);
                if (q != null && AdjustedGiverTimes(q) < m.CycleGiverTimes) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ players leaving mid-match

        private void OnPlayerRemovedFromMatch(PlayerState p, long now, EngineResult r)
        {
            var m = _m;
            if (m.Phase == MatchPhase.Reveal) return; // the round is settled; their line stays in the result
            if (p.Id == m.GiverId)
            {
                VoidRound(now, r, ReasonCodes.GiverLeft);
                return;
            }
            m.Guessers.Remove(p.Id);
            m.TurnQueue.Remove(p.Id);
            m.SkippedTurns.Remove(p.Id);
            m.Selections.Remove(p.Id);
            m.Confirmed.Remove(p.Id);
            m.Markers.RemoveAll(x => x.PlayerId == p.Id);
            // A departed current-turn player is replaced in Settle.
        }
    }
}
