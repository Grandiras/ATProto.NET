using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Site.Standard;
using ATProtoNet.Lexicon.Site.Standard.Document;
using ATProtoNet.Lexicon.Site.Standard.Graph;
using ATProtoNet.Lexicon.Site.Standard.Publication;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Site.Standard;

public class StandardSiteClientTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new();

    private StandardSiteClient Site => _fixture.Client.Site;

    private HttpStub.RecordedRequest Last => _fixture.Last;

    public StandardSiteClientTests() =>
        // Every StandardSiteClient call is generic repo CRUD (createRecord, getRecord, ...)
        // across different collections, so one fallback responder covers the whole file.
        _fixture.Fallback("{}");

    public void Dispose() => _fixture.Dispose();

    // ──────────────────────────────────────────────────────────
    //  Publication CRUD
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePublication_SendsCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { uri = "at://did:plc:test/site.standard.publication/abc", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" }));

        var record = new PublicationRecord
        {
            Url = "https://myblog.example.com",
            Name = "My Blog"
        };

        var result = await Site.CreatePublicationAsync(Did.Parse("did:plc:test"), record);

        Assert.Contains("site.standard.publication", Last.BodyText);
        Assert.Equal("at://did:plc:test/site.standard.publication/abc", result.Uri);
    }

    [Fact]
    public async Task GetPublication_QueriesCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new
        {
            uri = "at://did:plc:test/site.standard.publication/abc",
            cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
            value = new
            {
                url = "https://myblog.example.com",
                name = "My Blog",
                description = "A test blog"
            }
        }));

        var result = await Site.GetPublicationAsync(Did.Parse("did:plc:test"), RecordKey.Parse("abc"));

        Assert.Contains("collection=site.standard.publication", Last.Query);
        Assert.Equal("My Blog", result.Value.Name);
        Assert.Equal("https://myblog.example.com", result.Value.Url);
    }

    [Fact]
    public async Task PutPublication_SendsCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { uri = "at://did:plc:test/site.standard.publication/abc", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" }));

        var record = new PublicationRecord
        {
            Url = "https://myblog.example.com",
            Name = "Updated Blog"
        };

        await Site.PutPublicationAsync(Did.Parse("did:plc:test"), RecordKey.Parse("abc"), record);

        Assert.Contains("site.standard.publication", Last.BodyText);
        Assert.Contains("Updated Blog", Last.BodyText);
    }

    // DeletePublicationAsync is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task ListPublications_QueriesCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { records = Array.Empty<object>() }));

        await Site.ListPublicationsAsync(Did.Parse("did:plc:test"), limit: 10);

        Assert.Contains("collection=site.standard.publication", Last.Query);
        Assert.Contains("limit=10", Last.Query);
    }

    // ──────────────────────────────────────────────────────────
    //  Document CRUD
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocument_SendsCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { uri = "at://did:plc:test/site.standard.document/doc1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" }));

        var record = new DocumentRecord
        {
            Site = "at://did:plc:test/site.standard.publication/abc",
            Title = "My First Post",
            PublishedAt = AtDatetime.Parse("2024-01-20T14:30:00.000Z"),
            Path = "/blog/my-first-post",
            Tags = ["tutorial", "atproto"]
        };

        var result = await Site.CreateDocumentAsync(Did.Parse("did:plc:test"), record);

        Assert.Contains("site.standard.document", Last.BodyText);
        Assert.Equal("at://did:plc:test/site.standard.document/doc1", result.Uri);
    }

    [Fact]
    public async Task GetDocument_QueriesCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new
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
        }));

        var result = await Site.GetDocumentAsync(Did.Parse("did:plc:test"), RecordKey.Parse("doc1"));

        Assert.Contains("collection=site.standard.document", Last.Query);
        Assert.Equal("My First Post", result.Value.Title);
        Assert.Equal("/blog/my-first-post", result.Value.Path);
        Assert.Equal(2, result.Value.Tags!.Count);
    }

    [Fact]
    public async Task PutDocument_SendsCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { uri = "at://did:plc:test/site.standard.document/doc1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" }));

        var record = new DocumentRecord
        {
            Site = "https://myblog.example.com",
            Title = "Updated Post",
            PublishedAt = AtDatetime.Parse("2024-01-20T14:30:00.000Z"),
            UpdatedAt = AtDatetime.Parse("2024-02-01T10:00:00.000Z")
        };

        await Site.PutDocumentAsync(Did.Parse("did:plc:test"), RecordKey.Parse("doc1"), record);

        Assert.Contains("site.standard.document", Last.BodyText);
        Assert.Contains("Updated Post", Last.BodyText);
    }

    // DeleteDocumentAsync is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task ListDocuments_QueriesCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { records = Array.Empty<object>() }));

        await Site.ListDocumentsAsync(Did.Parse("did:plc:test"));

        Assert.Contains("collection=site.standard.document", Last.Query);
    }

    // ──────────────────────────────────────────────────────────
    //  Subscription CRUD
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSubscription_SendsCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { uri = "at://did:plc:sub/site.standard.graph.subscription/s1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" }));

        var record = new SubscriptionRecord
        {
            Publication = AtUri.Parse("at://did:plc:author/site.standard.publication/abc")
        };

        var result = await Site.CreateSubscriptionAsync(Did.Parse("did:plc:sub"), record);

        Assert.Contains("site.standard.graph.subscription", Last.BodyText);
        Assert.Equal("at://did:plc:sub/site.standard.graph.subscription/s1", result.Uri);
    }

    [Fact]
    public async Task GetSubscription_QueriesCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new
        {
            uri = "at://did:plc:sub/site.standard.graph.subscription/s1",
            cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
            value = new
            {
                publication = "at://did:plc:author/site.standard.publication/abc"
            }
        }));

        var result = await Site.GetSubscriptionAsync(Did.Parse("did:plc:sub"), RecordKey.Parse("s1"));

        Assert.Contains("collection=site.standard.graph.subscription", Last.Query);
        Assert.Equal("at://did:plc:author/site.standard.publication/abc", result.Value.Publication);
    }

    // DeleteSubscriptionAsync is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task ListSubscriptions_QueriesCorrectCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { records = Array.Empty<object>() }));

        await Site.ListSubscriptionsAsync(Did.Parse("did:plc:sub"));

        Assert.Contains("collection=site.standard.graph.subscription", Last.Query);
    }

    // ──────────────────────────────────────────────────────────
    //  Recommendations
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecommendation_WritesTheRecordToItsCollection()
    {
        _fixture.Fallback(HttpStub.Json(new { uri = "at://did:plc:fan/site.standard.graph.recommend/r1", cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm" }));

        var result = await Site.CreateRecommendationAsync(Did.Parse("did:plc:fan"), new RecommendRecord
        {
            Document = AtUri.Parse("at://did:plc:author/site.standard.document/doc1"),
            CreatedAt = AtDatetime.Parse("2026-09-25T10:00:00.000Z"),
        });

        var body = Last.JsonBody;
        Assert.Equal("site.standard.graph.recommend", body.GetProperty("collection").GetString());
        var record = body.GetProperty("record");
        Assert.Equal("site.standard.graph.recommend", record.GetProperty("$type").GetString());
        Assert.Equal("at://did:plc:author/site.standard.document/doc1", record.GetProperty("document").GetString());
        Assert.Equal("2026-09-25T10:00:00.000Z", record.GetProperty("createdAt").GetString());
        Assert.Equal("at://did:plc:fan/site.standard.graph.recommend/r1", result.Uri);
    }

    [Fact]
    public async Task GetRecommendation_ByAtUri_ReadsTheTypedRecord()
    {
        _fixture.Fallback(HttpStub.Json(new
        {
            uri = "at://did:plc:fan/site.standard.graph.recommend/r1",
            cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
            value = new
            {
                document = "at://did:plc:author/site.standard.document/doc1",
                createdAt = "2026-09-25T10:00:00.000Z",
            },
        }));

        var result = await Site.GetRecommendationAsync(AtUri.Parse("at://did:plc:fan/site.standard.graph.recommend/r1"));

        Assert.Equal("repo=did:plc:fan&collection=site.standard.graph.recommend&rkey=r1", Uri.UnescapeDataString(Last.Query));
        Assert.Equal(AtUri.Parse("at://did:plc:author/site.standard.document/doc1"), result.Value.Document);
        Assert.Equal("2026-09-25T10:00:00.000Z", result.Value.CreatedAt.ToString());
    }

    [Fact]
    public async Task GetRecommendation_UriOfAnotherCollection_ThrowsBeforeSending()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Site.GetRecommendationAsync(AtUri.Parse("at://did:plc:fan/site.standard.graph.subscription/r1")));
        Assert.Empty(_fixture.Requests);
    }

    // DeleteRecommendationAsync is covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    // ──────────────────────────────────────────────────────────
    //  Typed listings and AT URI overloads
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDocumentsAsync_ReturnsTypedRecords()
    {
        _fixture.Fallback(HttpStub.Json(new
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
        }));

        var page = await Site.ListDocumentsAsync(Did.Parse("did:plc:test"));

        var doc = Assert.Single(page.Records);
        Assert.Equal(RecordKey.Parse("doc1"), doc.RecordKey);
        Assert.Equal("My First Post", doc.Value.Title);
        Assert.Equal("2024-01-20T14:30:00Z", doc.Value.PublishedAt.ToString());
        Assert.Equal("next", page.Cursor);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_RecordOfTheWrongShape_IsAResponseFormatError()
    {
        _fixture.Fallback(HttpStub.Json(new
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
        }));

        await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => Site.ListSubscriptionsAsync(Did.Parse("did:plc:sub")));
    }

    [Fact]
    public async Task GetPublicationAsync_ByAtUri_QueriesItsParts()
    {
        _fixture.Fallback(HttpStub.Json(new
        {
            uri = "at://did:plc:author/site.standard.publication/self",
            value = new { url = "https://myblog.example.com", name = "My Blog" },
        }));

        var subscription = new SubscriptionRecord
        {
            Publication = AtUri.Parse("at://did:plc:author/site.standard.publication/self"),
        };
        var publication = await Site.GetPublicationAsync(subscription.Publication);

        Assert.Equal("repo=did:plc:author&collection=site.standard.publication&rkey=self", Uri.UnescapeDataString(Last.Query));
        Assert.Equal("My Blog", publication.Value.Name);
    }

    [Theory]
    [InlineData("at://did:plc:author/site.standard.document/self")]
    [InlineData("at://did:plc:author/site.standard.publication")]
    public async Task GetPublicationAsync_UriThatNamesNoPublication_ThrowsBeforeSending(string uri)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Site.GetPublicationAsync(AtUri.Parse(uri)));
        Assert.Empty(_fixture.Requests);
    }
}
