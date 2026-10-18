namespace ATProtoNet.Architecture.Tests;

// The checker itself, on the deliberate violations in Samples/.
public class LayerRulesTests
{
    private static readonly Layers SampleLayers = new(
    [
        ("ATProtoNet.Architecture.Samples.Low", new("Low", 0)),
        ("ATProtoNet.Architecture.Samples.High", new("High", 1)),
        ("ATProtoNet.Architecture.Samples.Ring.A", new("A", 1)),
        ("ATProtoNet.Architecture.Samples.Ring.B", new("B", 1)),
    ]);

    private static readonly IReadOnlyList<string> SampleViolations = LayerRules.Violations(
        Dependencies.Of(Repository.Module("ATProtoNet.Architecture.Tests")).Where(u => u.Type.Namespace.StartsWith("ATProtoNet.Architecture.Samples", StringComparison.Ordinal)),
        SampleLayers);

    [Fact]
    public void Violations_UpwardReference_Reported() =>
        Assert.Contains(SampleViolations, v => v.StartsWith("ATProtoNet.Architecture.Samples.Low.UsesHigh (layer 0", StringComparison.Ordinal));

    [Fact]
    public void Violations_ClockReadInLayer0_Reported() =>
        Assert.Contains(SampleViolations, v => v.Contains("ReadsClock (layer 0) reads System.DateTime::get_UtcNow", StringComparison.Ordinal));

    [Fact]
    public void Violations_Cycle_Reported() =>
        Assert.Contains(SampleViolations, v => v is "namespace cycle: A -> B -> A");

    [Fact]
    public void Violations_DownwardReference_NotReported() =>
        Assert.DoesNotContain(SampleViolations, v => v.StartsWith("ATProtoNet.Architecture.Samples.High.Top", StringComparison.Ordinal));
}
