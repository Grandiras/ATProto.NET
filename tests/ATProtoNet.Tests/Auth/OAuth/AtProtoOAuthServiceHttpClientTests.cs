using System.Net;
using System.Reflection;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Blazor.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Auth.OAuth;

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
        var client = service.TryGetClient();
        Assert.NotNull(client);

        await Assert.ThrowsAsync<OAuthException>(
            () => client.Discovery.ResolveHandleToDidAsync("example.com"));
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
        Assert.NotNull(service.TryGetClient());

        Assert.Equal(TimeSpan.FromSeconds(7), callerClient.Timeout);
    }

    [Fact]
    public void CreatedHttpClient_UsesConfiguredTimeout()
    {
        var options = CreateOptions();
        options.HttpClientTimeout = TimeSpan.FromSeconds(12);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);
        var client = service.TryGetClient();
        Assert.NotNull(client);

        var httpClient = (HttpClient)typeof(OAuthClient)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;

        Assert.Equal(TimeSpan.FromSeconds(12), httpClient.Timeout);
    }

    [Fact]
    public void HandleResolutionTimeout_FlowsToDiscovery()
    {
        var options = CreateOptions();
        options.HandleResolutionTimeout = TimeSpan.FromSeconds(3);

        using var service = new AtProtoOAuthService(options, NullLoggerFactory.Instance);
        var client = service.TryGetClient();
        Assert.NotNull(client);

        Assert.Equal(TimeSpan.FromSeconds(3), client.Discovery.HandleResolutionTimeout);
    }

    /// <summary>
    /// Explicit client metadata is what lets <see cref="AtProtoOAuthService.TryGetClient"/>
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
