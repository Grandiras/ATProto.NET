using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// The typed com.atproto surface: identifiers go out as their strings and come back parsed.
/// </summary>
public class TypedRepoClientTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string CidText = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private static readonly Did Alice = Did.Parse(DidText);
    private static readonly Nsid Notes = Nsid.Parse("com.example.note");

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public TypedRepoClientTests()
    {
        _stub.Fallback("{}");
        _httpClient = new HttpClient(_stub);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _stub.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpStub.RecordedRequest Last => _stub.Requests[^1];

    [Fact]
    public async Task CreateRecordAsync_WritesIdentifiersAsStringsAndParsesTheResponse()
    {
        _stub.Fallback($$$"""{"uri":"at://{{{DidText}}}/com.example.note/3k2la","cid":"{{{CidText}}}","commit":{"cid":"{{{CidText}}}","rev":"3k2la2bcd5e2a"}}""");

        var created = await _client.Repo.CreateRecordAsync(
            Alice, Notes, new { text = "hi" }, RecordKey.Parse("3k2la"), swapCommit: Cid.Parse(CidText));

        var body = Last.JsonBody;
        Assert.Equal(DidText, body.GetProperty("repo").GetString());
        Assert.Equal("com.example.note", body.GetProperty("collection").GetString());
        Assert.Equal("3k2la", body.GetProperty("rkey").GetString());
        Assert.Equal(CidText, body.GetProperty("swapCommit").GetString());

        Assert.Equal(RecordKey.Parse("3k2la"), created.Uri.RecordKey);
        Assert.Equal(CidText, created.Cid.Value);
        Assert.Equal(Tid.Parse("3k2la2bcd5e2a"), created.Commit!.Rev);
    }

    [Fact]
    public async Task GetRecordAsync_ByAtUri_SplitsTheUriIntoItsParameters()
    {
        _stub.Fallback($$$"""{"uri":"at://{{{DidText}}}/com.example.note/n1","value":{}}""");

        var record = await _client.Repo.GetRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note/n1"));

        Assert.Equal(
            $"https://pds.example.com/xrpc/com.atproto.repo.getRecord?repo={DidText}&collection=com.example.note&rkey=n1",
            Uri.UnescapeDataString(Last.Uri.ToString()));
        Assert.Null(record.Cid);
    }

    [Fact]
    public async Task DeleteRecordAsync_ByAtUri_SendsItsParts()
    {
        await _client.Repo.DeleteRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note/n1"));

        var body = Last.JsonBody;
        Assert.Equal(DidText, body.GetProperty("repo").GetString());
        Assert.Equal("n1", body.GetProperty("rkey").GetString());
    }

    [Fact]
    public async Task GetRecordAsync_UriWithoutRecordKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.Repo.GetRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note")));
        Assert.Empty(_stub.Requests);
    }

    [Fact]
    public async Task ListRecordsAsync_SendsFiltersLimitAndCursor()
    {
        _stub.Fallback("""{"records":[]}""");

        await _client.Repo.ListRecordsAsync(Alice, Notes, reverse: true, limit: 5, cursor: "c");

        var query = Uri.UnescapeDataString(Last.Query);
        Assert.Contains("limit=5", query);
        Assert.Contains("cursor=c", query);
        Assert.Contains("reverse=true", query);
    }

    [Fact]
    public async Task Response_WithAnInvalidIdentifier_IsAResponseFormatError()
    {
        _stub.Fallback($$$"""{"uri":"at://{{{DidText}}}/com.example.note/n1","cid":"not-a-cid","value":{}}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _client.Repo.GetRecordAsync(Alice, Notes, RecordKey.Parse("n1")));

        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task CreateReportAsync_SubjectFirst_SerializesTheTypedSubject()
    {
        _stub.Fallback($$"""{"id":1,"reasonType":"{{ReportReasons.Spam}}","subject":{"$type":"com.atproto.repo.strongRef","uri":"at://{{DidText}}/com.example.note/n1","cid":"{{CidText}}"},"reportedBy":"{{DidText}}","createdAt":"2024-01-01T00:00:00.000Z"}""");

        var report = await _client.Moderation.CreateReportAsync(
            new RecordSubject { Uri = AtUri.Parse($"at://{DidText}/com.example.note/n1"), Cid = Cid.Parse(CidText) },
            ReportReasons.Spam);

        var subject = Last.JsonBody.GetProperty("subject");
        Assert.Equal("com.atproto.repo.strongRef", subject.GetProperty("$type").GetString());
        Assert.Equal(CidText, subject.GetProperty("cid").GetString());
        Assert.Equal(Alice, report.ReportedBy);
        Assert.Equal("2024-01-01T00:00:00.000Z", report.CreatedAt.ToString());
        Assert.Equal(Cid.Parse(CidText), Assert.IsType<RecordSubject>(report.Subject).Cid);
    }
}
