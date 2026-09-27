using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Crypto;
using ATProtoNet.Serialization;

namespace ATProtoNet.Auth;

// A compact JWS split into its parts and decoded, but not verified.
//
// Header: The JOSE header, always a JSON object.
//
// Payload: The claims, always a JSON object.
//
// SigningInput: The bytes the signature covers: {header}.{payload} exactly as sent.
//
// Signature: The decoded signature.
internal readonly record struct DecodedJwt(
    JsonElement Header, JsonElement Payload, byte[] SigningInput, byte[] Signature);

// The compact JWS encoding shared by every JWT the SDK mints or reads: DPoP proofs, service auth
// tokens and space tokens.
//
// Only the encoding lives here. Which claims a token must carry, what a verifier checks, and which
// exception a failure becomes stay with each token type, because they differ.
internal static class Jwt
{
    // An ES256 or ES256K signature: fixed-width r || s.
    private const int SignatureLength = 64;

    private static readonly SearchValues<char> s_base64UrlChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_=");

    // Serializes and base64url-encodes a JOSE header, for signers that reuse one header across many
    // tokens.
    //
    // type: The typ, or null for none.
    //
    // curve: The signing key's curve, which determines the alg.
    //
    // keyId: The kid, when the token names one.
    //
    // jwk: The public key to embed as jwk, as a DPoP proof does.
    //
    // Returns: The encoded header as ASCII bytes.
    public static byte[] EncodeHeader(string? type, KeyCurve curve, string? keyId = null, JsonWebKey? jwk = null)
    {
        var json = new ArrayBufferWriter<byte>(jwk is null ? 128 : 256);
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            if (type is not null)
                writer.WriteString("typ"u8, type);
            writer.WriteString("alg"u8, curve.JwsAlgorithm());

            if (keyId is not null)
                writer.WriteString("kid"u8, keyId);

            if (jwk is not null)
            {
                writer.WritePropertyName("jwk"u8);
                JsonSerializer.Serialize(writer, jwk);
            }

            writer.WriteEndObject();
        }

        var encoded = new byte[Base64Url.GetEncodedLength(json.WrittenCount)];
        Base64Url.EncodeToUtf8(json.WrittenSpan, encoded);
        return encoded;
    }

    // Signs a compact JWS: {header}.{base64url(payload)}.{base64url(signature)}.
    //
    // encodedHeader: The header from EncodeHeader.
    //
    // payload: The claims as UTF-8 JSON.
    //
    // key: The signing key. Its curve must be the one the header was encoded for.
    public static string Sign(ReadOnlySpan<byte> encodedHeader, ReadOnlySpan<byte> payload, AtProtoKey key)
    {
        var signingLength = encodedHeader.Length + 1 + Base64Url.GetEncodedLength(payload.Length);
        var tokenLength = signingLength + 1 + Base64Url.GetEncodedLength(SignatureLength);

        var rented = ArrayPool<byte>.Shared.Rent(tokenLength);
        try
        {
            var token = rented.AsSpan(0, tokenLength);
            encodedHeader.CopyTo(token);
            token[encodedHeader.Length] = (byte)'.';
            Base64Url.EncodeToUtf8(payload, token[(encodedHeader.Length + 1)..]);

            var signature = key.Sign(token[..signingLength]);
            if (signature.Length != SignatureLength)
                throw new CryptographicException($"Expected a {SignatureLength}-byte signature, got {signature.Length} bytes.");

            token[signingLength] = (byte)'.';
            Base64Url.EncodeToUtf8(signature, token[(signingLength + 1)..]);

            return Encoding.ASCII.GetString(token);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // Writes a fresh jti claim: 128 bits from the system CSPRNG, as 32 lower-case hex characters.
    public static void WriteTokenId(Utf8JsonWriter writer)
    {
        Span<byte> random = stackalloc byte[16];
        RandomNumberGenerator.Fill(random);

        Span<char> hex = stackalloc char[32];
        Convert.TryToHexStringLower(random, hex, out _);
        writer.WriteString("jti"u8, hex);
    }

    // The longest jti a verifier accepts: the width of the EF Core replay table's column.
    public const int MaxTokenIdLength = 255;

    // Whether an inbound token's jti can be spent in a replay store: present, not only whitespace, free of
    // control characters, and at most MaxTokenIdLength long.
    //
    // The value is the token signer's choice. Anything a store would refuse to key on has to be refused
    // here as a malformed token, or it surfaces from the store as a server fault.
    //
    // tokenId: The jti claim, or null when there is none.
    public static bool IsUsableTokenId([NotNullWhen(true)] string? tokenId) =>
        !string.IsNullOrWhiteSpace(tokenId) &&
        tokenId.Length <= MaxTokenIdLength &&
        !tokenId.AsSpan().ContainsAnyInRange('\0', '\x1f') &&
        !tokenId.AsSpan().ContainsAnyInRange('\x7f', '\x9f');

    // The payload's jti, or the exception refuse builds from why it is missing or not usable.
    //
    // payload: The token's claims.
    //
    // token: What the token is, for the message: "service auth token", "DPoP proof".
    //
    // refuse: Builds the verifier's own refusal from a message.
    public static string RequireTokenId(JsonElement payload, string token, Func<string, Exception> refuse)
    {
        var tokenId = payload.GetStringOrNull("jti");
        if (string.IsNullOrEmpty(tokenId))
            throw refuse($"The {token} is missing its \"jti\".");

        return IsUsableTokenId(tokenId)
            ? tokenId
            : throw refuse(
                $"The {token}'s \"jti\" must be printable, not only whitespace, and at most {MaxTokenIdLength} characters.");
    }

    // Splits a compact JWS and decodes its three parts, without verifying anything.
    //
    // jwt: The token.
    //
    // token: The decoded token, on success.
    //
    // error: Why the token is malformed, on failure, phrased to follow "Malformed {kind of token}: ".
    public static bool TryDecode(string jwt, out DecodedJwt token, [NotNullWhen(false)] out string? error)
    {
        token = default;

        var first = jwt.IndexOf('.');
        var second = first < 0 ? -1 : jwt.IndexOf('.', first + 1);
        if (second < 0 || jwt.IndexOf('.', second + 1) >= 0)
        {
            error = "expected three parts";
            return false;
        }

        if (!TryDecodeObject(jwt.AsSpan(0, first), "header", out var header, out error) ||
            !TryDecodeObject(jwt.AsSpan(first + 1, second - first - 1), "payload", out var payload, out error))
        {
            return false;
        }

        if (!TryDecodeBase64Url(jwt.AsSpan(second + 1), out var signature))
        {
            error = "the signature is not base64url";
            return false;
        }

        // Both segments passed the base64url alphabet check, so ASCII is their exact encoding.
        token = new DecodedJwt(header, payload, Encoding.ASCII.GetBytes(jwt, 0, second), signature);
        return true;
    }

    // Decodes JOSE base64url (RFC 7515 section 2): the URL-safe alphabet, tolerating the trailing padding
    // RFC 7515 omits as long as it is correct.
    //
    // Stricter than Base64Url.DecodeFromChars(ReadOnlySpan{char}) on its own, which skips whitespace: no
    // JOSE value contains any, and accepting it would let one token be spelled several ways. The standard
    // alphabet's + and / and non-zero trailing bits are rejected too, so every value has exactly one
    // accepted encoding apart from padding.
    //
    // text: The encoded text.
    //
    // bytes: The decoded bytes, on success.
    public static bool TryDecodeBase64Url(ReadOnlySpan<char> text, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        if (text.ContainsAnyExcept(s_base64UrlChars))
            return false;

        var buffer = new byte[Base64Url.GetMaxDecodedLength(text.Length)];
        if (Base64Url.DecodeFromChars(text, buffer, out _, out var written) != OperationStatus.Done)
            return false;

        bytes = written == buffer.Length ? buffer : buffer[..written];
        return true;
    }

    private static bool TryDecodeObject(
        ReadOnlySpan<char> segment, string name, out JsonElement element, [NotNullWhen(false)] out string? error)
    {
        element = default;

        if (!TryDecodeBase64Url(segment, out var bytes))
        {
            error = $"the {name} is not base64url";
            return false;
        }

        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(bytes);
        }
        catch (JsonException ex)
        {
            error = $"the {name} is not JSON ({ex.Message})";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"the {name} is not a JSON object";
            return false;
        }

        error = null;
        return true;
    }
}
