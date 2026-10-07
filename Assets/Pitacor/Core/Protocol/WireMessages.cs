using Pitacor.Core.Rules;

namespace Pitacor.Core.Protocol
{
    /// <summary>
    /// Values of the envelope's "t" field. Client to server: hello, cmd (ClientCommand), ping.
    /// Server to client: welcome, state (RoomView), events (GameEvent[] filtered per player), error (EngineError), pong.
    /// </summary>
    public static class WireTypes
    {
        public const string Hello = "hello";
        public const string Command = "cmd";
        public const string Ping = "ping";
        /// <summary>Client gives up its seat for good (any close without it keeps the seat for reconnection).</summary>
        public const string Leave = "leave";
        public const string Welcome = "welcome";
        public const string State = "state";
        public const string Events = "events";
        public const string Error = "error";
        public const string Pong = "pong";
    }

    /// <summary>WebSocket envelope { "t": type, "d": payload } (camelCase JSON, enums as strings, nulls omitted).</summary>
    public class WireEnvelope<TData>
    {
        public string T { get; set; }
        public TData D { get; set; }

        public WireEnvelope() { }

        public WireEnvelope(string type, TData data)
        {
            T = type;
            D = data;
        }
    }

    public class HelloMessage
    {
        /// <summary>Token from a previous welcome; reclaims the same player slot when the room still exists.</summary>
        public string SessionToken { get; set; }
        public string Nickname { get; set; }
        public AvatarSpec Avatar { get; set; }
        /// <summary>Room to join; ignored when CreateRoom is true.</summary>
        public string RoomCode { get; set; }
        public bool CreateRoom { get; set; }
        public Language Language { get; set; }
    }

    public class WelcomeMessage
    {
        public string PlayerId { get; set; }
        public string SessionToken { get; set; }
        public string RoomCode { get; set; }
        public long ServerNowMs { get; set; }
    }

    public class PingMessage
    {
        public long ClientTimeMs { get; set; }
    }

    /// <summary>Payload of <see cref="WireTypes.Leave"/> (empty).</summary>
    public class LeaveMessage
    {
    }

    public class PongMessage
    {
        public long ClientTimeMs { get; set; }
        public long ServerNowMs { get; set; }
    }
}
