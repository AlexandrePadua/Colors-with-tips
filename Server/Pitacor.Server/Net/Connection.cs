using System.Net.WebSockets;
using System.Threading.Channels;
using Pitacor.Core.Protocol;
using Pitacor.Server.Rooms;

namespace Pitacor.Server.Net;

/// <summary>
/// One WebSocket. Outgoing messages go through a bounded queue drained by a single writer task, so room actors never
/// wait on the network; a client that cannot keep up is dropped. Receiving is done by <see cref="WebSocketEndpoint"/>.
/// </summary>
public sealed class Connection
{
    private const int OutboxCapacity = 512;
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    private readonly WebSocket _ws;
    private readonly Channel<byte[]> _outbox =
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(OutboxCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _abort = new();
    private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
    private string? _closeDescription;
    private volatile Session? _session;
    private volatile RoomHost? _room;

    public Connection(WebSocket ws, string ip, TimeProvider time, ServerOptions options)
    {
        _ws = ws;
        Ip = ip;
        General = new TokenBucket(options.MessagesPerSecond, time);
        Chat = new TokenBucket(options.ChatPerSecond, time);
        Reactions = new TokenBucket(options.ReactionsPerSecond, time);
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public string Ip { get; }
    public TokenBucket General { get; }
    public TokenBucket Chat { get; }
    public TokenBucket Reactions { get; }

    /// <summary>Set by the room actor when a hello succeeds.</summary>
    public Session? Session { get => _session; set => _session = value; }

    /// <summary>The room this connection currently plays in (null before hello, after a kick or when replaced).</summary>
    public RoomHost? Room { get => _room; set => _room = value; }

    /// <summary>Hash of the last state sent (without clock fields). Only touched by the room actor.</summary>
    public byte[]? LastStateHash { get; set; }

    /// <summary>Cancelled when the connection must stop (abort, close timeout, slow consumer).</summary>
    public CancellationToken Aborted => _abort.Token;

    public void Send<T>(string type, T data) => Send(WireJson.Envelope(type, data));

    public void SendError(string code, string? detail = null) =>
        Send(WireTypes.Error, new EngineError { PlayerId = Session?.PlayerId, Code = code, Detail = detail });

    public void Send(byte[] payload)
    {
        if (_outbox.Writer.TryWrite(payload)) return;
        if (_closeDescription == null) Abort(); // queue full (slow client) or already gone
    }

    /// <summary>Sends what is queued, then a close frame; aborts if the peer does not finish closing in time.</summary>
    public void Close(WebSocketCloseStatus status, string description)
    {
        if (_closeDescription != null) return;
        _closeStatus = status;
        _closeDescription = description;
        _outbox.Writer.TryComplete();
        try { _abort.CancelAfter(CloseGrace); } catch (ObjectDisposedException) { }
    }

    public void Abort()
    {
        _outbox.Writer.TryComplete();
        try { _abort.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async Task RunWriterAsync()
    {
        try
        {
            await foreach (var payload in _outbox.Reader.ReadAllAsync(_abort.Token))
                await _ws.SendAsync(payload, WebSocketMessageType.Text, true, _abort.Token);
            if (_closeDescription != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived))
                await _ws.CloseOutputAsync(_closeStatus, _closeDescription, _abort.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            Abort();
        }
    }
}

/// <summary>Classic token bucket: <c>rate</c> tokens per second, burst = rate (at least 1). Not thread-safe.</summary>
public sealed class TokenBucket
{
    private readonly double _rate;
    private readonly double _capacity;
    private readonly TimeProvider _time;
    private double _tokens;
    private long _last;

    public TokenBucket(double ratePerSecond, TimeProvider time)
    {
        _rate = Math.Max(0.001, ratePerSecond);
        _capacity = Math.Max(1, ratePerSecond);
        _time = time;
        _tokens = _capacity;
        _last = time.GetTimestamp();
    }

    public bool TryTake()
    {
        long now = _time.GetTimestamp();
        _tokens = Math.Min(_capacity, _tokens + _time.GetElapsedTime(_last, now).TotalSeconds * _rate);
        _last = now;
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }
}

/// <summary>Counts open WebSockets per client IP.</summary>
public sealed class ConnectionLimiter
{
    private readonly Dictionary<string, int> _counts = new();

    public bool TryAcquire(string ip, int max)
    {
        lock (_counts)
        {
            _counts.TryGetValue(ip, out int n);
            if (n >= max) return false;
            _counts[ip] = n + 1;
            return true;
        }
    }

    public void Release(string ip)
    {
        lock (_counts)
        {
            if (!_counts.TryGetValue(ip, out int n)) return;
            if (n <= 1) _counts.Remove(ip);
            else _counts[ip] = n - 1;
        }
    }

    public int Total
    {
        get { lock (_counts) return _counts.Values.Sum(); }
    }
}
