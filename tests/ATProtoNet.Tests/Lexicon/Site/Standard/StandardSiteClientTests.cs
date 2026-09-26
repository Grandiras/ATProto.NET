using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Site.Standard;
using ATProtoNet.Lexicon.Site.Standard.Document;
using ATProtoNet.Lexicon.Site.Standard.Graph;
using ATProtoNet.Lexicon.Site.Standard.Publication;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
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
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _repo = new RepoClient(_xrpc);
        _site = new StandardSiteClient(_repo);
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
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.publication/abc", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" });
        };

        var record = new PublicationRecord
        {
            Url = "https://myblog.example.com",
            Name = "My Blog"
        };

        var result = await _site.CreatePublicationAsync(Did.Parse("did:plc:test"), record);

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
                cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                value = new
                {
                    url = "https://myblog.example.com",
                    name = "My Blog",
                    description = "A test blog"
                }
            });
        };

        var result = await _site.GetPublicationAsync(Did.Parse("did:plc:test"), RecordKey.Parse("abc"));

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
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.publication/abc", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" });
        };

        var record = new PublicationRecord
        {
            Url = "https://myblog.example.com",
            Name = "Updated Blog"
        };

        await _site.PutPublicationAsync(Did.Parse("did:plc:test"), RecordKey.Parse("abc"), record);

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
            return JsonResponse(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } });
        };

        await _site.DeletePublicationAsync(Did.Parse("did:plc:test"), RecordKey.Parse("abc"));

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

        await _site.ListPublicationsAsync(Did.Parse("did:plc:test"), limit: 10);

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
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.document/doc1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" });
        };

        var record = new DocumentRecord
        {
            Site = "at://did:plc:test/site.standard.publication/abc",
            Title = "My First Post",
            PublishedAt = AtDatetime.Parse("2024-01-20T14:30:00.000Z"),
            Path = "/blog/my-first-post",
            Tags = ["tutorial", "atproto"]
        };

        var result = await _site.CreateDocumentAsync(Did.Parse("did:plc:test"), record);

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
                cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
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

        var result = await _site.GetDocumentAsync(Did.Parse("did:plc:test"), RecordKey.Parse("doc1"));

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
            return JsonResponse(new { uri = "at://did:plc:test/site.standard.document/doc1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" });
        };

        var record = new DocumentRecord
        {
            Site = "https://myblog.example.com",
            Title = "Updated Post",
            PublishedAt = AtDatetime.Parse("2024-01-20T14:30:00.000Z"),
            UpdatedAt = AtDatetime.Parse("2024-02-01T10:00:00.000Z")
        };

        await _site.PutDocumentAsync(Did.Parse("did:plc:test"), RecordKey.Parse("doc1"), record);

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
            return JsonResponse(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } });
        };

        await _site.DeleteDocumentAsync(Did.Parse("did:plc:test"), RecordKey.Parse("doc1"));

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

        await _site.ListDocumentsAsync(Did.Parse("did:plc:test"));

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
            return JsonResponse(new { uri = "at://did:plc:sub/site.standard.graph.subscription/s1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" });
        };

        var record = new SubscriptionRecord
        {
            Publication = AtUri.Parse("at://did:plc:author/site.standard.publication/abc")
        };

        var result = await _site.CreateSubscriptionAsync(Did.Parse("did:plc:sub"), record);

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
                cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                value = new
                {
                    publication = "at://did:plc:author/site.standard.publication/abc"
                }
            });
        };

        var result = await _site.GetSubscriptionAsync(Did.Parse("did:plc:sub"), RecordKey.Parse("s1"));

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
            return JsonResponse(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } });
        };

        await _site.DeleteSubscriptionAsync(Did.Parse("did:plc:sub"), RecordKey.Parse("s1"));

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

        await _site.ListSubscriptionsAsync(Did.Parse("did:plc:sub"));

        Assert.Contains("collection=site.standard.graph.subscription", capturedUrl);
    }

    // ──────────────────────────────────────────────────────────
    //  Recommendations
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecommendation_WritesTheRecordToItsCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { uri = "at://did:plc:fan/site.standard.graph.recommend/r1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" });
        };

        var result = await _site.CreateRecommendationAsync(Did.Parse("did:plc:fan"), new RecommendRecord
        {
            Document = AtUri.Parse("at://did:plc:author/site.standard.document/doc1"),
            CreatedAt = AtDatetime.Parse("2026-09-25T10:00:00.000Z"),
        });

        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("site.standard.graph.recommend", body.RootElement.GetProperty("collection").GetString());
        var record = body.RootElement.GetProperty("record");
        Assert.Equal("site.standard.graph.recommend", record.GetProperty("$type").GetString());
        Assert.Equal("at://did:plc:author/site.standard.document/doc1", record.GetProperty("document").GetString());
        Assert.Equal("2026-09-25T10:00:00.000Z", record.GetProperty("createdAt").GetString());
        Assert.Equal("at://did:plc:fan/site.standard.graph.recommend/r1", result.Uri);
    }

    [Fact]
    public async Task GetRecommendation_ByAtUri_ReadsTheTypedRecord()
    {
        string? capturedQuery = null;
        _handler.ResponseFactory = request =>
        {
            capturedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            return JsonResponse(new
            {
                uri = "at://did:plc:fan/site.standard.graph.recommend/r1",
                cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                value = new
                {
                    document = "at://did:plc:author/site.standard.document/doc1",
                    createdAt = "2026-09-25T10:00:00.000Z",
                },
            });
        };

        var result = await _site.GetRecommendationAsync(AtUri.Parse("at://did:plc:fan/site.standard.graph.recommend/r1"));

        Assert.Equal("?repo=did:plc:fan&collection=site.standard.graph.recommend&rkey=r1", capturedQuery);
        Assert.Equal(AtUri.Parse("at://did:plc:author/site.standard.document/doc1"), result.Value.Document);
        Assert.Equal("2026-09-25T10:00:00.000Z", result.Value.CreatedAt.ToString());
    }

    [Fact]
    public async Task GetRecommendation_UriOfAnotherCollection_ThrowsBeforeSending()
    {
        var sent = false;
        _handler.ResponseFactory = _ =>
        {
            sent = true;
            return JsonResponse(new { });
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _site.GetRecommendationAsync(AtUri.Parse("at://did:plc:fan/site.standard.graph.subscription/r1")));
        Assert.False(sent);
    }

    [Fact]
    public async Task DeleteRecommendation_DeletesFromItsCollection()
    {
        string? capturedBody = null;
        _handler.ResponseFactory = request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().Result;
            return JsonResponse(new { commit = new { cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", rev = "3jzfcijpj2z2a" } });
        };

        await _site.DeleteRecommendationAsync(Did.Parse("did:plc:fan"), RecordKey.Parse("r1"));

        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("site.standard.graph.recommend", body.RootElement.GetProperty("collection").GetString());
        Assert.Equal("r1", body.RootElement.GetProperty("rkey").GetString());
    }

    [Fact]
    public async Task EnumerateRecommendationsAsync_ReturnsTypedRecordsAcrossPages()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return JsonResponse(new
            {
                cursor = queries.Count == 1 ? "page-2" : null,
                records = new[]
                {
                    new
                    {
                        uri = $"at://did:plc:fan/site.standard.graph.recommend/r{queries.Count}",
                        cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                        value = new
                        {
                            document = $"at://did:plc:author/site.standard.document/doc{queries.Count}",
                            createdAt = "2026-09-25T10:00:00.000Z",
                        },
                    },
                },
            });
        };

        var documents = new List<string>();
        await foreach (var recommendation in _site.EnumerateRecommendationsAsync(Did.Parse("did:plc:fan"), pageSize: 1))
            documents.Add(recommendation.Value.Document.Value);

        Assert.Equal(
            ["at://did:plc:author/site.standard.document/doc1", "at://did:plc:author/site.standard.document/doc2"],
            documents);
        Assert.Contains("collection=site.standard.graph.recommend", queries[0]);
        Assert.Contains("cursor=page-2", queries[1]);
    }

    // ──────────────────────────────────────────────────────────
    //  Typed listings and AT URI overloads
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDocumentsAsync_ReturnsTypedRecords()
    {
        _handler.ResponseFactory = _ => JsonResponse(new
        {
            cursor = "next",
            records = new[]
            {
                new
                {
                    uri = "at://did:plc:test/site.standard.document/doc1",
                    cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                    value = new
                    {
                        site = "https://myblog.example.com",
                        title = "My First Post",
                        publishedAt = "2024-01-20T14:30:00Z",
                    },
                },
            },
        });

        var page = await _site.ListDocumentsAsync(Did.Parse("did:plc:test"));

        var doc = Assert.Single(page.Records);
        Assert.Equal(RecordKey.Parse("doc1"), doc.RecordKey);
        Assert.Equal("My First Post", doc.Value.Title);
        Assert.Equal("2024-01-20T14:30:00Z", doc.Value.PublishedAt.ToString());
        Assert.Equal("next", page.Cursor);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_RecordOfTheWrongShape_IsAResponseFormatError()
    {
        _handler.ResponseFactory = _ => JsonResponse(new
        {
            records = new[]
            {
                new
                {
                    uri = "at://did:plc:sub/site.standard.graph.subscription/s1",
                    cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                    value = new { publication = "https://not-an-at-uri.example.com" },
                },
            },
        });

        await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _site.ListSubscriptionsAsync(Did.Parse("did:plc:sub")));
    }

    [Fact]
    public async Task EnumeratePublicationsAsync_WalksPagesWithThePageSize()
    {
        var queries = new List<string>();
        _handler.ResponseFactory = request =>
        {
            queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            var rkey = $"p{queries.Count}";
            return JsonResponse(new
            {
                cursor = queries.Count == 1 ? "page-2" : null,
                records = new[]
                {
                    new
                    {
                        uri = $"at://did:plc:test/site.standard.publication/{rkey}",
                        cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
                        value = new { url = "https://myblog.example.com", name = rkey },
                    },
                },
            });
        };

        var names = new List<string>();
        await foreach (var publication in _site.EnumeratePublicationsAsync(Did.Parse("did:plc:test"), pageSize: 1))
            names.Add(publication.Value.Name);

        Assert.Equal(["p1", "p2"], names);
        Assert.Equal(2, queries.Count);
        Assert.Contains("limit=1", queries[0]);
        Assert.Contains("cursor=page-2", queries[1]);
    }

    [Fact]
    public async Task GetPublicationAsync_ByAtUri_QueriesItsParts()
    {
        string? capturedQuery = null;
        _handler.ResponseFactory = request =>
        {
            capturedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            return JsonResponse(new
            {
                uri = "at://did:plc:author/site.standard.publication/self",
                value = new { url = "https://myblog.example.com", name = "My Blog" },
            });
        };

        var subscription = new SubscriptionRecord
        {
            Publication = AtUri.Parse("at://did:plc:author/site.standard.publication/self"),
        };
        var publication = await _site.GetPublicationAsync(subscription.Publication);

        Assert.Equal("?repo=did:plc:author&collection=site.standard.publication&rkey=self", capturedQuery);
        Assert.Equal("My Blog", publication.Value.Name);
    }

    [Theory]
    [InlineData("at://did:plc:author/site.standard.document/self")]
    [InlineData("at://did:plc:author/site.standard.publication")]
    public async Task GetPublicationAsync_UriThatNamesNoPublication_ThrowsBeforeSending(string uri)
    {
        var sent = false;
        _handler.ResponseFactory = _ =>
        {
            sent = true;
            return JsonResponse(new { });
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _site.GetPublicationAsync(AtUri.Parse(uri)));
        Assert.False(sent);
    }

    [Fact]
    public void AtProtoClient_Site_IsAvailable()
    {
        using var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://pds.example.com" });

        Assert.NotNull(client.Site);
    }

    public void Dispose()
    {
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
