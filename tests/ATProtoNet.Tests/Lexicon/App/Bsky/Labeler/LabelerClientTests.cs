using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.App.Bsky.Labeler;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Labeler;

public class LabelerClientTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly LabelerClient _labeler;

    public LabelerClientTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _labeler = new LabelerClient(_xrpc, NullLogger.Instance);
    }

    [Fact]
    public async Task GetServices_SendsCorrectRequest()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { views = Array.Empty<object>() });
        };

        var result = await _labeler.GetServicesAsync(
            ["did:plc:labeler1", "did:plc:labeler2"],
            detailed: true);

        Assert.Contains("/xrpc/app.bsky.labeler.getServices", capturedUrl);
        Assert.Contains("did%3aplc%3alabeler1", capturedUrl!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("detailed=true", capturedUrl!);
        Assert.NotNull(result);
        Assert.Empty(result.Views);
    }

    [Fact]
    public async Task GetServices_WithoutDetailed_OmitsParam()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { views = Array.Empty<object>() });
        };

        await _labeler.GetServicesAsync(["did:plc:labeler1"]);

        Assert.DoesNotContain("detailed", capturedUrl!);
    }

    public void Dispose()
    {
        _xrpc.Dispose();
        _httpClient.Dispose();
    }

    private static HttpResponseMessage JsonResponse(object body) => new()
    {
        Content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8,
            "application/json")
    };

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(ResponseFactory(request));
    }
}
