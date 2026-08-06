using System.Collections;
using System.Globalization;
using System.Reflection;

namespace ATProtoNet.Http;

/// <summary>
/// Converts loosely-typed parameter objects — anonymous types, dictionaries, or an
/// existing pair sequence — into XRPC query parameters.
/// </summary>
/// <remarks>
/// This is the entry point for <see cref="AtProtoClient.QueryAsync{T}"/>, where callers
/// describe parameters with an anonymous object. Lexicon clients inside the SDK build
/// their parameters with <see cref="XrpcParams"/> instead.
/// </remarks>
internal static class XrpcQueryBuilder
{
    /// <summary>
    /// Converts an object to a sequence of query parameter pairs, or null when there is
    /// nothing to send. Duplicate keys are preserved: a property whose value is a
    /// non-string <see cref="IEnumerable"/> expands into one pair per element, which is
    /// how XRPC transmits array parameters.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>>? ToQueryParams(object? parameters)
    {
        if (parameters is null)
            return null;

        if (parameters is IEnumerable<KeyValuePair<string, string?>> alreadyPairs)
            return alreadyPairs;

        var list = new List<KeyValuePair<string, string?>>();

        if (parameters is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key?.ToString() is { } key)
                    AppendValue(list, key, entry.Value);
            }
        }
        else
        {
            foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                AppendValue(list, prop.Name, prop.GetValue(parameters));
        }

        return list.Count > 0 ? list : null;
    }

    private static void AppendValue(List<KeyValuePair<string, string?>> list, string key, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string s:
                list.Add(new KeyValuePair<string, string?>(key, s));
                return;
            case IEnumerable items:
                foreach (var item in items)
                    AppendValue(list, key, item);
                return;
            default:
                list.Add(new KeyValuePair<string, string?>(key, Format(value)));
                return;
        }
    }

    /// <summary>
    /// Renders a scalar for the wire: booleans lowercase per JSON/XRPC convention, and
    /// everything formattable in the invariant culture so a client running under, say,
    /// <c>de-DE</c> does not send <c>1,5</c> where the server expects <c>1.5</c>.
    /// </summary>
    private static string? Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
