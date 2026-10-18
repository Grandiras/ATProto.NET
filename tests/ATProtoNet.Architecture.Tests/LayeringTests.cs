namespace ATProtoNet.Architecture.Tests;

public class LayeringTests
{
    [Fact]
    public void Violations_AtProtoNet_None() =>
        Assert.Empty(LayerRules.Violations(Dependencies.Of(Repository.Module("ATProtoNet")), Layers.AtProtoNet));
}
