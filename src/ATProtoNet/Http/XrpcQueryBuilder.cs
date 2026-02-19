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
    /// </summary>
    public static string BuildQueryString(object? parameters)
    {
        if (parameters is null)
            return string.Empty;

        IDictionary<string, string?> dict;

        if (parameters is IDictionary<string, string?> stringDict)
        {
            dict = stringDict;
        }
        else if (parameters is IDictionary genericDict)
        {
            dict = new Dictionary<string, string?>();
            foreach (DictionaryEntry entry in genericDict)
            {
                var key = entry.Key?.ToString();
                if (key is not null)
                    dict[key] = entry.Value?.ToString();
            }
        }
        else
        {
            // Treat as anonymous object — reflect properties
            dict = new Dictionary<string, string?>();
            foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = prop.GetValue(parameters);
                if (value is not null)
                {
                    if (value is bool b)
                        dict[prop.Name] = b ? "true" : "false";
                    else
                        dict[prop.Name] = value.ToString();
                }
            }
        }

        if (dict.Count == 0)
            return string.Empty;

        var pairs = dict
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");

        return "?" + string.Join("&", pairs);
    }

    /// <summary>
    /// Convert an object to an IDictionary for XrpcClient methods.
    /// </summary>
    public static IDictionary<string, string?>? ToDictionary(object? parameters)
    {
        if (parameters is null)
            return null;

        if (parameters is IDictionary<string, string?> stringDict)
            return stringDict;

        var dict = new Dictionary<string, string?>();

        if (parameters is IDictionary genericDict)
        {
            foreach (DictionaryEntry entry in genericDict)
            {
                var key = entry.Key?.ToString();
                if (key is not null)
                    dict[key] = entry.Value?.ToString();
            }
        }
        else
        {
            foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = prop.GetValue(parameters);
                if (value is not null)
                {
                    if (value is bool b)
                        dict[prop.Name] = b ? "true" : "false";
                    else
                        dict[prop.Name] = value.ToString();
                }
            }
        }

        return dict.Count > 0 ? dict : null;
    }
}
