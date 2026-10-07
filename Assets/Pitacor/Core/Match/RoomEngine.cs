using System;
using System.Collections.Generic;
using Pitacor.Core.Board;
using Pitacor.Core.Protocol;
using Pitacor.Core.Rules;
using Pitacor.Core.Text;

namespace Pitacor.Core.Match
{
    /// <summary>
    /// Authoritative state machine of one room: lobby, match flow, timers, scoring, chat and moderation
    /// (docs/tech/core-spec.md section 4). Single-threaded: the server must serialize calls. Deterministic: time only
    /// comes from nowMs and randomness from the injected IRandomSource.
    /// Clients must only receive <see cref="BuildView"/> and <see cref="FilterEventsFor"/>, never raw state or
    /// <see cref="EngineResult.Events"/>.
    /// </summary>
    public sealed partial class RoomEngine
    {
        private readonly IRandomSource _rng;
        private readonly List<PlayerState> _players = new List<PlayerState>();
        private readonly HashSet<string> _banned = new HashSet<string>();
        private readonly List<ChatMessageView> _chat = new List<ChatMessageView>();
        private readonly Dictionary<string, HashSet<string>> _voteKicks = new Dictionary<string, HashSet<string>>();

        private GameSettings _settings;
        private RoomPhase _phase = RoomPhase.Lobby;
        private MatchState _m;
        private string _hostId;
        private long _joinCounter;
        private int _chatCounter;
        private int _version;
        // First giver of the last match: the next match starts with the player after it. When that player leaves, the
        // anchor moves to their predecessor so "the player after it" stays the same.
        private string _firstGiverAnchorId;

        public RoomEngine(string roomCode, RoomOptions options, IRandomSource rng, long nowMs)
        {
            RoomCode = roomCode;
            var opts = options ?? new RoomOptions();
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _settings = (opts.InitialSettings ?? new GameSettings()).Clone().Clamp();
            int min = opts.MinPlayersOverride > 0 ? opts.MinPlayersOverride : GameSettings.MinPlayers;
            MinPlayers = Math.Max(1, Math.Min(GameSettings.MaxPlayers, min));
            LastActivityMs = nowMs;
        }

        public string RoomCode { get; }
        /// <summary>Last Join/Leave/SetConnected/Handle call (Tick does not count).</summary>
        public long LastActivityMs { get; private set; }
        /// <summary>Increases on every state change (same value as RoomView.Version).</summary>
        public int Version => _version;
        public RoomPhase Phase => _phase;
        public string HostId => _hostId;
        /// <summary>Connected players needed to start and to keep a match running.</summary>
        public int MinPlayers { get; }
        public int PlayerCount => _players.Count;
        /// <summary>Players in rotation order. Read only: the engine owns these objects.</summary>
        public IReadOnlyList<PlayerState> Players => _players;
        public GameSettings Settings => _settings.Clone();
        public BoardSize Board => _settings.UltraHard ? BoardSize.UltraHard : BoardSize.Normal;

        public int ConnectedCount
        {
            get
            {
                int n = 0;
                foreach (var p in _players)
                    if (p.Connected) n++;
                return n;
            }
        }

        /// <summary>No connected player: the server may dispose the room once LastActivityMs is old enough.</summary>
        public bool IsEmpty => ConnectedCount == 0;

        public RoomSummary Summary => new RoomSummary
        {
            RoomCode = RoomCode,
            PlayerCount = _players.Count,
            MaxPlayers = GameSettings.MaxPlayers,
            Phase = _phase,
            Language = _settings.Language,
            IsPublic = _settings.IsPublic,
        };

        public bool IsBanned(string playerId) => playerId != null && _banned.Contains(playerId);

        /// <summary>The events of <paramref name="result"/> this player may receive, with hidden data removed.</summary>
        public static List<GameEvent> FilterEventsFor(string playerId, EngineResult result) => result.EventsFor(playerId);

        // ------------------------------------------------------------------ public API

        /// <summary>A new player enters the room. An id already in the room counts as a reconnection.</summary>
        public EngineResult Join(string playerId, string nickname, AvatarSpec avatar, long nowMs)
        {
            var r = Begin(nowMs);
            LastActivityMs = nowMs;
            if (string.IsNullOrEmpty(playerId)) return Fail(nowMs, r, playerId, ErrorCodes.InvalidCommand, "PlayerId");
            if (_banned.Contains(playerId)) return Fail(nowMs, r, playerId, ErrorCodes.Banned, null);

            var existing = Find(playerId);
            if (existing != null)
            {
                SetConnectedCore(existing, true, nowMs, r);
                return Finish(nowMs, r);
            }
            if (_players.Count >= GameSettings.MaxPlayers) return Fail(nowMs, r, playerId, ErrorCodes.RoomFull, null);

            string nick = NicknameRules.Sanitize(nickname, _settings.IsPublic);
            if (string.IsNullOrEmpty(nick)) return Fail(nowMs, r, playerId, ErrorCodes.InvalidNickname, null);

            var p = new PlayerState
            {
                Id = playerId,
                Nickname = UniqueNickname(nick, null),
                Avatar = AvatarSpec.Sanitize(avatar),
                Connected = true,
                JoinOrder = ++_joinCounter,
                PendingJoin = _phase == RoomPhase.InMatch,
            };
            // Appending is "end of the rotation" in a match too: the list is rotated so the pass ends at the last index.
            _players.Add(p);
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.PlayerJoined, p.Id, p.Nickname, p.PendingJoin ? 1 : 0));
            AddSystemChat(EventTypes.PlayerJoined, p.Id, nowMs);
            if (Find(_hostId) == null) SetHost(p, r);
            return Finish(nowMs, r);
        }

        /// <summary>The player left for good (the slot is freed).</summary>
        public EngineResult Leave(string playerId, long nowMs)
        {
            var r = Begin(nowMs);
            LastActivityMs = nowMs;
            var p = Find(playerId);
            if (p == null) return Fail(nowMs, r, playerId, ErrorCodes.PlayerNotFound, null);
            r.AddPublic(Ev(EventTypes.PlayerLeft, p.Id));
            AddSystemChat(EventTypes.PlayerLeft, p.Id, nowMs);
            RemovePlayer(p, nowMs, r);
            return Finish(nowMs, r);
        }

        /// <summary>Connection dropped or came back; the player keeps the slot.</summary>
        public EngineResult SetConnected(string playerId, bool connected, long nowMs)
        {
            var r = Begin(nowMs);
            LastActivityMs = nowMs;
            var p = Find(playerId);
            if (p == null) return Fail(nowMs, r, playerId, ErrorCodes.PlayerNotFound, null);
            SetConnectedCore(p, connected, nowMs, r);
            return Finish(nowMs, r);
        }

        public EngineResult Handle(string playerId, ClientCommand cmd, long nowMs)
        {
            var r = Begin(nowMs);
            LastActivityMs = nowMs;
            var p = Find(playerId);
            if (p == null) return Fail(nowMs, r, playerId, ErrorCodes.PlayerNotFound, null);
            if (cmd == null || string.IsNullOrEmpty(cmd.Type)) return Fail(nowMs, r, playerId, ErrorCodes.InvalidCommand, "Type");

            switch (cmd.Type)
            {
                case CommandTypes.UpdateSettings: DoUpdateSettings(p, cmd, r); break;
                case CommandTypes.ReorderPlayers: DoReorderPlayers(p, cmd, r); break;
                case CommandTypes.StartMatch: DoStartMatch(p, nowMs, r); break;
                case CommandTypes.UpdateProfile: DoUpdateProfile(p, cmd, r); break;
                case CommandTypes.Rematch: DoRematch(p, r); break;
                case CommandTypes.HostEndMatch: DoHostEndMatch(p, nowMs, r); break;
                case CommandTypes.HostSkipPhase: DoHostSkipPhase(p, nowMs, r); break;
                case CommandTypes.SubmitClue1: DoSubmitClue1(p, cmd, nowMs, r); break;
                case CommandTypes.SelectCell: DoSelectCell(p, cmd, r); break;
                case CommandTypes.ConfirmGuess: DoConfirmGuess(p, cmd, r); break;
                case CommandTypes.SubmitClue2: DoSubmitClue2(p, cmd, nowMs, r); break;
                case CommandTypes.PassClue2: DoPassClue2(p, nowMs, r); break;
                case CommandTypes.SetReady: DoSetReady(p, r); break;
                case CommandTypes.SendChat: DoSendChat(p, cmd, nowMs, r); break;
                case CommandTypes.SendReaction: DoSendReaction(p, cmd, r); break;
                case CommandTypes.Kick: DoKick(p, cmd, nowMs, r); break;
                case CommandTypes.VoteKick: DoVoteKick(p, cmd, nowMs, r); break;
                case CommandTypes.Report: DoReport(p, cmd, r); break;
                default: Reject(r, p, ErrorCodes.InvalidCommand, "UnknownType"); break;
            }
            return Finish(nowMs, r);
        }

        /// <summary>Applies timer expirations. Call periodically (the server uses 200 ms).</summary>
        public EngineResult Tick(long nowMs)
        {
            var r = Begin(nowMs);
            return Finish(nowMs, r);
        }

        // ------------------------------------------------------------------ call plumbing

        /// <summary>Every call first applies whatever expired up to nowMs, so late actions are judged on the server clock.</summary>
        private EngineResult Begin(long now)
        {
            var r = new EngineResult();
            CheckHostGrace(now, r);
            Settle(now, r);
            return r;
        }

        private EngineResult Finish(long now, EngineResult r)
        {
            Settle(now, r);
            if (r.StateChanged) _version++;
            return r;
        }

        private EngineResult Fail(long now, EngineResult r, string playerId, string code, string detail)
        {
            r.AddError(playerId, code, detail);
            return Finish(now, r);
        }

        private static void Reject(EngineResult r, PlayerState p, string code, string detail = null) => r.AddError(p.Id, code, detail);

        private static GameEvent Ev(string type, string playerId = null, string text = null, int value = 0, string code = null) =>
            new GameEvent { Type = type, PlayerId = playerId, Text = text, Value = value, Code = code };

        // ------------------------------------------------------------------ players and host

        private PlayerState Find(string id)
        {
            if (id == null) return null;
            foreach (var p in _players)
                if (p.Id == id) return p;
            return null;
        }

        private int IndexOf(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].Id == id) return i;
            return -1;
        }

        private bool IsHost(PlayerState p) => p.Id == _hostId;

        private bool IsConnected(string id)
        {
            var p = Find(id);
            return p != null && p.Connected;
        }

        private void SetHost(PlayerState p, EngineResult r)
        {
            foreach (var q in _players) q.IsHost = false;
            p.IsHost = true;
            _hostId = p.Id;
            r.StateChanged = true;
            r.AddPublic(Ev(EventTypes.HostChanged, p.Id));
        }

        /// <summary>Oldest (lowest JoinOrder) player, optionally only among connected ones.</summary>
        private PlayerState OldestPlayer(string excludeId, bool connectedOnly)
        {
            PlayerState best = null;
            foreach (var p in _players)
            {
                if (p.Id == excludeId || (connectedOnly && !p.Connected)) continue;
                if (best == null || p.JoinOrder < best.JoinOrder) best = p;
            }
            return best;
        }

        /// <summary>A host that stays disconnected for HostGraceMs hands the role to the oldest connected player.</summary>
        private void CheckHostGrace(long now, EngineResult r)
        {
            var host = Find(_hostId);
            if (host == null || host.Connected || now - host.DisconnectedAtMs < MatchTimings.HostGraceMs) return;
            var next = OldestPlayer(host.Id, true);
            if (next != null) SetHost(next, r);
        }

        private void SetConnectedCore(PlayerState p, bool connected, long now, EngineResult r)
        {
            if (p.Connected == connected) return;
            p.Connected = connected;
            if (!connected) p.DisconnectedAtMs = now;
            r.StateChanged = true;
            r.AddPublic(Ev(connected ? EventTypes.PlayerConnected : EventTypes.PlayerDisconnected, p.Id));
            // Match consequences (giver grace, skipped turns, pause, early phase end) are applied by Settle.
        }

        private void RemovePlayer(PlayerState p, long now, EngineResult r)
        {
            int idx = _players.IndexOf(p);
            if (_firstGiverAnchorId == p.Id)
                _firstGiverAnchorId = _players.Count > 1 ? _players[(idx - 1 + _players.Count) % _players.Count].Id : null;
            _players.RemoveAt(idx);
            if (_m != null && _phase == RoomPhase.InMatch)
            {
                if (idx <= _m.LastGiverIndex) _m.LastGiverIndex--;
                // Whoever comes back with the same id is a new player: not a member of the frozen cycle, no credit.
                if (_m.CycleMembers != null) _m.CycleMembers.Remove(p.Id);
                _m.GiverCredit.Remove(p.Id);
            }

            _voteKicks.Remove(p.Id);
            foreach (var votes in _voteKicks.Values) votes.Remove(p.Id);
            r.StateChanged = true;

            if (p.Id == _hostId)
            {
                p.IsHost = false;
                _hostId = null;
                var next = OldestPlayer(null, true) ?? OldestPlayer(null, false);
                if (next != null) SetHost(next, r);
            }
            if (_phase == RoomPhase.InMatch) OnPlayerRemovedFromMatch(p, now, r);
        }

        /// <summary>Duplicates (case, accents and look-alikes ignored, spec 3.3) get " 2", " 3"... within MaxLength.</summary>
        private string UniqueNickname(string nick, string selfId)
        {
            var taken = new List<string>(_players.Count);
            foreach (var p in _players)
                if (p.Id != selfId) taken.Add(p.Nickname);
            return NicknameRules.MakeUnique(nick, taken);
        }

        private void AddSystemChat(string code, string playerId, long now) =>
            AddChat(new ChatMessageView { Id = ++_chatCounter, PlayerId = playerId, Text = code, AtMs = now, IsSystem = true });

        private void AddChat(ChatMessageView message)
        {
            _chat.Add(message);
            while (_chat.Count > RoomLimits.ChatHistory) _chat.RemoveAt(0);
        }
    }
}
