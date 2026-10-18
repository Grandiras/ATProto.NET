using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// <c>subscribeRepos</c> frames and Sync 1.1 verification: <c>streaming/firehose-frames.json</c>
/// (how each frame is read or dropped) and <c>streaming/sync-cases.json</c> (sequences of frames
/// against a repository's stored state, the live frames of <c>firehose-sync-frames.json</c>
/// among them, with the outcome each must reach).
/// </summary>
public abstract class FirehoseConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private const string FramesFile = "streaming/firehose-frames.json";
    private const string SyncFile = "streaming/sync-cases.json";

    private static readonly IReadOnlyDictionary<string, JsonNode> FrameCases = Vectors.Cases(FramesFile);
    private static readonly JsonNode Sync = Vectors.Json(SyncFile);
    private static readonly IReadOnlyDictionary<string, JsonNode> SyncCases = Vectors.Cases(SyncFile);

    /// <summary>The frame vectors, by ID.</summary>
    public static TheoryData<string> Frames() => new(FrameCases.Keys);

    /// <summary>The sync vectors, by ID.</summary>
    public static TheoryData<string> SyncSequences() => new(SyncCases.Keys);

    /// <summary>A frame is read, or dropped, as its vector says.</summary>
    [Theory]
    [Trait("Spec", "STR-11")]
    [MemberData(nameof(Frames))]
    public void ParseFirehoseFrame_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = FrameCases[vectorId];

        var actual = Adapter.ParseFirehoseFrame(Vectors.Base64(vector, "frame"));

        AssertMatches(vector["expect"]!.AsObject(), actual);
    }

    /// <summary>Each frame of a sequence reaches its outcome and leaves the state its vector names.</summary>
    [Theory]
    [Trait("Spec", "STR-41")]
    [MemberData(nameof(SyncSequences))]
    public void ApplySyncEvent_Sequence_ReachesEachOutcome(string vectorId)
    {
        Vector(vectorId);
        var vector = SyncCases[vectorId];
        var now = DateTimeOffset.Parse((string)Sync["now"]!);
        var key = vector.AsObject().TryGetPropertyValue("signingKey", out var k) ? (string?)k : (string)Sync["signingKey"]!;
        var state = StateOf(vector["state"]);

        foreach (var (step, index) in vector["steps"]!.AsArray().Select((s, i) => (s!, i)))
        {
            var verdict = Adapter.ApplySyncEvent(Vectors.Base64(step, "frame"), state, key, now);

            Assert.True(Enum.Parse<SyncOutcome>((string)step["expect"]!, ignoreCase: true) == verdict.Outcome,
                $"Step {index}: expected {step["expect"]}, got {verdict.Outcome}.");
            if (verdict.Outcome is SyncOutcome.Invalid or SyncOutcome.Stale)
                Assert.Equal(state, verdict.State);
            if (StateOf(step["after"]) is { } after)
                Assert.Equal(after, verdict.State);
            state = verdict.State;
        }
    }

    private static SyncState? StateOf(JsonNode? node) =>
        node is null ? null : new((string?)node["rev"], (string?)node["data"], (bool)node["synchronized"]!);
}
