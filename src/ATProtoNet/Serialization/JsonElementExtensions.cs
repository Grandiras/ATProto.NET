using System.Runtime.InteropServices;
using System.Text.Json;
using ATProtoNet.Identity;

namespace ATProtoNet.Serialization;

// Reads optional members of a JSON object. Each answers null when the element is not an object, has no
// such member, or the member holds another kind of value.
internal static class JsonElementExtensions
{
    private static readonly long s_minUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long s_maxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public static string? GetStringOrNull(this JsonElement element, string name) =>
        TryGetMember(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The same, for a member named by its UTF-8 bytes, which spares transcoding the name on every
    // lookup. A hot path passes a u8 literal.
    public static string? GetStringOrNull(this JsonElement element, ReadOnlySpan<byte> utf8Name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(utf8Name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Reads an identifier member as T's TryParse reads the member's string, straight from its UTF-8 bytes
    // when it has no escapes.
    public static T? GetIdentifierOrNull<T>(this JsonElement element, ReadOnlySpan<byte> utf8Name)
        where T : class, IIdentifier<T>
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(utf8Name, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;

        // The raw value is the string as sent, in its quotes.
        var raw = JsonMarshal.GetRawUtf8Value(value);
        if (raw.IndexOf((byte)'\\') < 0 && T.TryCreate(raw[1..^1], out var fromUtf8))
            return fromUtf8;

        var text = value.GetString()!;
        return T.TryCreate(text, text, out var parsed) ? parsed : null;
    }

    public static long? GetInt64OrNull(this JsonElement element, string name) =>
        TryGetMember(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    public static long? GetInt64OrNull(this JsonElement element, ReadOnlySpan<byte> utf8Name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(utf8Name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    public static int? GetInt32OrNull(this JsonElement element, string name) =>
        TryGetMember(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    public static bool? GetBooleanOrNull(this JsonElement element, string name) =>
        TryGetMember(element, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    // Reads a JWT NumericDate member (exp, iat): whole seconds since the Unix epoch.
    //
    // The value is whatever the token's signer wrote, so one that cannot be represented is a malformed
    // token to refuse, not an ArgumentOutOfRangeException to let escape.
    //
    // value: The instant, or null when there is no such member.
    //
    // Returns: Whether the member is absent or holds a usable NumericDate; false for anything else,
    // including an integer outside the years 1 to 9999 that DateTimeOffset can represent.
    public static bool TryGetNumericDate(this JsonElement element, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!TryGetMember(element, name, out var member))
            return true;

        if (member.ValueKind != JsonValueKind.Number ||
            !member.TryGetInt64(out var seconds) ||
            seconds < s_minUnixSeconds ||
            seconds > s_maxUnixSeconds)
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private static bool TryGetMember(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }
}
