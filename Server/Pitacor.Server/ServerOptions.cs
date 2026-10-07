namespace Pitacor.Server;

/// <summary>Server settings, bound from the configuration root (appsettings*.json, environment variables, args).</summary>
public sealed class ServerOptions
{
    /// <summary>Origins allowed to open /ws (e.g. "https://pitacor.app"). Same-origin and requests without an Origin
    /// header are always allowed; in Development every origin is allowed.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Dev only: connected players needed to start/keep a match (RoomOptions.MinPlayersOverride). 0 = rules default.</summary>
    public int DevMinPlayers { get; set; }

    /// <summary>Folder with a Unity Web build served at "/" (relative to the content root). Ignored when missing.</summary>
    public string WebBuildPath { get; set; } = "../../Builds/Web";

    public int MaxConnectionsPerIp { get; set; } = 10;

    /// <summary>
    /// Header(s) carrying the real client IP behind a proxy, comma-separated, most trusted first (e.g. "Fly-Client-IP"
    /// or "CF-Connecting-IP,X-Forwarded-For"). Empty = socket address.
    /// </summary>
    public string ClientIpHeader { get; set; } = "";

    public int MaxMessageBytes { get; set; } = 8 * 1024;

    /// <summary>Room actor tick period (engine timers).</summary>
    public int TickMs { get; set; } = 200;

    /// <summary>A room with nobody connected is disposed after this many minutes without activity.</summary>
    public double EmptyRoomMinutes { get; set; } = 15;

    /// <summary>Token buckets per connection (messages per second; the burst equals the rate).</summary>
    public double MessagesPerSecond { get; set; } = 20;
    public double ChatPerSecond { get; set; } = 1;
    public double ReactionsPerSecond { get; set; } = 2;
}
