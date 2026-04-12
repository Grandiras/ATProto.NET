using System.Net;
using System.Text.Json;
using ATProtoNet.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientRateLimitTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientRateLimitTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
    }

    [Fact]
    public async Task QueryAsync_ParsesRateLimitHeaders()
    {
        _handler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("RateLimit-Limit", "3000");
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "2999");
            response.Headers.TryAddWithoutValidation("RateLimit-Reset", "1700000000");
            return response;
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.NotNull(_xrpc.LatestRateLimitInfo);
        Assert.Equal(3000, _xrpc.LatestRateLimitInfo.Limit);
        Assert.Equal(2999, _xrpc.LatestRateLimitInfo.Remaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), _xrpc.LatestRateLimitInfo.Reset);
        Assert.False(_xrpc.LatestRateLimitInfo.IsExceeded);
    }

    [Fact]
    public async Task RateLimitInfo_IsExceeded_WhenRemainingIsZero()
    {
        _handler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("RateLimit-Limit", "3000");
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("RateLimit-Reset", "1700000000");
            return response;
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.NotNull(_xrpc.LatestRateLimitInfo);
        Assert.True(_xrpc.LatestRateLimitInfo.IsExceeded);
    }

    [Fact]
    public async Task QueryAsync_RetriesOn429WithRetryAfterHeader()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":\"RateLimitExceeded\",\"message\":\"Too many requests\"}", System.Text.Encoding.UTF8, "application/json"),
                };
                rateLimitResponse.Headers.TryAddWithoutValidation("Retry-After", "0");
                return rateLimitResponse;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"did\":\"did:plc:test\"}", System.Text.Encoding.UTF8, "application/json"),
            };
        };

        var result = await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Equal(2, callCount);
        Assert.Equal("did:plc:test", result.GetProperty("did").GetString());
    }

    [Fact]
    public async Task QueryAsync_ThrowsAfterMaxRetries()
    {
        _handler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"RateLimitExceeded\",\"message\":\"Too many requests\"}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return response;
        };

        _xrpc.MaxRateLimitRetries = 2;

        await Assert.ThrowsAsync<AtProtoHttpException>(
            () => _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer"));
    }

    [Fact]
    public async Task QueryAsync_NoRetryWhenMaxRetriesIsZero()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"RateLimitExceeded\",\"message\":\"Too many requests\"}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return response;
        };

        _xrpc.MaxRateLimitRetries = 0;

        await Assert.ThrowsAsync<AtProtoHttpException>(
            () => _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer"));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task LatestRateLimitInfo_IsNullWhenNoHeaders()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Null(_xrpc.LatestRateLimitInfo);
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
