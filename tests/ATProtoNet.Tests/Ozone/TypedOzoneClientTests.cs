using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// The typed tools.ozone surface: identifiers go out as their strings, come back parsed, and
/// every cursored listing has an enumerator on the shared paginator.
/// </summary>
public sealed class TypedOzoneClientTests : IDisposable
{
    private const string ModDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string CidText = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private readonly XrpcTestClient _fixture = new(instanceUrl: "https://ozone.example.com");

    private AtProtoClient Client => _fixture.Client;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task QueryEventsAsync_TypedFilters_GoOutAsTheirStrings()
    {
        _fixture.On("tools.ozone.moderation.queryEvents", """{"events":[]}""");

        await Client.Ozone.Moderation.QueryEventsAsync(
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
            Uri.UnescapeDataString(_fixture.To("tools.ozone.moderation.queryEvents").Single().Uri.Query));
    }

    [Fact]
    public async Task QueryEventsAsync_ParsesTheTypedEventView()
    {
        _fixture.On("tools.ozone.moderation.queryEvents", $$"""
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

        var page = await Client.Ozone.Moderation.QueryEventsAsync();

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
        _fixture.On("tools.ozone.moderation.getRepo", """
            {"did":"not-a-did","handle":"user.example.com","relatedRecords":[],"indexedAt":"2024-01-01T00:00:00Z","moderation":{}}
            """);

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => Client.Ozone.Moderation.GetRepoAsync(Did.Parse(ModDid)));

        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task GetRecordAsync_SendsTheTypedUriAndCid()
    {
        var uri = AtUri.Parse($"at://{ModDid}/app.bsky.feed.post/3k2la");
        _fixture.On("tools.ozone.moderation.getRecord", $$"""
            {"uri":"{{uri}}","cid":"{{CidText}}","value":{},"blobs":[],"indexedAt":"2024-01-01T00:00:00Z",
             "moderation":{},"repo":{"did":"{{ModDid}}","handle":"mod.example.com","indexedAt":"2024-01-01T00:00:00Z","moderation":{} } }
            """);

        var record = await Client.Ozone.Moderation.GetRecordAsync(uri, Cid.Parse(CidText));

        Assert.Equal(
            $"?uri={uri}&cid={CidText}",
            Uri.UnescapeDataString(_fixture.To("tools.ozone.moderation.getRecord").Single().Uri.Query));
        Assert.Equal(uri, record.Uri);
        Assert.Equal(Handle.Parse("mod.example.com"), record.Repo.Handle);
    }

    [Fact]
    public async Task EmitEventAsync_WritesTypedIdentifiersAsStrings()
    {
        _fixture.On("tools.ozone.moderation.emitEvent", $$"""
            {"id":1,"event":{"$type":"tools.ozone.moderation.defs#modEventAcknowledge"},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{ModDid}}"},
             "createdBy":"{{ModDid}}","createdAt":"2024-01-01T00:00:00.000Z"}
            """);

        await Client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
        {
            Event = new ModEventAcknowledge(),
            Subject = new RepoSubject { Did = Did.Parse(ModDid) },
            SubjectBlobCids = [Cid.Parse(CidText)],
            CreatedBy = Did.Parse(ModDid),
        });

        var body = _fixture.To("tools.ozone.moderation.emitEvent").Single().JsonBody;
        Assert.Equal(ModDid, body.GetProperty("createdBy").GetString());
        Assert.Equal(CidText, body.GetProperty("subjectBlobCids")[0].GetString());
        Assert.Equal(ModDid, body.GetProperty("subject").GetProperty("did").GetString());
    }

    [Fact]
    public async Task ListMembersAsync_AdminTokenAsLastUpdatedBy_ReadsAsIs()
    {
        _fixture.On("tools.ozone.team.listMembers", $$"""
            {"members":[{"did":"{{ModDid}}","role":"tools.ozone.team.defs#roleAdmin","lastUpdatedBy":"admin_token"}]}
            """);

        var page = await Client.Ozone.Team.ListMembersAsync();

        Assert.Equal("admin_token", Assert.Single(page.Members).LastUpdatedBy);
    }

    [Fact]
    public async Task FindRelatedAccountsAsync_LimitThenCursor_SendsBoth()
    {
        _fixture.On("tools.ozone.signature.findRelatedAccounts", """{"accounts":[]}""");

        await Client.Ozone.Signature.FindRelatedAccountsAsync(Did.Parse(ModDid), 10, "c");

        var query = Uri.UnescapeDataString(_fixture.To("tools.ozone.signature.findRelatedAccounts").Single().Uri.Query);
        Assert.Contains($"did={ModDid}", query);
        Assert.Contains("limit=10", query);
        Assert.Contains("cursor=c", query);
    }

    [Fact]
    public async Task SearchAccountsAsync_IsAQueryWithOneValuesParameterEach()
    {
        _fixture.On(
            "tools.ozone.signature.searchAccounts",
            $$"""{"accounts":[{"did":"{{ModDid}}","handle":"mod.example.com","indexedAt":"2026-09-01T00:00:00.000Z"}]}""");

        var page = await Client.Ozone.Signature.SearchAccountsAsync(["192.0.2.1", "device-7"], 10, "c");

        // A query, not a procedure: the values go in the query string and nothing in a body.
        var sent = _fixture.To("tools.ozone.signature.searchAccounts").Single();
        Assert.Empty(sent.Body);
        Assert.Equal(
            "?values=192.0.2.1&values=device-7&limit=10&cursor=c",
            Uri.UnescapeDataString(sent.Uri.Query));
        Assert.Equal(ModDid, Assert.Single(page.Accounts).Did.Value);
    }

    public static TheoryData<string> Enumerators =>
    [
        "events", "statuses", "repos", "sets", "values", "members",
    ];

    [Theory]
    [MemberData(nameof(Enumerators))]
    public async Task Enumerate_WalksPagesWithPageSizeAndStopsOnARepeatedCursor(string listing)
    {
        // Two pages, then the second cursor again: the paginator must stop instead of looping.
        var (nsid, pages, enumerate) = Listing(listing);
        foreach (var page in pages)
            _fixture.On(nsid, page);

        var items = await enumerate(Client);

        Assert.Equal(3, items);
        Assert.Equal(3, _fixture.Requests.Count);
        Assert.All(_fixture.Requests, r => Assert.Equal($"/xrpc/{nsid}", r.Path));
        Assert.All(_fixture.Requests, r => Assert.Contains("limit=2", r.Uri.Query));
        Assert.DoesNotContain("cursor=", _fixture.Requests[0].Uri.Query);
        Assert.Contains("cursor=c1", _fixture.Requests[1].Uri.Query);
        Assert.Contains("cursor=c2", _fixture.Requests[2].Uri.Query);
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
            "statuses" => ("tools.ozone.moderation.queryStatuses", Pages("subjectStatuses", status),
                c => Count(c.Ozone.Moderation.EnumerateStatusesAsync(pageSize: 2))),
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
}
