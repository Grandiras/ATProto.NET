using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Crypto.Jose;

/// <summary>An elliptic-curve JSON Web Key: <c>kty</c> <c>EC</c>, <c>crv</c> <c>P-256</c> or <c>secp256k1</c>.</summary>
/// <remarks>
/// <see cref="VerificationKey.FromJsonWebKey"/> validates the key. The private member <c>d</c> is written only when
/// it is set, which only an explicit private export does; <see cref="ToPublic"/> drops it and <see cref="ToString"/>
/// never shows it. See <see href="https://www.rfc-editor.org/rfc/rfc7517"/> and <see href="https://www.rfc-editor.org/rfc/rfc7518#section-6.2"/>.
/// </remarks>
public sealed record JsonWebKey
{
    /// <summary>The key type, <c>EC</c>.</summary>
    [JsonPropertyName("kty")]
    public required string Kty { get; init; }

    /// <summary>The curve, <c>P-256</c> or <c>secp256k1</c>.</summary>
    [JsonPropertyName("crv")]
    public required string Crv { get; init; }

    /// <summary>The x coordinate, unpadded base64url of 32 bytes.</summary>
    [JsonPropertyName("x")]
    public required string X { get; init; }

    /// <summary>The y coordinate, unpadded base64url of 32 bytes.</summary>
    [JsonPropertyName("y")]
    public required string Y { get; init; }

    /// <summary>The private scalar, unpadded base64url; secret, and absent on public keys.</summary>
    [JsonPropertyName("d"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? D { get; init; }

    /// <summary>The key ID.</summary>
    [JsonPropertyName("kid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kid { get; init; }

    /// <summary>The algorithm the key is meant for, such as <c>ES256</c>.</summary>
    [JsonPropertyName("alg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Alg { get; init; }

    /// <summary>The intended use, such as <c>sig</c>.</summary>
    [JsonPropertyName("use"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Use { get; init; }

    /// <summary>The RFC 7638 thumbprint: base64url SHA-256 of the required members in lexicographic order.</summary>
    /// <returns>The unpadded base64url thumbprint.</returns>
    /// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc7638#section-3"/>.</remarks>
    public string Thumbprint()
    {
        var json = new ArrayBufferWriter<byte>(160);
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteString("crv"u8, Crv);
            writer.WriteString("kty"u8, Kty);
            writer.WriteString("x"u8, X);
            writer.WriteString("y"u8, Y);
            writer.WriteEndObject();
        }
        return Base64Url.EncodeToString(SHA256.HashData(json.WrittenSpan));
    }

    /// <summary>The key without its private member.</summary>
    /// <returns>A public key.</returns>
    public JsonWebKey ToPublic() => D is null ? this : this with { D = null };

    /// <summary>The key's public members; <c>d</c> is never shown.</summary>
    /// <returns>For example <c>JsonWebKey { kty = EC, crv = P-256, x = …, y = … }</c>.</returns>
    public override string ToString() =>
        $"JsonWebKey {{ kty = {Kty}, crv = {Crv}, x = {X}, y = {Y}{(Kid is null ? "" : $", kid = {Kid}")}{(D is null ? "" : ", d = [redacted]")} }}";
}

/// <summary>A JSON Web Key Set; it holds public keys only, as client metadata serves them.</summary>
/// <param name="Keys">The keys; private members are dropped.</param>
/// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc7517#section-5"/>.</remarks>
public sealed record JsonWebKeySet(IReadOnlyList<JsonWebKey> Keys)
{
    /// <summary>The keys, all public.</summary>
    [JsonPropertyName("keys")]
    public IReadOnlyList<JsonWebKey> Keys { get; } = [.. Keys.Select(key => key.ToPublic())];
}
