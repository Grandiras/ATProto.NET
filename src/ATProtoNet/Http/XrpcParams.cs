using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

namespace ATProtoNet.Http;

/// <summary>
/// Accumulates XRPC query parameters in call order, formatted for the wire.
/// </summary>
/// <remarks>
/// <para>Null values are dropped. Booleans render as <c>true</c>/<c>false</c>, numbers in the
/// invariant culture, timestamps as ISO 8601 in UTC with millisecond precision (the form
/// <c>datetime</c> Lexicon fields use), enums by their JSON names, and anything else —
/// identifier types included — through <see cref="object.ToString"/>. <see cref="AddAll"/>
/// appends one pair per element, so array parameters go out as repeated keys
/// (<c>uris=a&amp;uris=b</c>) per the XRPC convention rather than as one comma-joined value.</para>
/// <para><see cref="From"/> applies the same rules to a loosely typed parameter object — an
/// anonymous type or a dictionary — for the custom-XRPC entry points.</para>
/// </remarks>
internal sealed class XrpcParams : IEnumerable<KeyValuePair<string, string>>
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
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

    /// <summary>
    /// Appends any other formattable value — in the invariant culture, or by its JSON name for
    /// an enum — ignoring nulls.
    /// </summary>
    public XrpcParams Add<T>(string key, T? value)
        where T : struct, ISpanFormattable =>
        Add(key, value is null ? null : FormatScalar(value.Value));

    /// <summary>
    /// Appends one parameter per element, all sharing <paramref name="key"/>.
    /// A null or empty sequence contributes nothing.
    /// </summary>
    public XrpcParams AddAll(string key, IEnumerable<string>? values)
    {
        if (values is not null)
        {
            foreach (var value in values)
                Add(key, value);
        }

        return this;
    }

    /// <summary>
    /// Converts a loosely typed parameter object — an anonymous type, a dictionary, or a pair
    /// sequence — into parameters, or returns <see langword="null"/> when there is nothing to
    /// send. A property whose value is a non-string sequence expands into one pair per element.
    /// </summary>
    public static XrpcParams? From(object? parameters)
    {
        switch (parameters)
        {
            case null:
                return null;
            case XrpcParams already:
                return already;
        }

        var result = new XrpcParams();

        switch (parameters)
        {
            case IEnumerable<KeyValuePair<string, string?>> pairs:
                foreach (var (key, value) in pairs)
                    result.Add(key, value);
                break;

            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key?.ToString() is { } key)
                        result.AddValue(key, entry.Value);
                }

                break;

            default:
                var properties = PropertyCache.GetOrAdd(
                    parameters.GetType(),
                    static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.GetIndexParameters().Length == 0)
                        .ToArray());

                foreach (var property in properties)
                    result.AddValue(property.Name, property.GetValue(parameters));
                break;
        }

        return result.Count > 0 ? result : null;
    }

    private void AddValue(string key, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string s:
                Add(key, s);
                return;
            case IEnumerable items:
                foreach (var item in items)
                    AddValue(key, item);
                return;
            default:
                Add(key, FormatScalar(value));
                return;
        }
    }

    /// <summary>
    /// Renders a scalar for the wire. Everything formattable goes through the invariant culture,
    /// so a client running under, say, <c>de-DE</c> does not send <c>1,5</c> where the server
    /// expects <c>1.5</c>, nor a localized date where it expects ISO 8601.
    /// </summary>
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

    /// <summary>
    /// An enum's name as the SDK's JSON serializer writes it, so a query parameter and a body
    /// field of the same enum agree — camelCase by default, or whatever
    /// <c>[JsonStringEnumMemberName]</c> says.
    /// </summary>
    private static string FormatEnum(Enum value)
    {
        var element = JsonSerializer.SerializeToElement(value, value.GetType(), AtProtoJsonDefaults.Options);
        return element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _pairs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
