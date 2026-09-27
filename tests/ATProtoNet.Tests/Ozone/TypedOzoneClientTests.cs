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

}
