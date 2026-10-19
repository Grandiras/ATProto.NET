namespace ATProtoNet.Tests.Syntax;

public class AtIdentifierTests
{
    [Theory]
    [InlineData("did:plc:abc123", true)]
    [InlineData("alice.bsky.social", false)]
    [Trait("Spec", "SYN-19"), Trait("Spec", "SYN-20")]
    public void Parse_KnowsWhetherItIsADidOrAHandle(string value, bool isDid)
    {
        var id = AtIdentifier.Parse(value);

        Assert.Equal((isDid, !isDid), (id.IsDid, id.IsHandle));
        Assert.Equal(isDid, id.Did is not null);
        Assert.Equal(!isDid, id.Handle is not null);
        Assert.Equal(value, id.Value);
    }

    [Fact]
    [Trait("Spec", "SYN-19")]
    public void Parse_TextStartingWithDid_IsNotRetriedAsAHandle()
    {
        Assert.False(AtIdentifier.TryParse("did:thing.test", out _));
        Assert.False(AtIdentifier.TryParse("did:", out _));
        Assert.False(AtIdentifier.TryParse("DID:method:val", out _));
    }

    [Fact]
    [Trait("Spec", "SYN-20")]
    public void Parse_Handle_IsNormalizedLikeHandle()
    {
        var id = AtIdentifier.Parse("Alice.Bsky.Social");

        Assert.Equal("alice.bsky.social", id.Value);
        Assert.Equal(Handle.Parse("alice.bsky.social"), id.Handle);
    }

    [Fact]
    [Trait("Spec", "SYN-20")]
    public void Convert_DidAndHandle_WrapTheirIdentifier()
    {
        var did = Did.Parse("did:plc:abc123");
        var handle = Handle.Parse("alice.bsky.social");
        AtIdentifier fromDid = did;
        AtIdentifier fromHandle = handle;

        Assert.Equal(did, fromDid.Did);
        Assert.Equal(handle, fromHandle.Handle);
        Assert.Equal(fromDid, AtIdentifier.Parse(did.Value));
    }
}
