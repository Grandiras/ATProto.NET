using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientRateLimitTests : IDisposable
{
    private const string RateLimitedBody = """{"error":"RateLimitExceeded","message":"Too many requests"}""";

    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientRateLimitTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler);
        _xrpc = CreateClient(new XrpcRateLimitOptions());
    }

    private XrpcClient CreateClient(XrpcRateLimitOptions rateLimit) =>
        new(_httpClient, new Uri("https://pds.example.com/"), NullLogger.Instance) { RateLimit = rateLimit };

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
                return RateLimited(retryAfter: "0");

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
    public async Task QueryAsync_ThrowsRateLimitExceptionAfterMaxRetries()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            return RateLimited(retryAfter: "0");
        };

        var xrpc = CreateClient(new XrpcRateLimitOptions { MaxRetries = 2 });

        var ex = await Assert.ThrowsAsync<XrpcRateLimitException>(
            () => xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer"));

        Assert.Equal(3, callCount);
        Assert.Equal(TimeSpan.Zero, ex.RetryAfter);
        Assert.Equal("com.atproto.server.describeServer", ex.Nsid);
    }

    [Fact]
    public async Task QueryAsync_NoRetryWhenMaxRetriesIsZero()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            return RateLimited(retryAfter: "0");
        };

        var xrpc = CreateClient(new XrpcRateLimitOptions { MaxRetries = 0 });

        await Assert.ThrowsAsync<XrpcRateLimitException>(
            () => xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer"));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task QueryAsync_WhenTheWaitExceedsMaxDelay_ThrowsAtOnceWithRetryAfter()
    {
        // A daily window: before, the client slept on this for the rest of the day.
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            return RateLimited(retryAfter: "86400");
        };

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<XrpcRateLimitException>(
            () => _xrpc.QueryAsync<JsonElement>("com.atproto.server.createSession"));

        Assert.Equal(1, callCount);
        Assert.Equal(TimeSpan.FromDays(1), ex.RetryAfter);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task QueryAsync_WhenRateLimitResetIsHoursAway_ThrowsAtOnce()
    {
        var reset = DateTimeOffset.UtcNow.AddHours(3);
        _handler.ResponseFactory = _ =>
        {
            var response = RateLimited(retryAfter: null);
            response.Headers.TryAddWithoutValidation("RateLimit-Limit", "300");
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("RateLimit-Reset", reset.ToUnixTimeSeconds().ToString());
            return response;
        };

        var ex = await Assert.ThrowsAsync<XrpcRateLimitException>(
            () => _xrpc.QueryAsync<JsonElement>("com.atproto.server.createSession"));

        Assert.InRange(ex.RetryAfter!.Value, TimeSpan.FromHours(2.9), TimeSpan.FromHours(3));
        Assert.Equal(300, ex.RateLimit?.Limit);
        Assert.Equal(0, ex.RateLimit?.Remaining);
    }

    [Fact]
    public async Task QueryAsync_HonoursAnHttpDateRetryAfter()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            return callCount == 1
                // A date already reached: retry at once.
                ? RateLimited(retryAfter: DateTimeOffset.UtcNow.AddMinutes(-1).ToString("R"))
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };
        };

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task QueryAsync_WhenAnHttpDateRetryAfterIsBeyondMaxDelay_Throws()
    {
        _handler.ResponseFactory = _ => RateLimited(retryAfter: DateTimeOffset.UtcNow.AddHours(1).ToString("R"));

        var ex = await Assert.ThrowsAsync<XrpcRateLimitException>(
            () => _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer"));

        Assert.InRange(ex.RetryAfter!.Value, TimeSpan.FromMinutes(58), TimeSpan.FromHours(1));
    }

    [Fact]
    public void RateLimitOptions_Defaults()
    {
        var options = new XrpcRateLimitOptions();

        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxDelay);
    }

    [Fact]
    public void RateLimitOptions_RejectNegativeValues()
    {
        var options = new XrpcRateLimitOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxRetries = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxDelay = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public async Task AtProtoClient_AppliesItsRateLimitOptions()
    {
        int callCount = 0;
        _handler.ResponseFactory = _ =>
        {
            callCount++;
            return RateLimited(retryAfter: "0");
        };

        using var client = new AtProtoClient(
            new AtProtoClientOptions
            {
                AutoRefreshSession = false,
                RateLimit = new XrpcRateLimitOptions { MaxRetries = 1 },
            },
            _httpClient,
            null,
            null);

        await Assert.ThrowsAsync<XrpcRateLimitException>(() => client.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping")));
        Assert.Equal(2, callCount);
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
        _httpClient.Dispose();
        _handler.Dispose();
    }

    private static HttpResponseMessage RateLimited(string? retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(RateLimitedBody, System.Text.Encoding.UTF8, "application/json"),
        };
        if (retryAfter is not null)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
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
