using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Site.Standard;
using ATProtoNet.Lexicon.Site.Standard.Document;
using ATProtoNet.Lexicon.Site.Standard.Graph;
using ATProtoNet.Lexicon.Site.Standard.Publication;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Lexicon.Site.Standard;

public class StandardSiteClientTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly RepoClient _repo;
    private readonly StandardSiteClient _site;

    public StandardSiteClientTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _repo = new RepoClient(_xrpc);
        _site = new StandardSiteClient(_xrpc, _repo);
    }

    // ──────────────────────────────────────────────────────────
    //  Publication CRUD
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePublication_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.publication/abc", cid = "bafytest" });
        };

        var record = new PublicationRecord
        {
            Url = "https://myblog.example.com",
            Name = "My Blog"
        };

        var result = await _site.CreatePublicationAsync("did:plc:test", record);

        Assert.Contains("site.standard.publication", capturedBody);
        Assert.Equal("at://did:plc:test/site.standard.publication/abc", result.Uri);
    }

    [Fact]
    public async Task GetPublication_QueriesCorrectCollection()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new
            {
                uri = "at://did:plc:test/site.standard.publication/abc",
                cid = "bafytest",
                value = new
                {
                    url = "https://myblog.example.com",
                    name = "My Blog",
                    description = "A test blog"
                }
            });
        };

        var result = await _site.GetPublicationAsync("did:plc:test", "abc");

        Assert.Contains("collection=site.standard.publication", capturedUrl);
        Assert.Equal("My Blog", result.Value.Name);
        Assert.Equal("https://myblog.example.com", result.Value.Url);
    }

    [Fact]
    public async Task PutPublication_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.publication/abc", cid = "bafytest" });
        };

        var record = new PublicationRecord
        {
            Url = "https://myblog.example.com",
            Name = "Updated Blog"
        };

        await _site.PutPublicationAsync("did:plc:test", "abc", record);

        Assert.Contains("site.standard.publication", capturedBody);
        Assert.Contains("Updated Blog", capturedBody);
    }

    [Fact]
    public async Task DeletePublication_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { commit = new { cid = "bafytest", rev = "rev1" } });
        };

        await _site.DeletePublicationAsync("did:plc:test", "abc");

        Assert.Contains("site.standard.publication", capturedBody);
    }

    [Fact]
    public async Task ListPublications_QueriesCorrectCollection()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { records = new object[0] });
        };

        await _site.ListPublicationsAsync("did:plc:test", limit: 10);

        Assert.Contains("collection=site.standard.publication", capturedUrl);
        Assert.Contains("limit=10", capturedUrl);
    }

    // ──────────────────────────────────────────────────────────
    //  Document CRUD
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocument_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.document/doc1", cid = "bafytest" });
        };

        var record = new DocumentRecord
        {
            Site = "at://did:plc:test/site.standard.publication/abc",
            Title = "My First Post",
            PublishedAt = "2024-01-20T14:30:00.000Z",
            Path = "/blog/my-first-post",
            Tags = ["tutorial", "atproto"]
        };

        var result = await _site.CreateDocumentAsync("did:plc:test", record);

        Assert.Contains("site.standard.document", capturedBody);
        Assert.Equal("at://did:plc:test/site.standard.document/doc1", result.Uri);
    }

    [Fact]
    public async Task GetDocument_QueriesCorrectCollection()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new
            {
                uri = "at://did:plc:test/site.standard.document/doc1",
                cid = "bafytest",
                value = new
                {
                    site = "at://did:plc:test/site.standard.publication/abc",
                    title = "My First Post",
                    publishedAt = "2024-01-20T14:30:00.000Z",
                    path = "/blog/my-first-post",
                    tags = new[] { "tutorial", "atproto" }
                }
            });
        };

        var result = await _site.GetDocumentAsync("did:plc:test", "doc1");

        Assert.Contains("collection=site.standard.document", capturedUrl);
        Assert.Equal("My First Post", result.Value.Title);
        Assert.Equal("/blog/my-first-post", result.Value.Path);
        Assert.Equal(2, result.Value.Tags!.Count);
    }

    [Fact]
    public async Task PutDocument_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.document/doc1", cid = "bafytest" });
        };

        var record = new DocumentRecord
        {
            Site = "https://myblog.example.com",
            Title = "Updated Post",
            PublishedAt = "2024-01-20T14:30:00.000Z",
            UpdatedAt = "2024-02-01T10:00:00.000Z"
        };

        await _site.PutDocumentAsync("did:plc:test", "doc1", record);

        Assert.Contains("site.standard.document", capturedBody);
        Assert.Contains("Updated Post", capturedBody);
    }

    [Fact]
    public async Task DeleteDocument_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { commit = new { cid = "bafytest", rev = "rev1" } });
        };

        await _site.DeleteDocumentAsync("did:plc:test", "doc1");

        Assert.Contains("site.standard.document", capturedBody);
    }

    [Fact]
    public async Task ListDocuments_QueriesCorrectCollection()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { records = new object[0] });
        };

        await _site.ListDocumentsAsync("did:plc:test");

        Assert.Contains("collection=site.standard.document", capturedUrl);
    }

    // ──────────────────────────────────────────────────────────
    //  Subscription CRUD
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSubscription_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { uri = "at://did:plc:sub/site.standard.graph.subscription/s1", cid = "bafytest" });
        };

        var record = new SubscriptionRecord
        {
            Publication = "at://did:plc:author/site.standard.publication/abc"
        };

        var result = await _site.CreateSubscriptionAsync("did:plc:sub", record);

        Assert.Contains("site.standard.graph.subscription", capturedBody);
        Assert.Equal("at://did:plc:sub/site.standard.graph.subscription/s1", result.Uri);
    }

    [Fact]
    public async Task GetSubscription_QueriesCorrectCollection()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new
            {
                uri = "at://did:plc:sub/site.standard.graph.subscription/s1",
                cid = "bafytest",
                value = new
                {
                    publication = "at://did:plc:author/site.standard.publication/abc"
                }
            });
        };

        var result = await _site.GetSubscriptionAsync("did:plc:sub", "s1");

        Assert.Contains("collection=site.standard.graph.subscription", capturedUrl);
        Assert.Equal("at://did:plc:author/site.standard.publication/abc", result.Value.Publication);
    }

    [Fact]
    public async Task DeleteSubscription_SendsCorrectCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { commit = new { cid = "bafytest", rev = "rev1" } });
        };

        await _site.DeleteSubscriptionAsync("did:plc:sub", "s1");

        Assert.Contains("site.standard.graph.subscription", capturedBody);
    }

    [Fact]
    public async Task ListSubscriptions_QueriesCorrectCollection()
    {
        string? capturedUrl = null;
        _handler.ResponseFactory = request =>
        {
            capturedUrl = request.RequestUri?.PathAndQuery;
            return JsonResponse(new { records = new object[0] });
        };

        await _site.ListSubscriptionsAsync("did:plc:sub");

        Assert.Contains("collection=site.standard.graph.subscription", capturedUrl);
    }

    public void Dispose()
    {
        _xrpc.Dispose();
        _httpClient.Dispose();
    }

    private static HttpResponseMessage JsonResponse(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return new HttpResponseMessage
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(ResponseFactory(request));
    }
}
