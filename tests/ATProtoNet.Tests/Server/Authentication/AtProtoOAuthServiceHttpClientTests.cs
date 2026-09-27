using System.Net;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// Tests how <see cref="AtProtoOAuthService"/> sources its <see cref="HttpClient"/>: the named
/// client it is given is used as is and left alive, and without one the OAuth client's own keeps
/// the identity fetch policy.
/// </summary>
public class AtProtoOAuthServiceHttpClientTests
{
    [Fact]
    public async Task GivenHttpClient_IsUsedForDiscoveryAndLeftAsItWas()
    {
        using var stub = new HttpStub().Fallback(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var named = new HttpClient(stub, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(7) };

        var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance, named);

        // The OAuth metadata requests go through it; identity resolution has its own client.
        await Assert.ThrowsAnyAsync<Exception>(
            () => service.Client.Discovery.ResolveAuthorizationServerAsync("https://pds.example.com"));
        Assert.NotEmpty(stub.Requests);

        // The factory owns it: its timeout is left alone, and it survives the service.
        Assert.Equal(TimeSpan.FromSeconds(7), named.Timeout);
        service.Dispose();
        using var response = await named.GetAsync("https://example.com/still-usable");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void WithoutAnHttpClient_TheOAuthClientsOwnKeepsTheIdentityFetchPolicy()
    {
        using var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance);

        var primary = Assert.IsType<SocketsHttpHandler>(HandlerOf(service.Client.HttpClient));
        Assert.NotNull(primary.ConnectCallback);
        Assert.False(primary.AllowAutoRedirect);
    }

    [Fact]
    public void CreatedHttpClient_SendsTheSdksUserAgent()
    {
        using var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance);

        Assert.StartsWith("ATProtoNet/", service.Client.HttpClient.DefaultRequestHeaders.UserAgent.ToString());
    }

    // HttpMessageInvoker keeps its handler private; no InternalsVisibleTo reaches the BCL.
    private static HttpMessageHandler HandlerOf(HttpClient client) =>
        (HttpMessageHandler)typeof(HttpMessageInvoker)
            .GetField("_handler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(client)!;

    [Fact]
    public void Client_IsBuiltOnceFromTheOptions()
    {
        using var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance);

        Assert.Same(service.Client, service.Client);
    }

    [Fact]
    public void Client_ALoopbackClient_TakesItsCallbackFromTheServersHttpAddress()
    {
        // No request is needed, so after a restart the client_id is the one the stored sessions
        // were issued to.
        var server = new FakeServer("https://localhost:7203", "http://localhost:5203");
        using var service = new AtProtoOAuthService(
            new AtProtoOAuthServerOptions { Scopes = "atproto" }, NullLoggerFactory.Instance, server: server);

        Assert.Equal(
            "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%3A5203%2Fatproto%2Fcallback&scope=atproto",
            service.Client.ClientId);
    }

    [Fact]
    public void Client_ALoopbackClientWithABaseUrl_TakesItsCallbackFromIt()
    {
        using var service = new AtProtoOAuthService(
            new AtProtoOAuthServerOptions { Scopes = "atproto", BaseUrl = "http://127.0.0.1:8080/" }, NullLoggerFactory.Instance);

        Assert.Equal(
            "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%3A8080%2Fatproto%2Fcallback&scope=atproto",
            service.Client.ClientId);
    }

    [Fact]
    public void Client_ALoopbackClientWithoutAnHttpAddress_FailsUntilOneIsBound()
    {
        var server = new FakeServer("https://localhost:7203");
        using var service = new AtProtoOAuthService(new AtProtoOAuthServerOptions(), NullLoggerFactory.Instance, server: server);

        Assert.Throws<InvalidOperationException>(() => service.Client);

        // The failure is not remembered: once the server listens on plain HTTP, it works.
        server.Addresses.Add("http://127.0.0.1:5000");
        Assert.NotNull(service.Client);
    }

    [Fact]
    public void Client_AConfidentialClient_CarriesItsKeys()
    {
        using var key = OAuthClientKey.Generate("key-1");
        var options = CreateOptions();
        options.ClientMetadata!.TokenEndpointAuthMethod = "private_key_jwt";
        options.ClientMetadata.TokenEndpointAuthSigningAlg = "ES256";
        options.ClientMetadata.Jwks = OAuthClientKey.CreateKeySet([key]);
        options.ClientKeys.Add(key);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);

        Assert.Equal(options.ClientMetadata.ClientId, service.Client.ClientId);
    }

    [Fact]
    public void Client_ClientKeysWithoutClientMetadata_AreRefused()
    {
        // WithOAuth refuses this at startup; a loopback client is public, so its keys are refused too.
        using var key = OAuthClientKey.Generate("key-1");
        var options = new AtProtoOAuthServerOptions { BaseUrl = "http://127.0.0.1:8080" };
        options.ClientKeys.Add(key);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);

        Assert.Throws<ArgumentException>(() => service.Client);
    }

    [Fact]
    public void Client_ClientKeysOfAPublicClient_AreRefused()
    {
        using var key = OAuthClientKey.Generate("key-1");
        var options = CreateOptions();
        options.ClientKeys.Add(key);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);

        Assert.Throws<ArgumentException>(() => service.Client);
    }

    [Theory]
    [InlineData("http://localhost:5000", "http://127.0.0.1:5000")]
    [InlineData("http://127.0.0.1:5000", "http://127.0.0.1:5000")]
    [InlineData("http://[::]:5000", "http://127.0.0.1:5000")]
    [InlineData("http://0.0.0.0:5000", "http://127.0.0.1:5000")]
    [InlineData("http://[::1]:5000", "http://[::1]:5000")]
    [InlineData("HTTP://LOCALHOST:5000/", "http://127.0.0.1:5000")]
    [InlineData("https://localhost:5001", null)]
    [InlineData("http://192.0.2.1:5000", null)]
    [InlineData("http://app.example.com:80", null)]
    [InlineData("http://localhost", null)]
    public void TryGetLoopbackHttpOrigin_MapsServerAddresses(string address, string? expected)
    {
        Assert.Equal(expected, AtProtoOAuthService.TryGetLoopbackHttpOrigin(address));
    }

    /// <summary>A server that only reports the addresses it listens on.</summary>
    private sealed class FakeServer : Microsoft.AspNetCore.Hosting.Server.IServer,
        Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature
    {
        public FakeServer(params string[] addresses)
        {
            foreach (var address in addresses)
                Addresses.Add(address);
            Features.Set<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>(this);
        }

        public Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();

        public ICollection<string> Addresses { get; } = new List<string>();

        public bool PreferHostingUrls { get; set; }

        public Task StartAsync<TContext>(Microsoft.AspNetCore.Hosting.Server.IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void RegisteredIdentityResolver_FlowsToDiscovery()
    {
        var resolver = Substitute.For<IIdentityResolver>();

        using var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance, identityResolver: resolver);
        var client = service.Client;
        Assert.NotNull(client);

        Assert.Same(resolver, client.Discovery.IdentityResolver);
    }

    [Fact]
    public void WithoutAnIdentityResolver_DiscoveryCreatesItsOwn()
    {
        using var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance);
        var client = service.Client;
        Assert.NotNull(client);

        Assert.IsType<IdentityResolver>(client.Discovery.IdentityResolver);
    }

    /// <summary>
    /// Explicit client metadata is what lets <see cref="AtProtoOAuthService.Client"/>
    /// build the OAuth client without an <c>HttpContext</c>.
    /// </summary>
    private static AtProtoOAuthServerOptions CreateOptions() => new()
    {
        ClientMetadata = new OAuthClientMetadata
        {
            ClientId = "https://app.example/client-metadata.json",
            RedirectUris = ["https://app.example/atproto/callback"],
        },
    };
}
