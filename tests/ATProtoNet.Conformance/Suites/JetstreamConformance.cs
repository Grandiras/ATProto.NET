using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// Jetstream: the v1 and v2 frames of <c>streaming/jetstream-events.json</c>, and the segment
/// pieces of <c>streaming/jetstream-segments.json</c> (the reference encoder's golden block, a
/// real sealed header, and hostile variants of both).
/// </summary>
public abstract class JetstreamConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private static readonly IReadOnlyDictionary<string, JsonNode> EventCases = Vectors.Cases("streaming/jetstream-events.json");
    private static readonly IReadOnlyDictionary<string, JsonNode> SegmentCases = Vectors.Cases("streaming/jetstream-segments.json");

    /// <summary>The frame vectors, by ID.</summary>
    public static TheoryData<string> Events() => new(EventCases.Keys);

    /// <summary>The segment vectors, by ID.</summary>
    public static TheoryData<string> Segments() => new(SegmentCases.Keys);

    /// <summary>A frame is read, or dropped, as its vector says.</summary>
    [Theory]
    [Trait("Spec", "STR-71")]
    [MemberData(nameof(Events))]
    public void ParseJetstreamFrame_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = EventCases[vectorId];

        var actual = Adapter.ParseJetstreamFrame((string)vector["frame"]!, (int)vector["protocol"]!);

        AssertMatches(vector["expect"]!.AsObject(), actual);
    }

    /// <summary>A segment block or header decodes to what its vector lists, or is rejected.</summary>
    [Theory]
    [Trait("Spec", "STR-76")]
    [MemberData(nameof(Segments))]
    public void DecodeSegment_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = SegmentCases[vectorId];
        var bytes = Input(vector);
        var expect = vector["expect"]!;

        if ((string)vector["part"]! == "header")
        {
            var header = Adapter.ReadJetstreamSegmentHeader(bytes);
            if (expect is JsonValue)
                Assert.Null(header);
            else
                AssertMatches(expect.AsObject(), header);
            return;
        }

        var rows = Adapter.DecodeJetstreamBlock(bytes);
        if (expect is JsonValue)
        {
            Assert.Null(rows);
            return;
        }

        Assert.NotNull(rows);
        Assert.Equal(expect.AsArray().Count, rows.Count);
        foreach (var (expected, actual) in expect.AsArray().Zip(rows))
        {
            var row = expected!.AsObject().DeepClone().AsObject();
            row.Remove("event", out var evt);
            AssertMatches(row, actual!.AsObject());
            AssertMatches(evt!.AsObject(), actual["event"]?.AsObject());
        }
    }

    private static byte[] Input(JsonNode vector)
    {
        var bytes = vector["file"] is { } file ? Vectors.Bytes($"streaming/{file}") : Convert.FromHexString((string)vector["hex"]!);
        if (vector["patch"] is { } patch)
            Convert.FromHexString((string)patch["hex"]!).CopyTo(bytes, (int)patch["offset"]!);
        return vector["truncateBy"] is { } cut ? bytes[..^(int)cut] : bytes;
    }
}
