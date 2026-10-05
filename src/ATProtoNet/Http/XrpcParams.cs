using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

namespace ATProtoNet.Http;

/// <summary>Accumulates XRPC query parameters in call order, formatted for the wire.</summary>
/// <remarks>
/// <para>Null values are dropped. Booleans render as <c>true</c>/<c>false</c>, numbers in the
/// invariant culture, timestamps as ISO 8601 in UTC with millisecond precision (the form
/// <c>datetime</c> Lexicon fields use), enums by their JSON names, and anything else —
/// identifier types included — through <see cref="object.ToString"/>. <see cref="AddAll"/>
/// appends one pair per element, so array parameters go out as repeated keys
/// (<c>uris=a&amp;uris=b</c>) per the XRPC convention rather than as one comma-joined value.</para>
/// <para>Build one fluently, or with a collection initializer, and pass it to
/// <see cref="AtProtoClient.QueryAsync{TOut}(Nsid, XrpcParams?, XrpcCallOptions?, CancellationToken)"/>
/// or an <see cref="IXrpcTransport"/> call. Identifier types such as <see cref="Did"/> and
/// <see cref="AtUri"/> pass as strings.</para>
/// </remarks>
/// <example>
/// <code>
/// var parameters = new XrpcParams
/// {
///     { "actor", did },
///     { "limit", 25 },
///     { "cursor", cursor },
/// };
///
/// var fluent = new XrpcParams().Add("actor", did).AddAll("tags", ["a", "b"]);
/// </code>
/// </example>
public sealed class XrpcParams : IEnumerable<KeyValuePair<string, string>>
{
    private static readonly ConcurrentDictionary<Enum, string> EnumNames = new();

    private readonly List<KeyValuePair<string, string>> _pairs = [];

    /// <summary>The number of pairs added.</summary>
    public int Count => _pairs.Count;

    /// <summary>Appends a string parameter, ignoring nulls.</summary>
    public XrpcParams Add(string key, string? value)
    {
        if (value is not null)
            _pairs.Add(new KeyValuePair<string, string>(key, value));
        return this;
    }

    /// <summary>Appends an integer parameter, ignoring nulls.</summary>
    public XrpcParams Add(string key, int? value) =>
        Add(key, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Appends a 64-bit integer parameter, ignoring nulls.</summary>
    public XrpcParams Add(string key, long? value) =>
        Add(key, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Appends a boolean parameter as <c>true</c>/<c>false</c>, ignoring nulls.</summary>
    public XrpcParams Add(string key, bool? value) =>
        Add(key, value is null ? null : FormatBoolean(value.Value));

    /// <summary>Appends a timestamp as ISO 8601 in UTC, ignoring nulls.</summary>
    public XrpcParams Add(string key, DateTimeOffset? value) =>
        Add(key, value is null ? null : FormatTimestamp(value.Value));

    /// <summary>Appends any other formattable value — in the invariant culture, or by its JSON name for an enum — ignoring nulls.</summary>
    public XrpcParams Add<T>(string key, T? value)
        where T : struct, ISpanFormattable =>
        Add(key, value is null ? null : FormatScalar(value.Value));

    /// <summary>Appends one parameter per element, all sharing <paramref name="key"/>. A null or empty sequence contributes nothing.</summary>
    public XrpcParams AddAll(string key, IEnumerable<string>? values)
    {
        foreach (var value in values ?? [])
            Add(key, value);
        return this;
    }

    // The first value added under a key, or null.
    internal string? Get(string key) => _pairs.FirstOrDefault(pair => pair.Key == key).Value;

    // The parameters as a percent-encoded query string with repeated keys for arrays, prefixed with ?,
    // or an empty string when there are none.
    internal string ToQueryString()
    {
        if (_pairs.Count == 0)
            return string.Empty;

        var query = new System.Text.StringBuilder();
        foreach (var (key, value) in _pairs)
        {
            query.Append(query.Length == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        return query.ToString();
    }

    // Renders a scalar for the wire. Everything formattable goes through the invariant culture, so a
    // client running under, say, de-DE does not send 1,5 where the server expects 1.5, nor a localized
    // date where it expects ISO 8601.
    internal static string? FormatScalar(object value) => value switch
    {
        bool b => FormatBoolean(b),
        DateTimeOffset dto => FormatTimestamp(dto),
        DateTime dt => AtDatetime.FromDateTime(dt).ToString(),
        Enum e => EnumNames.GetOrAdd(e, FormatEnum),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string FormatTimestamp(DateTimeOffset value) =>
        AtDatetime.FromDateTimeOffset(value).ToString();

    // An enum's name as the SDK's JSON serializer writes it, so a query parameter and a body field of
    // the same enum agree — camelCase by default, or whatever [JsonStringEnumMemberName] says.
    private static string FormatEnum(Enum value)
    {
        var element = JsonSerializer.SerializeToElement(value, value.GetType(), AtProtoJsonDefaults.Options);
        return element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _pairs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
