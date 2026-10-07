using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Pitacor.Core.Match;
using Pitacor.Core.Protocol;
using Pitacor.Core.Rules;
using Pitacor.Server.Net;

namespace Pitacor.Server.Rooms;

public sealed record JoinOutcome(EngineError? Error)
{
    public static readonly JoinOutcome Success = new((EngineError?)null);
    public bool Ok => Error == null;
    public static JoinOutcome Fail(string code) => new(new EngineError { Code = code });
}

/// <summary>
/// One room = one single-threaded actor: every engine call runs on the loop that drains <see cref="_inbox"/>, so the
/// engine needs no locks. After each input it sends errors to their author, a fresh state to each player whose
/// filtered view changed, and each player's filtered events.
/// </summary>
public sealed class RoomHost
{
    /// <summary>WebSocket close code sent to a connection superseded by a newer one with the same session token.
    /// The client must not auto-reconnect on it.</summary>
    public const WebSocketCloseStatus ReplacedCloseStatus = (WebSocketCloseStatus)4001;

    private readonly Channel<Action> _inbox = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly RoomEngine _engine;
    /// <summary>playerId → live connection.</summary>
    private readonly Dictionary<string, Connection> _connections = new();
    /// <summary>playerId → session, for everyone holding a seat (connected or not).</summary>
    private readonly Dictionary<string, Session> _members = new();
    private readonly HashSet<string> _bannedTokens = new();
    private readonly ServerOptions _options;
    private readonly TimeProvider _time;
    private readonly RoomRegistry _registry;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _stop = new();
    private int _tickQueued;
    private int _lastVersion = -1;
    private volatile RoomSummary _summary;
    private volatile bool _closed;

    public RoomHost(string code, GameSettings settings, ServerOptions options, TimeProvider time, RoomRegistry registry, ILogger log)
    {
        Code = code;
        _options = options;
        _time = time;
        _registry = registry;
        _log = log;
        var engineOptions = new RoomOptions { InitialSettings = settings, MinPlayersOverride = Math.Max(0, options.DevMinPlayers) };
        _engine = new RoomEngine(code, engineOptions, new CryptoRandomSource(), Now());
        _summary = _engine.Summary;
    }

    public string Code { get; }
    public bool IsClosed => _closed;
    /// <summary>Snapshot refreshed by the actor after every input (safe to read from any thread).</summary>
    public RoomSummary Summary => _summary;

    private long EmptyRoomMs => (long)(_options.EmptyRoomMinutes * 60_000);

    private long Now() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    // ------------------------------------------------------------------ inputs (any thread)

    internal void Start()
    {
        _ = Task.Run(RunAsync);
        _ = Task.Run(TickLoopAsync);
    }

    public void Stop() => _inbox.Writer.TryWrite(Shutdown);

    public Task<JoinOutcome> JoinAsync(Connection conn, Session session, HelloMessage hello)
    {
        var tcs = new TaskCompletionSource<JoinOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inbox.Writer.TryWrite(() => DoJoin(conn, session, hello, tcs)))
            tcs.TrySetResult(JoinOutcome.Fail(ServerErrorCodes.RoomNotFound));
        return tcs.Task;
    }

    /// <summary>The session gives up its seat here (it is joining another room).</summary>
    public Task LeaveAsync(Session session)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inbox.Writer.TryWrite(() => { DoLeave(session); tcs.TrySetResult(); })) tcs.TrySetResult();
        return tcs.Task;
    }

    public void Command(Connection conn, ClientCommand cmd)
    {
        if (!_inbox.Writer.TryWrite(() => DoCommand(conn, cmd))) conn.SendError(ServerErrorCodes.NotInRoom);
    }

    /// <summary>The socket is gone. <paramref name="leaving"/> = the client said it left for good.</summary>
    public void Disconnect(Connection conn, bool leaving) => _inbox.Writer.TryWrite(() => DoDisconnect(conn, leaving));

    // ------------------------------------------------------------------ actor

    private async Task RunAsync()
    {
        await foreach (var action in _inbox.Reader.ReadAllAsync())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Room {RoomCode}: input failed", Code);
            }
        }
    }

    private async Task TickLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(10, _options.TickMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                // At most one queued tick: a slow actor must not build a backlog of them.
                if (Interlocked.Exchange(ref _tickQueued, 1) == 0 && !_inbox.Writer.TryWrite(DoTick)) break;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DoTick()
    {
        Interlocked.Exchange(ref _tickQueued, 0);
        if (_closed) return;
        long now = Now();
        Publish(_engine.Tick(now), null, now);
        if (_engine.IsEmpty && now - _engine.LastActivityMs >= EmptyRoomMs)
        {
            _log.LogInformation("Room {RoomCode} expired (empty since {LastActivityMs})", Code, _engine.LastActivityMs);
            Shutdown();
        }
    }

    private void DoJoin(Connection conn, Session session, HelloMessage hello, TaskCompletionSource<JoinOutcome> tcs)
    {
        if (_closed)
        {
            tcs.TrySetResult(JoinOutcome.Fail(ServerErrorCodes.RoomNotFound));
            return;
        }
        string pid = session.PlayerId;
        if (_bannedTokens.Contains(session.Token) || (hello.SessionToken != null && _bannedTokens.Contains(hello.SessionToken)))
        {
            tcs.TrySetResult(JoinOutcome.Fail(ErrorCodes.Banned));
            return;
        }

        long now = Now();
        var r = _engine.Join(pid, hello.Nickname, hello.Avatar, now);
        var error = r.Errors.Find(e => e.PlayerId == pid);
        if (error != null)
        {
            r.Errors.RemoveAll(e => e.PlayerId == pid);
            tcs.TrySetResult(new JoinOutcome(error));
            Publish(r, null, now);
            return;
        }

        if (_connections.TryGetValue(pid, out var previous) && previous != conn)
        {
            previous.Room = null;
            previous.Close(ReplacedCloseStatus, "replaced");
        }
        _connections[pid] = conn;
        _members[pid] = session;
        session.RoomCode = Code;
        _registry.Register(session);
        conn.Session = session;
        conn.Room = this;
        conn.LastStateHash = null;
        conn.Send(WireTypes.Welcome, new WelcomeMessage { PlayerId = pid, SessionToken = session.Token, RoomCode = Code, ServerNowMs = now });
        _log.LogInformation("Room {RoomCode}: player {PlayerId} joined from {Ip} (connection {ConnectionId}, {Kind})",
            Code, pid, conn.Ip, conn.Id, previous != null ? "replaced" : "new");
        tcs.TrySetResult(JoinOutcome.Success);
        Publish(r, conn, now);
    }

    private void DoCommand(Connection conn, ClientCommand cmd)
    {
        var session = conn.Session;
        if (_closed || session == null || conn.Room != this
            || !_connections.TryGetValue(session.PlayerId, out var current) || current != conn)
        {
            conn.SendError(ServerErrorCodes.NotInRoom);
            return;
        }
        long now = Now();
        Publish(_engine.Handle(session.PlayerId, cmd, now), conn, now);
    }

    private void DoDisconnect(Connection conn, bool leaving)
    {
        var session = conn.Session;
        if (_closed || session == null) return;
        string pid = session.PlayerId;
        if (!_connections.TryGetValue(pid, out var current) || current != conn) return; // replaced or already removed
        _connections.Remove(pid);
        conn.Room = null;

        long now = Now();
        EngineResult r;
        if (leaving)
        {
            r = _engine.Leave(pid, now);
            _members.Remove(pid);
            _registry.Forget(session, Code);
        }
        else
        {
            r = _engine.SetConnected(pid, false, now);
        }
        _log.LogInformation("Room {RoomCode}: player {PlayerId} {Action}", Code, pid, leaving ? "left" : "disconnected");
        Publish(r, null, now);
    }

    private void DoLeave(Session session)
    {
        if (_closed) return;
        string pid = session.PlayerId;
        if (_connections.Remove(pid, out var conn))
        {
            conn.Room = null;
            conn.Close(WebSocketCloseStatus.NormalClosure, "left");
        }
        _members.Remove(pid);
        _registry.Forget(session, Code);
        long now = Now();
        var r = _engine.Leave(pid, now);
        r.Errors.RemoveAll(e => e.PlayerId == pid);
        Publish(r, null, now);
    }

    private void Shutdown()
    {
        if (_closed) return;
        _closed = true;
        _stop.Cancel();
        foreach (var conn in _connections.Values)
        {
            conn.Room = null;
            conn.Close(WebSocketCloseStatus.EndpointUnavailable, "room closed");
        }
        _connections.Clear();
        _registry.Remove(this);
        _inbox.Writer.TryComplete(); // queued inputs still drain and see _closed
        _log.LogInformation("Room {RoomCode} closed", Code);
    }

    // ------------------------------------------------------------------ output

    private void Publish(EngineResult r, Connection? author, long now)
    {
        foreach (var e in r.Errors)
            if (e.PlayerId != null && _connections.TryGetValue(e.PlayerId, out var target)) target.Send(WireTypes.Error, e);

        List<(string PlayerId, Connection Conn)>? kicked = null;
        foreach (var ev in r.Events)
        {
            if (ev.Type == EventTypes.ReportFiled)
            {
                _log.LogWarning("Room {RoomCode}: report by {ReporterId} against {ReportedId}: {Reason}", Code, ev.PlayerId, ev.Code, ev.Text);
            }
            else if (ev.Type == EventTypes.PlayerKicked && ev.PlayerId != null)
            {
                if (_members.Remove(ev.PlayerId, out var session))
                {
                    _bannedTokens.Add(session.Token);
                    _registry.Forget(session, Code);
                }
                if (_connections.Remove(ev.PlayerId, out var conn)) (kicked ??= new()).Add((ev.PlayerId, conn));
                _log.LogInformation("Room {RoomCode}: player {PlayerId} kicked ({Reason})", Code, ev.PlayerId, ev.Code);
            }
        }

        bool changed = _engine.Version != _lastVersion;
        _lastVersion = _engine.Version;
        foreach (var (pid, conn) in _connections)
            if (changed || conn == author || conn.LastStateHash == null) SendStateIfChanged(pid, conn, now);

        if (r.Events.Count > 0)
        {
            foreach (var (pid, conn) in _connections) SendEvents(r, pid, conn);
            if (kicked != null)
            {
                foreach (var (pid, conn) in kicked)
                {
                    SendEvents(r, pid, conn);
                    conn.Room = null;
                }
            }
        }
        _summary = _engine.Summary;
    }

    private static void SendEvents(EngineResult r, string playerId, Connection conn)
    {
        var events = r.EventsFor(playerId);
        if (events.Count > 0) conn.Send(WireTypes.Events, events);
    }

    /// <summary>Sends the player's view unless it equals the last one sent, ignoring the clock fields and Version.</summary>
    private void SendStateIfChanged(string playerId, Connection conn, long now)
    {
        var view = _engine.BuildView(playerId, now);
        long serverNow = view.ServerNowMs;
        int version = view.Version;
        long? remaining = view.Match?.PhaseRemainingMs;
        view.ServerNowMs = 0;
        view.Version = 0;
        if (view.Match != null) view.Match.PhaseRemainingMs = null;

        byte[] hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(view, WireJson.Options));
        if (conn.LastStateHash != null && hash.AsSpan().SequenceEqual(conn.LastStateHash)) return;
        conn.LastStateHash = hash;

        view.ServerNowMs = serverNow;
        view.Version = version;
        if (view.Match != null) view.Match.PhaseRemainingMs = remaining;
        conn.Send(WireTypes.State, view);
    }
}
