using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pitacor.Core.Match;
using Pitacor.Core.Protocol;

namespace Pitacor.Server;

/// <summary>JSON settings shared by the WebSocket protocol and the HTTP API (camelCase, enums as strings, no nulls).</summary>
public static class WireJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // Names only: an integer would let a client smuggle undefined enum values into the engine.
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    public static byte[] Envelope<T>(string type, T data) =>
        JsonSerializer.SerializeToUtf8Bytes(new WireEnvelope<T>(type, data), Options);
}

/// <summary>Error codes produced by the server itself (the engine's are in <see cref="ErrorCodes"/>).</summary>
public static class ServerErrorCodes
{
    /// <summary>hello with a room code that does not exist (or already expired).</summary>
    public const string RoomNotFound = "RoomNotFound";
    /// <summary>cmd before a successful hello, or after being removed from the room.</summary>
    public const string NotInRoom = "NotInRoom";
    /// <summary>hello on a connection that is already in a room (open a new connection instead).</summary>
    public const string AlreadyInRoom = "AlreadyInRoom";
}

/// <summary>Random identifiers. Everything comes from the OS CSPRNG.</summary>
public static class Ids
{
    private const string RoomAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int RoomCodeLength = 5;

    /// <summary>128-bit secret, base64url (22 chars).</summary>
    public static string NewSessionToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Short public id (48 bits, 8 chars).</summary>
    public static string NewPlayerId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(6));

    public static string NewRoomCode()
    {
        Span<char> chars = stackalloc char[RoomCodeLength];
        for (int i = 0; i < chars.Length; i++) chars[i] = RoomAlphabet[RandomNumberGenerator.GetInt32(RoomAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>Upper-cases and validates a user-typed room code; null when it cannot be one.</summary>
    public static string? NormalizeRoomCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string c = code.Trim().ToUpperInvariant();
        if (c.Length != RoomCodeLength) return null;
        foreach (char ch in c)
            if (RoomAlphabet.IndexOf(ch) < 0) return null;
        return c;
    }
}

/// <summary>Unpredictable randomness for the engine (options must not be guessable from earlier rounds).</summary>
public sealed class CryptoRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) =>
        maxExclusive <= minInclusive ? minInclusive : RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
}
