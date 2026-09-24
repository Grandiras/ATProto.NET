using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Tests;

/// <summary>The three segments of a compact JWS, for choosing which ones a test pads.</summary>
[Flags]
public enum JwsSegments
{
    None = 0,
    Header = 1,
    Payload = 2,
    Signature = 4,
    All = Header | Payload | Signature,
}

/// <summary>
/// Mints compact JWS tokens independently of the SDK's own encoder, including shapes a
/// conforming signer would not produce: base64 padding on any segment.
/// </summary>
public static class TestJws
{
    /// <summary>Base64url-encodes <paramref name="data"/>, keeping the <c>=</c> padding when asked to.</summary>
    public static string Encode(ReadOnlySpan<byte> data, bool padded = false)
    {
        var text = Base64Url.EncodeToString(data);
        return padded ? text.PadRight((text.Length + 3) / 4 * 4, '=') : text;
    }

    /// <summary>Base64url-decodes an unpadded segment.</summary>
    public static byte[] Decode(string segment) => Base64Url.DecodeFromChars(segment);

    /// <summary>Decodes one JSON segment of a compact JWS.</summary>
    public static JsonElement DecodeJson(string jwt, int index) =>
        JsonSerializer.Deserialize<JsonElement>(Decode(jwt.Split('.')[index]));

    /// <summary>
    /// Mints a compact JWS. A padded header or payload is padded before signing, so the signature
    /// covers the text as sent, the way it would for a signer that emitted it.
    /// </summary>
    /// <param name="header">The JOSE header.</param>
    /// <param name="payload">The claims.</param>
    /// <param name="sign">Produces the <c>r || s</c> signature over the signing input.</param>
    /// <param name="padded">Which segments carry base64 padding.</param>
    public static string Mint(
        object header, object payload, Func<byte[], byte[]> sign, JwsSegments padded = JwsSegments.None)
    {
        var headerB64 = EncodeJson(header, padded.HasFlag(JwsSegments.Header));
        var payloadB64 = EncodeJson(payload, padded.HasFlag(JwsSegments.Payload));
        var signature = sign(Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}"));

        return $"{headerB64}.{payloadB64}.{Encode(signature, padded.HasFlag(JwsSegments.Signature))}";
    }

    /// <summary>Replaces one segment of a compact JWS.</summary>
    public static string WithSegment(string jwt, int index, string segment)
    {
        var parts = jwt.Split('.');
        parts[index] = segment;
        return string.Join('.', parts);
    }

    private static string EncodeJson(object value, bool padded)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value);

        // Trailing whitespace is valid JSON; it is the only way to give a JSON segment a length
        // whose base64 form needs padding without changing what it says.
        if (padded && json.Length % 3 == 0)
            json = [.. json, (byte)' '];

        return Encode(json, padded);
    }
}
