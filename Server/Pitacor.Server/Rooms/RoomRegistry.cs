using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Pitacor.Core.Protocol;
using Pitacor.Core.Rules;

namespace Pitacor.Server.Rooms;

/// <summary>A player's identity across connections: the secret token reclaims the same seat.</summary>
public sealed class Session
{
    private volatile string? _roomCode;

    public Session(string token, string playerId)
    {
        Token = token;
        PlayerId = playerId;
    }

    public string Token { get; }
    public string PlayerId { get; }

    /// <summary>Room the player has a seat in (written by that room's actor).</summary>
    public string? RoomCode { get => _roomCode; set => _roomCode = value; }
}

/// <summary>All live rooms and sessions. Thread-safe; each room's state lives in its own actor.</summary>
public sealed class RoomRegistry
{
    private const int MaxPublicListing = 50;

    private readonly ConcurrentDictionary<string, RoomHost> _rooms = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly IOptions<ServerOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _log;

    public RoomRegistry(IOptions<ServerOptions> options, TimeProvider time, ILoggerFactory loggers)
    {
        _options = options;
        _time = time;
        _loggers = loggers;
        _log = loggers.CreateLogger<RoomRegistry>();
    }

    public int RoomCount => _rooms.Count;

    public RoomHost Create(Language language)
    {
        var settings = new GameSettings { Language = language };
        while (true)
        {
            string code = Ids.NewRoomCode();
            var room = new RoomHost(code, settings, _options.Value, _time, this, _loggers.CreateLogger<RoomHost>());
            if (!_rooms.TryAdd(code, room)) continue;
            room.Start();
            _log.LogInformation("Room {RoomCode} created ({Language})", code, language);
            return room;
        }
    }

    public RoomHost? Find(string? code) =>
        code != null && _rooms.TryGetValue(code, out var room) && !room.IsClosed ? room : null;

    /// <summary>The session of a known token, or a fresh one (registered only once it gets a seat).</summary>
    public Session ResolveSession(string? token)
    {
        if (!string.IsNullOrEmpty(token) && _sessions.TryGetValue(token, out var session)) return session;
        return new Session(Ids.NewSessionToken(), Ids.NewPlayerId());
    }

    internal void Register(Session session) => _sessions[session.Token] = session;

    /// <summary>Drops a session that no longer has a seat anywhere.</summary>
    internal void Forget(Session session, string roomCode)
    {
        if (session.RoomCode != roomCode) return; // moved to another room meanwhile
        session.RoomCode = null;
        _sessions.TryRemove(new KeyValuePair<string, Session>(session.Token, session));
    }

    /// <summary>Called by a room actor when it shuts down.</summary>
    internal void Remove(RoomHost room)
    {
        _rooms.TryRemove(new KeyValuePair<string, RoomHost>(room.Code, room));
        foreach (var pair in _sessions)
            if (pair.Value.RoomCode == room.Code) Forget(pair.Value, room.Code);
    }

    public List<RoomSummary> PublicRooms(Language? language)
    {
        var list = new List<RoomSummary>();
        foreach (var room in _rooms.Values)
        {
            var s = room.Summary;
            if (room.IsClosed || !s.IsPublic || s.PlayerCount == 0) continue;
            if (language.HasValue && s.Language != language.Value) continue;
            list.Add(s);
        }
        return list
            .OrderBy(s => s.Phase == RoomPhase.Lobby ? 0 : 1)
            .ThenByDescending(s => s.PlayerCount < s.MaxPlayers ? s.PlayerCount : -1)
            .Take(MaxPublicListing)
            .ToList();
    }

    public void Shutdown()
    {
        foreach (var room in _rooms.Values) room.Stop();
    }
}
