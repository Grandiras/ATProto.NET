using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Signature;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// The typed tools.ozone surface: identifiers go out as their strings, come back parsed, and
/// every cursored listing has an enumerator on the shared paginator.
/// </summary>
public class TypedOzoneClientTests : IDisposable
{
    private const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string CidText = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private readonly ScriptedHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public TypedOzoneClientTests()
    {
        _httpClient = new HttpClient(_handler);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://ozone.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task QueryEventsAsync_TypedFilters_GoOutAsTheirStrings()
    {
        _handler.Pages.Enqueue("""{"events":[]}""");

        await _client.Ozone.Moderation.QueryEventsAsync(
            subject: "did:plc:abc",
            createdBy: Did.Parse(ModDid),
            createdAfter: AtDatetime.Parse("2024-01-01T00:00:00Z"),
            createdBefore: AtDatetime.FromDateTimeOffset(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            types: ["tools.ozone.moderation.defs#modEventTakedown"],
            limit: 5,
            cursor: "c");

        Assert.Equal(
            $"?subject=did:plc:abc&createdBy={ModDid}&createdAfter=2024-01-01T00:00:00Z" +
            "&createdBefore=2024-02-01T00:00:00.000Z&types=tools.ozone.moderation.defs#modEventTakedown&limit=5&cursor=c",
            Uri.UnescapeDataString(_handler.Requests.Single().Query));
    }

    [Fact]
    public async Task QueryEventsAsync_ParsesTheTypedEventView()
    {
        _handler.Pages.Enqueue($$"""
            {"events":[{
              "id":7,
              "event":{"$type":"tools.ozone.moderation.defs#modEventComment","comment":"hi"},
              "subject":{"$type":"com.atproto.repo.strongRef","uri":"at://{{ModDid}}/app.bsky.feed.post/3k2la","cid":"{{CidText}}"},
              "subjectBlobCids":["{{CidText}}"],
              "createdBy":"{{ModDid}}",
              "createdAt":"2024-01-01T00:00:00Z",
              "creatorHandle":"mod.example.com"
            }]}
            """);

        var page = await _client.Ozone.Moderation.QueryEventsAsync();

        var view = Assert.Single(page.Events);
        var subject = Assert.IsType<RecordSubject>(view.Subject);
        Assert.Equal(RecordKey.Parse("3k2la"), subject.Uri.RecordKey);
        Assert.Equal(Cid.Parse(CidText), Assert.Single(view.SubjectBlobCids!));
        Assert.Equal(Did.Parse(ModDid), view.CreatedBy);
        Assert.Equal("2024-01-01T00:00:00Z", view.CreatedAt.ToString());
        Assert.Equal(Handle.Parse("mod.example.com"), view.CreatorHandle);
    }

    [Fact]
    public async Task GetRepoAsync_InvalidDidInTheResponse_IsAResponseFormatError()
    {
        _handler.Pages.Enqueue("""
            {"did":"not-a-did","handle":"user.example.com","relatedRecords":[],"indexedAt":"2024-01-01T00:00:00Z","moderation":{}}
            """);

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _client.Ozone.Moderation.GetRepoAsync(Did.Parse(ModDid)));

        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task GetRecordAsync_SendsTheTypedUriAndCid()
    {
        var uri = AtUri.Parse($"at://{ModDid}/app.bsky.feed.post/3k2la");
        _handler.Pages.Enqueue($$"""
            {"uri":"{{uri}}","cid":"{{CidText}}","value":{},"indexedAt":"2024-01-01T00:00:00Z",
             "moderation":{},"repo":{"did":"{{ModDid}}","handle":"mod.example.com","indexedAt":"2024-01-01T00:00:00Z","moderation":{} } }
            """);

        var record = await _client.Ozone.Moderation.GetRecordAsync(uri, Cid.Parse(CidText));

        Assert.Equal($"?uri={uri}&cid={CidText}", Uri.UnescapeDataString(_handler.Requests.Single().Query));
        Assert.Equal(uri, record.Uri);
        Assert.Equal(Handle.Parse("mod.example.com"), record.Repo.Handle);
    }

    [Fact]
    public async Task EmitEventAsync_WritesTypedIdentifiersAsStrings()
    {
        _handler.Pages.Enqueue($$"""
            {"id":1,"event":{"$type":"tools.ozone.moderation.defs#modEventAcknowledge"},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{ModDid}}"},
             "createdBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00.000Z"}
            """);

        await _client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
        {
            Event = new ModEventAcknowledge(),
            Subject = new RepoSubject { Did = Did.Parse(ModDid) },
            SubjectBlobCids = [Cid.Parse(CidText)],
            CreatedBy = Did.Parse(ModDid),
        });

        using var body = JsonDocument.Parse(_handler.Bodies.Single()!);
        Assert.Equal(ModDid, body.RootElement.GetProperty("createdBy").GetString());
        Assert.Equal(CidText, body.RootElement.GetProperty("subjectBlobCids")[0].GetString());
        Assert.Equal(ModDid, body.RootElement.GetProperty("subject").GetProperty("did").GetString());
    }

    [Fact]
    public async Task ListMembersAsync_AdminTokenAsLastUpdatedBy_ReadsAsIs()
    {
        _handler.Pages.Enqueue($$"""
            {"members":[{"did":"{{ModDid}}","role":"tools.ozone.team.defs#roleAdmin","lastUpdatedBy":"admin_token"}]}
            """);

        var page = await _client.Ozone.Team.ListMembersAsync();

        Assert.Equal("admin_token", Assert.Single(page.Members).LastUpdatedBy);
    }

    [Fact]
    public async Task FindRelatedAccountsAsync_LimitThenCursor_SendsBoth()
    {
        _handler.Pages.Enqueue("""{"accounts":[]}""");

        await _client.Ozone.Signature.FindRelatedAccountsAsync(Did.Parse(ModDid), 10, "c");

        var query = Uri.UnescapeDataString(_handler.Requests.Single().Query);
        Assert.Contains($"did={ModDid}", query);
        Assert.Contains("limit=10", query);
        Assert.Contains("cursor=c", query);
    }

    [Fact]
    public async Task SearchAccountsAsync_LimitThenCursor_SendsBoth()
    {
        _handler.Pages.Enqueue("""{"accounts":[]}""");

        await _client.Ozone.Signature.SearchAccountsAsync(
            [new SigDetail { Property = "ip", Value = "192.0.2.1" }], 10, "c");

        using var body = JsonDocument.Parse(_handler.Bodies.Single()!);
        Assert.Equal(10, body.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal("c", body.RootElement.GetProperty("cursor").GetString());
    }

    public static TheoryData<string> Enumerators =>
    [
        "events", "subjects", "repos", "sets", "values", "members",
    ];

    [Theory]
    [MemberData(nameof(Enumerators))]
    public async Task Enumerate_WalksPagesWithPageSizeAndStopsOnARepeatedCursor(string listing)
    {
        // Two pages, then the second cursor again: the paginator must stop instead of looping.
        var (nsid, pages, enumerate) = Listing(listing);
        foreach (var page in pages)
            _handler.Pages.Enqueue(page);

        var items = await enumerate(_client);

        Assert.Equal(3, items);
        Assert.Equal(3, _handler.Requests.Count);
        Assert.All(_handler.Requests, uri => Assert.Equal($"/xrpc/{nsid}", uri.AbsolutePath));
        Assert.All(_handler.Requests, uri => Assert.Contains("limit=2", uri.Query));
        Assert.DoesNotContain("cursor=", _handler.Requests[0].Query);
        Assert.Contains("cursor=c1", _handler.Requests[1].Query);
        Assert.Contains("cursor=c2", _handler.Requests[2].Query);
    }

    private static (string Nsid, string[] Pages, Func<AtProtoClient, Task<int>> Enumerate) Listing(string listing)
    {
        const string Set = """{"name":"s","setSize":1,"createdAt":"2024-01-01T00:00:00Z","updatedAt":"2024-01-01T00:00:00Z"}""";
        var member = $$"""{"did":"{{ModDid}}","role":"tools.ozone.team.defs#roleModerator"}""";
        var repo = $$$"""{"did":"{{{ModDid}}}","handle":"mod.example.com","relatedRecords":[],"indexedAt":"2024-01-01T00:00:00Z","moderation":{}}""";
        var eventView = $$"""
            {"id":1,"event":{"$type":"tools.ozone.moderation.defs#modEventAcknowledge"},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{ModDid}}"},
             "createdBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00Z"}
            """;
        var status = $$"""
            {"id":1,"subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{ModDid}}"},
             "updatedAt":"2024-01-01T00:00:00Z","createdAt":"2024-01-01T00:00:00Z",
             "reviewState":"tools.ozone.moderation.defs#reviewOpen"}
            """;

        string[] Pages(string list, string item, string extra = "") =>
        [
            $$"""{{{extra}}"cursor":"c1","{{list}}":[{{item}}]}""",
            $$"""{{{extra}}"cursor":"c2","{{list}}":[{{item}}]}""",
            $$"""{{{extra}}"cursor":"c2","{{list}}":[{{item}}]}""",
        ];

        static async Task<int> Count<T>(IAsyncEnumerable<T> items)
        {
            var count = 0;
            await foreach (var _ in items)
                count++;
            return count;
        }

        return listing switch
        {
            "events" => ("tools.ozone.moderation.queryEvents", Pages("events", eventView),
                c => Count(c.Ozone.Moderation.EnumerateEventsAsync(pageSize: 2))),
            "subjects" => ("tools.ozone.moderation.querySubjects", Pages("subjects", status),
                c => Count(c.Ozone.Moderation.EnumerateSubjectsAsync(pageSize: 2))),
            "repos" => ("tools.ozone.moderation.searchRepos", Pages("repos", repo),
                c => Count(c.Ozone.Moderation.EnumerateReposAsync("mod", pageSize: 2))),
            "sets" => ("tools.ozone.set.querySets", Pages("sets", Set),
                c => Count(c.Ozone.Set.EnumerateSetsAsync(pageSize: 2))),
            "values" => ("tools.ozone.set.getValues", Pages("values", "\"v\"", $"\"set\":{Set},"),
                c => Count(c.Ozone.Set.EnumerateValuesAsync("s", pageSize: 2))),
            "members" => ("tools.ozone.team.listMembers", Pages("members", member),
                c => Count(c.Ozone.Team.EnumerateMembersAsync(pageSize: 2))),
            _ => throw new ArgumentOutOfRangeException(nameof(listing)),
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Queue<string> Pages { get; } = new();

        public List<Uri> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Pages.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }
}
