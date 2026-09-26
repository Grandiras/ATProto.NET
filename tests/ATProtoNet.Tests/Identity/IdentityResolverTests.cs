using ATProtoNet.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Bidirectional handle verification: a handle counts only when the DID document claims it and
/// it resolves back to the DID, and <c>handle.invalid</c> stands in otherwise.
/// </summary>
public class IdentityResolverTests
{
    private static readonly Did AliceDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Handle Alice = Handle.Parse("alice.example.com");

    private readonly IDidResolver _dids = Substitute.For<IDidResolver>();
    private readonly IHandleResolver _handles = Substitute.For<IHandleResolver>();

    private IdentityResolver Create() => new(_dids, _handles);

    private void Publish(Did did, string? handle, string? pds = "https://pds.example.com") =>
        _dids.ResolveAsync(did, Arg.Any<CancellationToken>()).Returns(DidDocs.Parse(did.Value, handle, pds));

    [Fact]
    public async Task ResolveAsync_DidWhoseHandleResolvesBack_IsVerified()
    {
        Publish(AliceDid, "alice.example.com");
        _handles.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns(AliceDid);

        var identity = await Create().ResolveAsync(AtIdentifier.FromDid(AliceDid));

        Assert.Equal(AliceDid, identity.Did);
        Assert.Equal(Alice, identity.Handle);
        Assert.True(identity.HandleVerified);
        Assert.Equal(new Uri("https://pds.example.com"), identity.PdsEndpoint);
    }

    [Fact]
    public async Task ResolveAsync_DidWhoseHandleResolvesElsewhere_IsHandleInvalid()
    {
        // Anyone can claim any handle in their own document.
        Publish(AliceDid, "alice.example.com");
        _handles.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns(Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb"));

        var identity = await Create().ResolveAsync(AtIdentifier.FromDid(AliceDid));

        Assert.Equal(Handle.Invalid, identity.Handle);
        Assert.False(identity.HandleVerified);
    }

    [Fact]
    public async Task ResolveAsync_DidWhoseHandleAuthoritiesFail_IsHandleInvalidButResolved()
    {
        Publish(AliceDid, "alice.example.com");
        _handles.ResolveAsync(Alice, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DidResolutionException("conflict", DidResolutionErrorKind.HandleConflict));

        var identity = await Create().ResolveAsync(AtIdentifier.FromDid(AliceDid));

        Assert.Equal(AliceDid, identity.Did);
        Assert.Equal(Handle.Invalid, identity.Handle);
        Assert.False(identity.HandleVerified);
    }

    [Fact]
    public async Task ResolveAsync_DidClaimingNoHandle_HasNoHandle()
    {
        Publish(AliceDid, handle: null, pds: null);

        var identity = await Create().ResolveAsync(AtIdentifier.FromDid(AliceDid));

        Assert.Null(identity.Handle);
        Assert.False(identity.HandleVerified);
        Assert.Null(identity.PdsEndpoint);
        await _handles.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task ResolveAsync_HandleClaimedBack_IsVerified()
    {
        _handles.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns(AliceDid);
        Publish(AliceDid, "Alice.Example.com");

        var identity = await Create().ResolveAsync(AtIdentifier.FromHandle(Alice));

        Assert.Equal(AliceDid, identity.Did);
        Assert.Equal(Alice, identity.Handle);
        Assert.True(identity.HandleVerified);
    }

    [Fact]
    public async Task ResolveAsync_HandleNotClaimedBack_IsHandleInvalid()
    {
        // A domain pointed at someone else's DID does not become their handle.
        _handles.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns(AliceDid);
        Publish(AliceDid, "someone-else.example.com");

        var identity = await Create().ResolveAsync(AtIdentifier.FromHandle(Alice));

        Assert.Equal(AliceDid, identity.Did);
        Assert.Equal(Handle.Invalid, identity.Handle);
        Assert.False(identity.HandleVerified);
    }

    [Fact]
    public async Task ResolveAsync_HandleThatDoesNotResolve_ThrowsHandleNotFound()
    {
        _handles.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns((Did?)null);

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => Create().ResolveAsync(AtIdentifier.FromHandle(Alice)));

        Assert.Equal(DidResolutionErrorKind.HandleNotFound, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_UnresolvableDid_Propagates()
    {
        _dids.ResolveAsync(AliceDid, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DidResolutionException("gone", DidResolutionErrorKind.Deactivated, AliceDid));

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => Create().ResolveAsync(AtIdentifier.FromDid(AliceDid)));

        Assert.Equal(DidResolutionErrorKind.Deactivated, ex.Kind);
    }

    [Fact]
    public void Handle_Invalid_IsTheReservedName() =>
        Assert.Equal("handle.invalid", Handle.Invalid.Value);

    [Fact]
    public void CreateDefault_CachesDidDocuments()
    {
        using var resolver = IdentityResolver.CreateDefault();

        Assert.IsType<CachingDidResolver>(resolver.DidResolver);
    }
}
