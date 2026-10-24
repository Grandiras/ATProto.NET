using System;
using System.Collections.Generic;
using System.Text.Json;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

// The typed field readers of LexiconParser: each reports a violation where the JSON kind is wrong.
internal sealed partial class LexiconParser
{
    private string? RequiredString(JsonElement node, string name, string pointer)
    {
        if (node.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String) return element.GetString();
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is a required string.", pointer + "/" + Escape(name));
        return null;
    }

    private string? OptionalString(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is a string.", pointer + "/" + Escape(name));
        return null;
    }

    private long? OptionalLong(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value)) return value;
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is an integer.", pointer + "/" + Escape(name));
        return null;
    }

    private int? OptionalInt(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)) return value;
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is an integer.", pointer + "/" + Escape(name));
        return null;
    }

    private bool? OptionalBool(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) return element.GetBoolean();
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is a boolean.", pointer + "/" + Escape(name));
        return null;
    }

    private List<string>? StringList(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        var list = new List<string>();
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) break;
                list.Add(item.GetString()!);
            }
        if (element.ValueKind == JsonValueKind.Array && list.Count == element.GetArrayLength()) return list;
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is an array of strings.", pointer + "/" + Escape(name));
        return null;
    }

    private List<long>? LongList(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        var list = new List<long>();
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var value)) break;
                list.Add(value);
            }
        if (element.ValueKind == JsonValueKind.Array && list.Count == element.GetArrayLength()) return list;
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is an array of integers.", pointer + "/" + Escape(name));
        return null;
    }

    private Dictionary<string, string>? Localized(JsonElement node, string name, string pointer)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var entry in element.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) break;
                map[entry.Name] = entry.Value.GetString()!;
            }
        if (element.ValueKind == JsonValueKind.Object && map.Count == Count(element)) return map;
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is an object of strings by language.", pointer + "/" + Escape(name));
        return null;
    }

    private static int Count(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject()) count++;
        return count;
    }

    // RFC 6901: "~" → "~0", "/" → "~1".
    internal static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
}
