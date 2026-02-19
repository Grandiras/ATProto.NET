using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class AtIdentifierTests
{
    [Fact]
    public void Parse_Did_CreatesDidIdentifier()
    {
        var id = AtIdentifier.Parse("did:plc:abc123");

        Assert.True(id.IsDid);
        Assert.False(id.IsHandle);
        Assert.NotNull(id.Did);
        Assert.Equal("did:plc:abc123", id.Value);
    }

    [Fact]
    public void Parse_Handle_CreatesHandleIdentifier()
    {
        var id = AtIdentifier.Parse("alice.bsky.social");

        Assert.False(id.IsDid);
        Assert.True(id.IsHandle);
        Assert.NotNull(id.Handle);
        Assert.Equal("alice.bsky.social", id.Value);
    }

    [Fact]
    public void FromDid_Wraps_Did()
    {
        var did = Did.Parse("did:plc:abc123");
        var id = AtIdentifier.FromDid(did);

        Assert.True(id.IsDid);
        Assert.Equal(did, id.Did);
    }

    [Fact]
    public void FromHandle_Wraps_Handle()
    {
        var handle = Handle.Parse("alice.bsky.social");
        var id = AtIdentifier.FromHandle(handle);

        Assert.True(id.IsHandle);
        Assert.Equal(handle, id.Handle);
    }

    [Fact]
    public void TryParse_ValidDid_Succeeds()
    {
        var result = AtIdentifier.TryParse("did:plc:abc123", out var id);
        Assert.True(result);
        Assert.NotNull(id);
        Assert.True(id.IsDid);
    }

    [Fact]
    public void TryParse_ValidHandle_Succeeds()
    {
        var result = AtIdentifier.TryParse("alice.bsky.social", out var id);
        Assert.True(result);
        Assert.NotNull(id);
        Assert.True(id.IsHandle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TryParse_Invalid_ReturnsFalse(string? value)
    {
        var result = AtIdentifier.TryParse(value, out var id);
        Assert.False(result);
        Assert.Null(id);
    }

    [Fact]
    public void Equality_SameDidValues_AreEqual()
    {
        var a = AtIdentifier.Parse("did:plc:abc123");
        var b = AtIdentifier.Parse("did:plc:abc123");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_SameHandleValues_AreEqual()
    {
        var a = AtIdentifier.Parse("alice.bsky.social");
        var b = AtIdentifier.Parse("ALICE.bsky.social");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentTypes_AreNotEqual()
    {
        var a = AtIdentifier.Parse("did:plc:abc123");
        var b = AtIdentifier.Parse("alice.bsky.social");

        Assert.NotEqual(a, b);
    }
}
