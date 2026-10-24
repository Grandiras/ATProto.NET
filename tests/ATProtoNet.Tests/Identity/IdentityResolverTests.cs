using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public sealed class IdentityResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Did Alice = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did Mallory = Did.Parse("did:plc:mmmmmmmmmmmmmmmmmmmmmmmm");
    private static readonly Handle AliceHandle = Handle.Parse("alice.example.com");

    private readonly Dictionary<Did, DidDocument> _documents = [];
    private readonly Dictionary<Handle, Func<Did?>> _handles = [];
    private readonly List<string> _fetches = [];

    private IdentityResolver Create(out StubHandleResolver handles)
    {
        var dids = new StubDidResolver(did => _documents.TryGetValue(did, out var d) ? Task.FromResult(d)
            : Task.FromException<DidDocument>(new IdentityResolutionException(IdentityResolutionError.NotFound, "none", did)));
        handles = new StubHandleResolver(h => Task.FromResult(_handles.TryGetValue(h, out var answer) ? answer() : null));
        return new(new Recorder(dids, _fetches), handles);
    }

    private IdentityResolver Create() => Create(out _);

    [Fact]
    [Trait("Spec", "IDN-34")]
    public async Task ResolveAsync_DidWhoseHandleResolvesBack_IsVerified()
    {
        _documents[Alice] = Docs.Parse(Alice.Value, AliceHandle.Value);
        _handles[AliceHandle] = () => Alice;

        var identity = await Create().ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal((Alice, AliceHandle, true), (identity.Did, identity.Handle, identity.HandleVerified));
        Assert.Equal(new Uri("https://pds.example.com"), identity.Pds);
    }

    [Fact]
    [Trait("Spec", "IDN-34")]
    public async Task ResolveAsync_DidWhoseHandleResolvesElsewhere_IsHandleInvalid()
    {
        _documents[Alice] = Docs.Parse(Alice.Value, AliceHandle.Value);
        _handles[AliceHandle] = () => Mallory;

        var identity = await Create().ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal((Handle.Invalid, false), (identity.Handle, identity.HandleVerified));
    }

    [Theory]
    [Trait("Spec", "IDN-34")]
    [InlineData("conflict")]
    [InlineData("bug")]
    [InlineData("none")]
    public async Task ResolveAsync_DidWhoseHandleStepFails_IsHandleInvalidButResolved(string failure)
    {
        _documents[Alice] = Docs.Parse(Alice.Value, AliceHandle.Value);
        _handles[AliceHandle] = failure switch
        {
            "conflict" => () => throw new IdentityResolutionException(IdentityResolutionError.HandleConflict, "two"),
            "bug" => () => throw new InvalidOperationException("bug"),
            _ => () => null,
        };

        var identity = await Create().ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal((Alice, Handle.Invalid, false), (identity.Did, identity.Handle, identity.HandleVerified));
    }

    [Fact]
    [Trait("Spec", "IDN-34")]
    public async Task ResolveAsync_DidClaimingNoHandle_HasNoHandle()
    {
        _documents[Alice] = Docs.Parse(Alice.Value);

        var identity = await Create().ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal((null, false), (identity.Handle, identity.HandleVerified));
    }

    [Fact]
    [Trait("Spec", "IDN-33")]
    public async Task ResolveAsync_HandleClaimedBack_IsVerified()
    {
        _documents[Alice] = Docs.Parse(Alice.Value, AliceHandle.Value);
        _handles[AliceHandle] = () => Alice;

        var identity = await Create().ResolveAsync(AliceHandle, cancellationToken: Ct);

        Assert.Equal((Alice, AliceHandle, true), (identity.Did, identity.Handle, identity.HandleVerified));
    }

    [Fact]
    [Trait("Spec", "IDN-33")]
    public async Task ResolveAsync_HandleNotClaimedBack_IsHandleInvalid()
    {
        _documents[Mallory] = Docs.Parse(Mallory.Value, "mallory.example.com");
        _handles[AliceHandle] = () => Mallory;

        var identity = await Create().ResolveAsync(AliceHandle, cancellationToken: Ct);

        Assert.Equal((Mallory, Handle.Invalid, false), (identity.Did, identity.Handle, identity.HandleVerified));
    }

    [Fact]
    [Trait("Spec", "IDN-33")]
    public async Task ResolveAsync_HandleThatDoesNotResolve_ThrowsHandleNotFound()
    {
        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().ResolveAsync(AliceHandle, cancellationToken: Ct));

        Assert.Equal(IdentityResolutionError.HandleNotFound, ex.Error);
    }

    [Fact]
    public async Task ResolveAsync_UnresolvableDid_Propagates()
    {
        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().ResolveAsync(Alice, cancellationToken: Ct));

        Assert.Equal(IdentityResolutionError.NotFound, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-35")]
    public async Task ResolveAsync_BypassCache_FetchesFreshAndForgetsTheHandle()
    {
        _documents[Alice] = Docs.Parse(Alice.Value, AliceHandle.Value);
        _handles[AliceHandle] = () => Alice;
        var resolver = Create(out var handles);

        await resolver.ResolveAsync(Alice, new() { BypassCache = true }, cancellationToken: Ct);
        await resolver.ResolveAsync(AliceHandle, new() { BypassCache = true }, cancellationToken: Ct);

        Assert.Equal(["fresh", "fresh"], _fetches);
        Assert.Equal([AliceHandle, AliceHandle], handles.Invalidated);
    }

    [Fact]
    [Trait("Spec", "IDN-36")]
    public async Task InvalidateAsync_ReachesTheResolverOfTheIdentifiersKind()
    {
        var resolver = Create(out var handles);

        await resolver.InvalidateAsync(AliceHandle, cancellationToken: Ct);
        await resolver.InvalidateAsync(Alice, cancellationToken: Ct);

        Assert.Equal([AliceHandle], handles.Invalidated);
        Assert.Equal(["invalidate"], _fetches);
    }

    private sealed class Recorder(IDidResolver inner, List<string> log) : IDidResolver
    {
        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) => inner.ResolveAsync(did, cancellationToken);

        public Task<DidDocument> FetchFreshAsync(Did did, CancellationToken cancellationToken = default)
        {
            log.Add("fresh");
            return inner.ResolveAsync(did, cancellationToken);
        }

        public ValueTask InvalidateAsync(Did did, CancellationToken cancellationToken = default)
        {
            log.Add("invalidate");
            return ValueTask.CompletedTask;
        }
    }
}
