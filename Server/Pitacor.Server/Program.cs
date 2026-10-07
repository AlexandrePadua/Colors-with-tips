using Microsoft.Extensions.DependencyInjection.Extensions;
using Pitacor.Core.Rules;
using Pitacor.Server;
using Pitacor.Server.Net;
using Pitacor.Server.Rooms;

var builder = WebApplication.CreateBuilder(args);

// Production (Fly.io, Docker): PORT decides the listening port. Development uses launchSettings (0.0.0.0:5080).
string? port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.Configure<ServerOptions>(builder.Configuration);
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RoomRegistry>();
builder.Services.AddSingleton<ConnectionLimiter>();
builder.Services.AddSingleton<WebSocketEndpoint>();
builder.Services.ConfigureHttpJsonOptions(o => WireJson.Configure(o.SerializerOptions));
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Pitacor.Server");

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15),
    KeepAliveTimeout = TimeSpan.FromSeconds(30), // drops dead sockets (phone locked, network gone)
});

string? webRoot = WebBuildHosting.Use(app);
if (webRoot != null) log.LogInformation("Serving the Unity Web build from {WebRoot}", webRoot);

app.MapGet("/health", (RoomRegistry rooms, WebSocketEndpoint ws) =>
    Results.Ok(new { status = "ok", rooms = rooms.RoomCount, connections = ws.LiveConnections }));

app.MapGet("/api/rooms/public", (string? lang, RoomRegistry rooms) => Results.Ok(rooms.PublicRooms(ParseLanguage(lang))));

app.Map("/ws", (HttpContext ctx, WebSocketEndpoint ws) => ws.HandleAsync(ctx));

app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RoomRegistry>().Shutdown());

if (app.Environment.IsDevelopment() && webRoot == null)
    app.MapGet("/", () => Results.Text("Pitacor server is running. No Web build found (WebBuildPath)."));

app.Run();

static Language? ParseLanguage(string? lang)
{
    if (string.IsNullOrWhiteSpace(lang)) return null;
    string key = lang.Replace("-", "").Replace("_", "");
    return Enum.TryParse<Language>(key, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
}

/// <summary>Entry point (public for WebApplicationFactory in the integration tests).</summary>
public partial class Program;
