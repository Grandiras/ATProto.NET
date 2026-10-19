using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Crypto.Jose;

/// <summary>The JOSE header members the SDK reads and writes.</summary>
/// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc7515#section-4.1"/>.</remarks>
public sealed record JwsHeader
{
    /// <summary>The algorithm, <c>ES256</c> or <c>ES256K</c> for the tokens the SDK signs.</summary>
    [JsonPropertyName("alg")]
    public required string Algorithm { get; init; }

    /// <summary>The token type (<c>typ</c>), such as <c>dpop+jwt</c>.</summary>
    [JsonPropertyName("typ"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    /// <summary>The key ID (<c>kid</c>).</summary>
    [JsonPropertyName("kid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyId { get; init; }

    /// <summary>The embedded public key (<c>jwk</c>), as a DPoP proof carries it.</summary>
    [JsonPropertyName("jwk"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonWebKey? Jwk { get; init; }
}

/// <summary>A compact JWS split and decoded, but not verified.</summary>
/// <param name="header">The header members the SDK knows.</param>
/// <param name="headerJson">The whole header, a JSON object.</param>
/// <param name="payload">The payload, a JSON object.</param>
/// <param name="signingInput">The bytes the signature covers.</param>
/// <param name="signature">The 64-byte signature.</param>
public readonly struct DecodedJws(JwsHeader header, JsonElement headerJson, JsonElement payload, ReadOnlyMemory<byte> signingInput, ReadOnlyMemory<byte> signature)
{
    /// <summary>The header members the SDK knows.</summary>
    public JwsHeader Header { get; } = header;

    /// <summary>The whole header, a JSON object.</summary>
    public JsonElement HeaderJson { get; } = headerJson;

    /// <summary>The payload, a JSON object.</summary>
    public JsonElement Payload { get; } = payload;

    /// <summary>The bytes the signature covers: <c>header.payload</c> exactly as received.</summary>
    public ReadOnlyMemory<byte> SigningInput { get; } = signingInput;

    /// <summary>The 64-byte signature.</summary>
    public ReadOnlyMemory<byte> Signature { get; } = signature;
}

[JsonSerializable(typeof(JwsHeader))]
internal sealed partial class JoseJsonContext : JsonSerializerContext;
