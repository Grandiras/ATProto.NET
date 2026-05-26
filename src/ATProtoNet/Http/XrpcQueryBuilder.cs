using System.Collections;
using System.Reflection;

namespace ATProtoNet.Http;

/// <summary>
/// Builds XRPC query strings from anonymous objects or dictionaries.
/// </summary>
internal static class XrpcQueryBuilder
{
    /// <summary>
    /// Convert an object (anonymous type, dictionary, or null) into a query string prefix
    /// like "?key=value&amp;key2=value2" or "" if no parameters.
    /// Properties whose value is a non-string <see cref="IEnumerable"/> are emitted as
    /// repeated keys per XRPC array-parameter convention.
    /// </summary>
    public static string BuildQueryString(object? parameters)
    {
        var pairs = ToQueryParams(parameters);
        if (pairs is null) return string.Empty;

        var first = true;
        var sb = new System.Text.StringBuilder();
        foreach (var (key, value) in pairs)
        {
            if (value is null) continue;
            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Convert an object to a sequence of query parameter pairs for XrpcClient methods.
    /// Duplicate keys are preserved (required for XRPC array parameters).
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>>? ToDictionary(object? parameters)
        => ToQueryParams(parameters);

    /// <summary>
    /// Convert an object to a sequence of query parameter pairs.
    /// <see cref="IEnumerable"/> property values (other than <see cref="string"/>) are
    /// expanded into one KVP per element, sharing the same key.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>>? ToQueryParams(object? parameters)
    {
        if (parameters is null)
            return null;

        if (parameters is IEnumerable<KeyValuePair<string, string?>> alreadyPairs)
            return alreadyPairs;

        var list = new List<KeyValuePair<string, string?>>();

        if (parameters is IDictionary genericDict)
        {
            foreach (DictionaryEntry entry in genericDict)
            {
                var key = entry.Key?.ToString();
                if (key is null) continue;
                AppendValue(list, key, entry.Value);
            }
        }
        else
        {
            foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                AppendValue(list, prop.Name, prop.GetValue(parameters));
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static void AppendValue(List<KeyValuePair<string, string?>> list, string key, object? value)
    {
        if (value is null) return;

        if (value is string s)
        {
            list.Add(new KeyValuePair<string, string?>(key, s));
            return;
        }

        if (value is bool b)
        {
            list.Add(new KeyValuePair<string, string?>(key, b ? "true" : "false"));
            return;
        }

        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is null) continue;
                list.Add(new KeyValuePair<string, string?>(
                    key, item is bool ib ? (ib ? "true" : "false") : item.ToString()));
            }
            return;
        }

        list.Add(new KeyValuePair<string, string?>(key, value.ToString()));
    }
}
