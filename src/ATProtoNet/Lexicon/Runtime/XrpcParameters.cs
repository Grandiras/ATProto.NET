using System.Globalization;

namespace ATProtoNet.Lexicon.Runtime;

/// <summary>Collects the query parameters of an XRPC call as name and text pairs, in the order written (LEX-63).</summary>
/// <remarks>Values are formatted in the invariant culture; absent values are skipped; an array writes one pair per item.</remarks>
public sealed class XrpcParameterWriter
{
    private readonly List<KeyValuePair<string, string>> _pairs = [];

    /// <summary>The pairs written so far.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

    /// <summary>Writes a string; skips <see langword="null"/>.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    public void Add(string name, string? value)
    {
        if (value is not null) _pairs.Add(new(name, value));
    }

    /// <summary>Writes an integer; skips <see langword="null"/>.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    public void Add(string name, long? value)
    {
        if (value is { } number) _pairs.Add(new(name, number.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Writes a boolean as <c>true</c> or <c>false</c>; skips <see langword="null"/>.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    public void Add(string name, bool? value)
    {
        if (value is { } flag) _pairs.Add(new(name, flag ? "true" : "false"));
    }

    /// <summary>Writes one pair per item; skips <see langword="null"/>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="values">The items.</param>
    /// <param name="format">The text of an item.</param>
    public void AddAll<T>(string name, IEnumerable<T>? values, Func<T, string> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (values is null) return;
        foreach (var value in values) _pairs.Add(new(name, format(value)));
    }
}

/// <summary>Reads the query parameters of an XRPC request strictly, for server binding (LEX-63).</summary>
/// <param name="pairs">The decoded name and value pairs.</param>
/// <remarks>Every failure is a <see cref="FormatException"/> naming the parameter: a missing required one, a repeated single one, or an invalid value.</remarks>
public sealed class XrpcParameterReader(IEnumerable<KeyValuePair<string, string>> pairs)
{
    private readonly ILookup<string, string> _values = pairs.ToLookup(p => p.Key, p => p.Value, StringComparer.Ordinal);

    /// <summary>A single string parameter.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    public string? GetString(string name)
    {
        var values = _values[name].ToList();
        return values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => throw new FormatException($"The parameter \"{name}\" is given more than once."),
        };
    }

    /// <summary>A single parameter parsed by <paramref name="parse"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="parse">Parses the text; throws <see cref="FormatException"/> when invalid.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    public T? Get<T>(string name, Func<string, T> parse) where T : class => GetString(name) is { } text ? Parse(name, text, parse) : null;

    /// <summary>A single value-type parameter parsed by <paramref name="parse"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="parse">Parses the text; throws <see cref="FormatException"/> when invalid.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    public T? GetValue<T>(string name, Func<string, T> parse) where T : struct => GetString(name) is { } text ? Parse(name, text, parse) : null;

    /// <summary>A repeated parameter, each item parsed by <paramref name="parse"/>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="parse">Parses an item.</param>
    /// <returns>The items, or <see langword="null"/> when absent.</returns>
    public IReadOnlyList<T>? GetAll<T>(string name, Func<string, T> parse) =>
        _values.Contains(name) ? [.. _values[name].Select(text => Parse(name, text, parse))] : null;

    /// <summary>A required parameter: absent is a <see cref="FormatException"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value read, or <see langword="null"/> when absent.</param>
    /// <returns>The value.</returns>
    public static T Required<T>(string name, T? value) where T : class =>
        value ?? throw new FormatException($"The required parameter \"{name}\" is missing.");

    /// <summary>A required value-type parameter: absent is a <see cref="FormatException"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value read, or <see langword="null"/> when absent.</param>
    /// <returns>The value.</returns>
    public static T RequiredValue<T>(string name, T? value) where T : struct =>
        value ?? throw new FormatException($"The required parameter \"{name}\" is missing.");

    /// <summary>The text of an integer parameter.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The integer.</returns>
    public static long ParseInt64(string text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException($"\"{text}\" is not an integer.");

    /// <summary>The text of a boolean parameter: <c>true</c> or <c>false</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The boolean.</returns>
    public static bool ParseBoolean(string text) => text switch
    {
        "true" => true,
        "false" => false,
        _ => throw new FormatException($"\"{text}\" is not true or false."),
    };

    private static T Parse<T>(string name, string text, Func<string, T> parse)
    {
        try
        {
            return parse(text);
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new FormatException($"The parameter \"{name}\" is invalid: {e.Message}", e);
        }
    }
}
