using System.Net;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// Tests how <see cref="AtProtoOAuthService"/> sources its <see cref="HttpClient"/>:
/// a caller-supplied client is used as-is and left alive, an SDK-created one gets a
/// timeout well below the 100 s <see cref="HttpClient"/> default.
/// </summary>
public class AtProtoOAuthServiceHttpClientTests
{
    [Fact]
    public async Task SuppliedHttpClient_IsUsedForDiscoveryAndNotDisposedWithTheService()
    {
        var requests = new List<string>();
        using var handler = new RecordingHandler(requests);
        using var callerClient = new HttpClient(handler, disposeHandler: false);

        var options = CreateOptions();
        options.HttpClient = callerClient;

        var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);
        var client = service.Client;
        Assert.NotNull(client);

        // The OAuth metadata requests go through the caller's client; identity resolution has
        // its own, under the identity fetch policy.
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.Discovery.ResolveAuthorizationServerAsync("https://pds.example.com"));
        Assert.NotEmpty(requests);

        // The caller owns the client's lifetime — it must survive the service.
        service.Dispose();
        using var response = await callerClient.GetAsync("https://example.com/still-usable");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void SuppliedHttpClient_TimeoutIsLeftUntouched()
    {
        using var handler = new RecordingHandler([]);
        using var callerClient = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(7),
        };

        var options = CreateOptions();
        options.HttpClient = callerClient;
        options.HttpClientTimeout = TimeSpan.FromSeconds(30);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);
        Assert.NotNull(service.Client);

        Assert.Equal(TimeSpan.FromSeconds(7), callerClient.Timeout);
    }

    [Fact]
    public void CreatedHttpClient_UsesConfiguredTimeout()
    {
        var options = CreateOptions();
        options.HttpClientTimeout = TimeSpan.FromSeconds(12);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);

        Assert.Equal(TimeSpan.FromSeconds(12), service.Client.HttpClient.Timeout);
    }

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
        using var key = OAuthClientKey.Generate("key-1");
        var options = new AtProtoOAuthServerOptions { BaseUrl = "http://127.0.0.1:8080" };
        options.ClientKeys.Add(key);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);

        Assert.Throws<InvalidOperationException>(() => service.Client);
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

        using var service = new AtProtoOAuthService(CreateOptions(), NullLoggerFactory.Instance, resolver);
        var client = service.Client;
        Assert.NotNull(client);

        Assert.Same(resolver, client.Discovery.IdentityResolver);
    }

    [Fact]
    public void WithoutAnIdentityResolver_DiscoveryCreatesItsOwn()
    {
        var options = CreateOptions();
        options.HandleResolutionTimeout = TimeSpan.FromSeconds(3);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);
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

    private sealed class RecordingHandler(List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (requests)
                requests.Add(request.RequestUri!.ToString());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            });
        }
    }
}
