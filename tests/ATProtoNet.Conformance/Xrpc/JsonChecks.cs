using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance;

/// <summary>JSON comparisons for the XRPC suite.</summary>
public static class JsonChecks
{
    /// <summary>
    /// Whether <paramref name="actual"/> holds everything <paramref name="expected"/> lists:
    /// objects by property (extras allowed), arrays element by element and equal in length, scalars equal.
    /// </summary>
    public static bool Contains(JsonNode? actual, JsonNode? expected) => (actual, expected) switch
    {
        (_, null) => actual is null,
        (JsonObject a, JsonObject e) => e.All(p => a.TryGetPropertyValue(p.Key, out var value) && Contains(value, p.Value)),
        (JsonArray a, JsonArray e) => a.Count == e.Count && a.Zip(e).All(pair => Contains(pair.First, pair.Second)),
        (null, _) => false,
        _ => JsonNode.DeepEquals(actual, expected),
    };

    /// <summary>
    /// Whether the object at <paramref name="path"/> (dot-separated, empty for the root) of the
    /// JSON text <paramref name="body"/> has exactly one <c>$type</c> and writes it first.
    /// </summary>
    public static bool WritesTypeOnceAndFirst(byte[] body, string path)
    {
        using var document = JsonDocument.Parse(body);
        var node = document.RootElement;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            if (!node.TryGetProperty(segment, out node)) return false;
        var names = node.ValueKind is JsonValueKind.Object ? node.EnumerateObject().Select(p => p.Name).ToList() : [];
        return names.Count > 0 && names[0] == "$type" && names.Count(n => n == "$type") == 1;
    }

    /// <summary>Whether two JSON texts are equal as JSON (property order is free).</summary>
    public static bool Equal(JsonNode expected, ReadOnlySpan<byte> actual) =>
        JsonElement.DeepEquals(JsonDocument.Parse(expected.ToJsonString()).RootElement, JsonDocument.Parse(actual.ToArray()).RootElement);
}
