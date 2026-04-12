using System.Net;
using System.Text.Json;
using ATProtoNet.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientRepoRevTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientRepoRevTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
    }

    [Fact]
    public async Task QueryAsync_ExtractsRepoRevHeader()
    {
        _handler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"did\":\"did:plc:test\"}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Atproto-Repo-Rev", "3jzhpt2dsby2u");
            return response;
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task ProcedureAsync_ExtractsRepoRevHeader()
    {
        _handler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Atproto-Repo-Rev", "3jzhpt2dsby2x");
            return response;
        };

        await _xrpc.ProcedureAsync<object, JsonElement>("com.atproto.repo.createRecord", new { });

        Assert.Equal("3jzhpt2dsby2x", _xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task LatestRepoRev_UpdatesOnSubsequentRequests()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Atproto-Repo-Rev",
                callCount == 1 ? "3jzhpt2dsby2u" : "3jzhpt2dsby2z");
            return response;
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2z", _xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task LatestRepoRev_IsNullWhenNoHeader()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Null(_xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task LatestRepoRev_RetainsPreviousValueWhenHeaderMissing()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            if (callCount == 1)
                response.Headers.TryAddWithoutValidation("Atproto-Repo-Rev", "3jzhpt2dsby2u");
            return response;
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);
    }

    public void Dispose()
    {
        _xrpc.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ResponseFactory(request));
        }
    }
}
