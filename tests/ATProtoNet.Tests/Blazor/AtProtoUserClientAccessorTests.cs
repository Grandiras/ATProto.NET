using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Blazor;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Services;
using ATProtoNet.Tests.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Blazor;

/// <summary>
/// <see cref="AtProtoUserClientAccessor"/> hands the components of a scope one client for the
/// signed-in user, and a new one when the user changes.
/// </summary>
public sealed class AtProtoUserClientAccessorTests : IDisposable
{
    private readonly StubServer _server = new() { Respond = _ => JsonResponse("{}") };
    private readonly TestAuthenticationState _state = new();
    private readonly IAtProtoClientFactory _factory = Substitute.For<IAtProtoClientFactory>();

    public void Dispose() => _server.Dispose();

    private static ClaimsPrincipal User(Did did) =>
        new(new ClaimsIdentity([new Claim(AtProtoClaimTypes.Did, did.Value)], "test"));

    private void ServeSessions()
    {
        _factory.CreateClientForUserAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(call => ClientForAsync(call.Arg<ClaimsPrincipal>()!));
    }

    private async Task<AtProtoClient?> ClientForAsync(ClaimsPrincipal user)
    {
        var did = Did.Parse(user.FindFirst(AtProtoClaimTypes.Did)!.Value);
        var client = new AtProtoClient(httpClient: new HttpClient(_server, disposeHandler: false));
        await client.ApplySessionAsync(PasswordSession(AccessJwt("a1"), "r1") with { Did = did });
        return client;
    }

    [Fact]
    public async Task SignedOut_ReturnsNullWithoutAskingTheFactory()
    {
        await using var accessor = new AtProtoUserClientAccessor(_state, _factory);

        Assert.Null(await accessor.GetClientAsync());
        await _factory.DidNotReceiveWithAnyArgs().CreateClientForUserAsync(default!, default);
    }

    [Fact]
    public async Task AUserWithoutADidClaim_HasNoClient()
    {
        _state.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test"));
        await using var accessor = new AtProtoUserClientAccessor(_state, _factory);

        Assert.Null(await accessor.GetClientAsync());
        await _factory.DidNotReceiveWithAnyArgs().CreateClientForUserAsync(default!, default);
    }

    [Fact]
    public async Task TheSameUser_GetsTheSameClient()
    {
        ServeSessions();
        _state.User = User(Alice);
        await using var accessor = new AtProtoUserClientAccessor(_state, _factory);

        var first = await accessor.GetClientAsync();
        var second = await accessor.GetClientAsync();

        Assert.NotNull(first);
        Assert.Same(first, second);
        await _factory.Received(1).CreateClientForUserAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherUser_GetsANewClient_AndTheOldOneIsDisposed()
    {
        ServeSessions();
        _state.User = User(Alice);
        await using var accessor = new AtProtoUserClientAccessor(_state, _factory);
        var alice = (await accessor.GetClientAsync())!;

        _state.User = User(Did.Parse("did:plc:bob"));
        var bob = await accessor.GetClientAsync();

        Assert.Equal("did:plc:bob", bob!.Did!.Value);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => alice.TryRestoreSessionAsync(Alice));
    }

    [Fact]
    public async Task SigningOut_ReleasesTheClient()
    {
        ServeSessions();
        _state.User = User(Alice);
        await using var accessor = new AtProtoUserClientAccessor(_state, _factory);
        var client = (await accessor.GetClientAsync())!;

        _state.User = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Null(await accessor.GetClientAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.TryRestoreSessionAsync(Alice));
    }

    [Fact]
    public async Task ASessionThatEnded_IsRestoredAgain()
    {
        ServeSessions();
        _state.User = User(Alice);
        await using var accessor = new AtProtoUserClientAccessor(_state, _factory);
        var first = (await accessor.GetClientAsync())!;
        await first.LogoutAsync();

        var second = await accessor.GetClientAsync();

        Assert.NotSame(first, second);
        await _factory.Received(2).CreateClientForUserAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheClient()
    {
        ServeSessions();
        _state.User = User(Alice);
        var accessor = new AtProtoUserClientAccessor(_state, _factory);
        var client = (await accessor.GetClientAsync())!;

        await accessor.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.TryRestoreSessionAsync(Alice));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => accessor.GetClientAsync());
    }

    private sealed class TestAuthenticationState : AuthenticationStateProvider
    {
        public ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(User));
    }
}
