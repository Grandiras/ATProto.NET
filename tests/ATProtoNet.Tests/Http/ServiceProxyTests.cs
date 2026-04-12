using ATProtoNet.Http;

namespace ATProtoNet.Tests.Http;

public class ServiceProxyTests
{
    [Fact]
    public void Build_DidWithFragment_ReturnsCorrectHeader()
    {
        var result = ServiceProxy.Build("did:web:api.bsky.app", "#bsky_appview");
        Assert.Equal("did:web:api.bsky.app#bsky_appview", result);
    }

    [Fact]
    public void Build_DidWithoutHashPrefix_AddsHash()
    {
        var result = ServiceProxy.Build("did:web:api.bsky.app", "bsky_appview");
        Assert.Equal("did:web:api.bsky.app#bsky_appview", result);
    }

    [Fact]
    public void Build_NullDid_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceProxy.Build(null!, "#bsky_appview"));
    }

    [Fact]
    public void Build_NullServiceId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceProxy.Build("did:web:example.com", null!));
    }

    [Fact]
    public void BskyAppViewHeader_HasCorrectValue()
    {
        Assert.Equal("did:web:api.bsky.app#bsky_appview", ServiceProxy.BskyAppViewHeader);
    }

    [Fact]
    public void BskyChatHeader_HasCorrectValue()
    {
        Assert.Equal("did:web:api.bsky.chat#bsky_chat", ServiceProxy.BskyChatHeader);
    }

    [Fact]
    public void WellKnownConstants_AreCorrect()
    {
        Assert.Equal("#bsky_appview", ServiceProxy.BskyAppView);
        Assert.Equal("#bsky_chat", ServiceProxy.BskyChat);
        Assert.Equal("#atproto_labeler", ServiceProxy.AtProtoLabeler);
        Assert.Equal("#atproto_pds", ServiceProxy.AtProtoPds);
    }
}
