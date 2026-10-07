using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pitacor.Core.Protocol;
using Pitacor.Server.Rooms;

namespace Pitacor.Server.Net;

/// <summary>GET /ws: admission checks (Origin, per-IP limit), then the receive loop of one connection.</summary>
public sealed class WebSocketEndpoint
{
    /// <summary>Close description a client sends to give up its seat for good (any other close keeps the seat).</summary>
    public const string LeaveCloseDescription = "leave";

    private readonly RoomRegistry _rooms;
    private readonly ConnectionLimiter _limiter;
    private readonly IOptions<ServerOptions> _options;
    private readonly TimeProvider _time;
    private readonly IHostEnvironment _env;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WebSocketEndpoint> _log;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Connection> _live = new();

    public WebSocketEndpoint(RoomRegistry rooms, ConnectionLimiter limiter, IOptions<ServerOptions> options, TimeProvider time,
        IHostEnvironment env, IHostApplicationLifetime lifetime, ILogger<WebSocketEndpoint> log)
    {
        _rooms = rooms;
        _limiter = limiter;
        _options = options;
        _time = time;
        _env = env;
        _lifetime = lifetime;
        _log = log;
        // Graceful shutdown: every client gets a close frame (1001) instead of a dropped socket.
        lifetime.ApplicationStopping.Register(() =>
        {
            foreach (var conn in _live.Values) conn.Close(WebSocketCloseStatus.EndpointUnavailable, "server shutting down");
        });
    }

    public int LiveConnections => _live.Count;

    public async Task HandleAsync(HttpContext ctx)
    {
        var options = _options.Value;
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!OriginAllowed(ctx, options))
        {
            _log.LogWarning("Rejected WebSocket from origin {Origin}", ctx.Request.Headers.Origin.ToString());
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        string ip = ClientIp(ctx, options);
        if (!_limiter.TryAcquire(ip, options.MaxConnectionsPerIp))
        {
            _log.LogWarning("Rejected WebSocket from {Ip}: too many connections", ip);
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        try
        {
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var conn = new Connection(ws, ip, _time, options);
            var writer = conn.RunWriterAsync();
            _live[conn.Id] = conn;
            if (_lifetime.ApplicationStopping.IsCancellationRequested) conn.Close(WebSocketCloseStatus.EndpointUnavailable, "server shutting down");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, conn.Aborted);
            bool leaving = false;
            try
            {
                leaving = await ReceiveLoopAsync(ws, conn, options, linked.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                // Abrupt disconnect, abort or shutdown: the seat is kept.
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Connection {ConnectionId} failed", conn.Id);
            }
            finally
            {
                _live.TryRemove(conn.Id, out _);
                conn.Room?.Disconnect(conn, leaving);
                conn.Close(WebSocketCloseStatus.NormalClosure, "bye"); // no-op when a close is already under way
                await writer;
                if (ws.State != WebSocketState.Closed) ws.Abort();
            }
        }
        finally
        {
            _limiter.Release(ip);
        }
    }

    /// <summary>
    /// Returns true when the client left for good: a "leave" message (every client) or a close with the "leave"
    /// description (Web clients). Any other end keeps the seat for reconnection.
    /// </summary>
    private async Task<bool> ReceiveLoopAsync(WebSocket ws, Connection conn, ServerOptions options, CancellationToken ct)
    {
        var buffer = new byte[Math.Max(256, options.MaxMessageBytes)];
        int count = 0;
        while (true)
        {
            if (count == buffer.Length)
            {
                _log.LogWarning("Connection {ConnectionId} ({Ip}) sent a message over {Max} bytes", conn.Id, conn.Ip, buffer.Length);
                conn.Close(WebSocketCloseStatus.MessageTooBig, "message too big");
                await DrainUntilClosedAsync(ws, buffer, ct);
                return false;
            }
            var result = await ws.ReceiveAsync(buffer.AsMemory(count), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return string.Equals(ws.CloseStatusDescription, LeaveCloseDescription, StringComparison.OrdinalIgnoreCase);
            count += result.Count;
            if (!result.EndOfMessage) continue;

            if (result.MessageType == WebSocketMessageType.Binary) conn.SendError(ErrorCodes.InvalidCommand, "Binary");
            else if (await OnMessageAsync(conn, buffer.AsMemory(0, count))) return true;
            count = 0;
        }
    }

    private static async Task DrainUntilClosedAsync(WebSocket ws, byte[] buffer, CancellationToken ct)
    {
        while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseSent)
        {
            var result = await ws.ReceiveAsync(buffer.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
        }
    }

    /// <summary>Handles one text message; returns true when it was a "leave".</summary>
    private async Task<bool> OnMessageAsync(Connection conn, ReadOnlyMemory<byte> json)
    {
        if (!conn.General.TryTake())
        {
            conn.SendError(ErrorCodes.RateLimited);
            return false;
        }

        string? type;
        JsonElement data;
        try
        {
            var envelope = JsonSerializer.Deserialize<WireEnvelope<JsonElement>>(json.Span, WireJson.Options);
            type = envelope?.T;
            data = envelope?.D ?? default;
        }
        catch (JsonException)
        {
            conn.SendError(ErrorCodes.InvalidCommand, "MalformedJson");
            return false;
        }

        try
        {
            switch (type)
            {
                case WireTypes.Ping:
                    var ping = Read<PingMessage>(data);
                    conn.Send(WireTypes.Pong, new PongMessage { ClientTimeMs = ping?.ClientTimeMs ?? 0, ServerNowMs = _time.GetUtcNow().ToUnixTimeMilliseconds() });
                    break;
                case WireTypes.Hello:
                    await OnHelloAsync(conn, Read<HelloMessage>(data));
                    break;
                case WireTypes.Command:
                    OnCommand(conn, Read<ClientCommand>(data));
                    break;
                case WireTypes.Leave:
                    return true;
                default:
                    conn.SendError(ErrorCodes.InvalidCommand, "UnknownMessageType");
                    break;
            }
        }
        catch (JsonException)
        {
            conn.SendError(ErrorCodes.InvalidCommand, "MalformedJson");
        }
        return false;
    }

    private static T? Read<T>(JsonElement data) where T : class =>
        data.ValueKind == JsonValueKind.Object ? data.Deserialize<T>(WireJson.Options) : null;

    private void OnCommand(Connection conn, ClientCommand? cmd)
    {
        if (cmd == null || string.IsNullOrEmpty(cmd.Type))
        {
            conn.SendError(ErrorCodes.InvalidCommand, "Type");
            return;
        }
        var room = conn.Room;
        if (room == null)
        {
            conn.SendError(ServerErrorCodes.NotInRoom);
            return;
        }
        if ((cmd.Type == CommandTypes.SendChat && !conn.Chat.TryTake())
            || (cmd.Type == CommandTypes.SendReaction && !conn.Reactions.TryTake()))
        {
            conn.SendError(ErrorCodes.RateLimited, cmd.Type);
            return;
        }
        room.Command(conn, cmd);
    }

    private async Task OnHelloAsync(Connection conn, HelloMessage? hello)
    {
        if (hello == null)
        {
            conn.SendError(ErrorCodes.InvalidCommand, "Hello");
            return;
        }
        if (conn.Room != null)
        {
            conn.SendError(ServerErrorCodes.AlreadyInRoom);
            return;
        }

        var session = _rooms.ResolveSession(hello.SessionToken);
        RoomHost? room = null;
        if (!hello.CreateRoom)
        {
            // No code: go back to the session's room (reconnection with only the token).
            string? code = string.IsNullOrWhiteSpace(hello.RoomCode) ? session.RoomCode : Ids.NormalizeRoomCode(hello.RoomCode);
            if (code == null && string.IsNullOrWhiteSpace(hello.RoomCode))
            {
                conn.SendError(ErrorCodes.InvalidCommand, "RoomCode");
                return;
            }
            room = _rooms.Find(code);
            if (room == null)
            {
                conn.SendError(ServerErrorCodes.RoomNotFound);
                return;
            }
        }

        // One seat per session: moving to another room gives up the previous one.
        string? previousCode = session.RoomCode;
        if (previousCode != null && previousCode != room?.Code)
        {
            var previous = _rooms.Find(previousCode);
            if (previous != null) await previous.LeaveAsync(session);
        }

        bool created = room == null;
        room ??= _rooms.Create(hello.Language);
        var outcome = await room.JoinAsync(conn, session, hello);
        if (outcome.Ok) return;

        conn.SendError(outcome.Error!.Code, outcome.Error.Detail);
        if (created) room.Stop();
    }

    private bool OriginAllowed(HttpContext ctx, ServerOptions options)
    {
        string origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || _env.IsDevelopment()) return true;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase)) return true; // same origin
        foreach (var allowed in options.AllowedOrigins)
        {
            if (allowed == "*" || string.Equals(allowed.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ClientIp(HttpContext ctx, ServerOptions options)
    {
        // ClientIpHeader may list several headers, most trusted first (e.g. "CF-Connecting-IP,X-Forwarded-For").
        // Only values that parse as an IP count, so junk in a forged header falls through to the next one.
        if (!string.IsNullOrEmpty(options.ClientIpHeader))
        {
            foreach (string name in options.ClientIpHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string header = ctx.Request.Headers[name].ToString();
                if (string.IsNullOrWhiteSpace(header)) continue;
                string first = header.Split(',')[0].Trim();
                if (System.Net.IPAddress.TryParse(first, out var parsed)) return parsed.ToString();
            }
        }
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
