using ATProtoNet.Http;

namespace ATProtoNet.Tests.Http;

public class ServiceProxyTests
{
    [Theory]
    [InlineData("#bsky_appview")]
    [InlineData("bsky_appview")] // the '#' is added when missing
    public void Build_DidAndServiceId_ReturnsTheHeader(string serviceId)
    {
        Assert.Equal("did:web:api.bsky.app#bsky_appview", ServiceProxy.Build("did:web:api.bsky.app", serviceId));
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceProxy.Build(null!, "#bsky_appview"));
        Assert.Throws<ArgumentNullException>(() => ServiceProxy.Build("did:web:example.com", null!));
    }
}
