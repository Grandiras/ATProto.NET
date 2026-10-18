using ATProtoNet.TestKit.Integration;

namespace ATProtoNet.Tests.TestKit;

public class IntegrationGateTests
{
    [Theory]
    [InlineData(Requirement.Pds)]
    [InlineData(Requirement.Spaces)]
    public void GetTraits_RequiresFact_TagsTheRequirement(Requirement requirement) =>
        Assert.Equal([new("Requirement", requirement.ToString())], new RequiresFactAttribute(requirement).GetTraits());

    [Fact]
    public void MissingPrerequisite_EveryRequirement_IsDefined()
    {
        foreach (var requirement in Enum.GetValues<Requirement>())
            _ = IntegrationGate.MissingPrerequisite(requirement);
    }
}
