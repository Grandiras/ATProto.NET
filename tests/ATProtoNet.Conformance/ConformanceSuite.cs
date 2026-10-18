using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance;

/// <summary>
/// The base of every suite: one adapter instance per test class instance.
/// </summary>
public abstract class ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>The implementation under test.</summary>
    protected TAdapter Adapter { get; } = new();

    /// <summary>
    /// Skips the current test when the adapter lists <paramref name="vectorId"/> as a known
    /// failure, by its exact ID or by a key ending in <c>*</c> that prefixes it. Call it first
    /// in every vector-driven test.
    /// </summary>
    protected void Vector(string vectorId)
    {
        foreach (var (key, reason) in Adapter.KnownFailures)
            if (key == vectorId || key.EndsWith('*') && vectorId.StartsWith(key[..^1], StringComparison.Ordinal))
                Assert.Skip($"Known failure of this implementation: {reason}");
    }

    /// <summary>Asserts that <paramref name="actual"/> is the same JSON value as <paramref name="expected"/>.</summary>
    protected static void AssertJson(string expected, string? actual)
    {
        Assert.NotNull(actual);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"Expected {expected}, got {actual}");
    }

    /// <summary>
    /// Asserts that every member of <paramref name="expected"/> is in <paramref name="actual"/>
    /// with an equal value. Members the vector leaves out are not checked.
    /// </summary>
    protected static void AssertMatches(JsonObject expected, JsonObject? actual)
    {
        Assert.NotNull(actual);
        foreach (var (name, value) in expected)
            Assert.True(JsonNode.DeepEquals(value, actual[name]), $"\"{name}\": expected {value?.ToJsonString() ?? "null"}, got {actual[name]?.ToJsonString() ?? "null"} in {actual.ToJsonString()}");
    }
}
