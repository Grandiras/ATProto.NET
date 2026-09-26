using System.Net;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Tests.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// How the Blazor OAuth service keeps pending logins: in the state store it is given, limited
/// per remote address.
/// </summary>
public sealed class AtProtoOAuthServiceStateTests : IDisposable
{
    private readonly StubServer _server = new() { Respond = AuthorizationServer };
    private readonly HttpClient _http;

    public AtProtoOAuthServiceStateTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private AtProtoOAuthService Service(IOAuthStateStore store) => new(
        new AtProtoOAuthServerOptions
        {
            ClientMetadata = new OAuthClientMetadata
            {
                ClientId = ClientId,
                RedirectUris = ["https://app.example.com/atproto/callback"],
            },
            HttpClient = _http,
        },
        NullLoggerFactory.Instance,
        AliceIdentity(),
        store);

    private static DefaultHttpContext Request(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example.com");
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        return context;
    }

    [Fact]
    public async Task StartLoginAsync_KeepsThePendingLoginInTheGivenStoreUnderTheRemoteAddress()
    {
        OAuthPendingAuthorization? stored = null;
        var store = Substitute.For<IOAuthStateStore>();
        store.SetAsync(Arg.Do<OAuthPendingAuthorization>(pending => stored = pending), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var service = Service(store);

        var url = await service.StartLoginAsync(Request("203.0.113.7"), AliceHandle.Value);

        Assert.NotNull(stored);
        Assert.Equal("203.0.113.7", stored.RequesterId);
        Assert.Equal("https://app.example.com/atproto/callback", stored.RedirectUri);
        Assert.StartsWith($"{Issuer}/oauth/authorize?client_id=", url);
    }

    [Fact]
    public async Task StartLoginAsync_AnIpv6Address_CountsTowardsItsSlash64()
    {
        OAuthPendingAuthorization? stored = null;
        var store = Substitute.For<IOAuthStateStore>();
        store.SetAsync(Arg.Do<OAuthPendingAuthorization>(pending => stored = pending), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var service = Service(store);

        await service.StartLoginAsync(Request("2001:db8:5:6:1:2:3:4"), AliceHandle.Value);

        Assert.Equal("2001:db8:5:6::/64", stored!.RequesterId);
    }

    [Fact]
    public async Task StartLoginAsync_OneAddressFloodingTheLogin_DisplacesOnlyItsOwn()
    {
        var store = new InMemoryOAuthStateStore(maxPerRequester: 5);
        using var service = Service(store);

        await service.StartLoginAsync(Request("198.51.100.1"), AliceHandle.Value);
        for (var i = 0; i < 20; i++)
            await service.StartLoginAsync(Request("203.0.113.7"), AliceHandle.Value);

        Assert.Equal(6, store.Count);
    }
}
