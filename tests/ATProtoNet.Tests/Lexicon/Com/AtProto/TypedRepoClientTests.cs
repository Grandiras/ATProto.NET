using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;

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

    private readonly CapturingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public TypedRepoClientTests()
    {
        _httpClient = new HttpClient(_handler);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
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
    public async Task CreateRecordAsync_WritesIdentifiersAsStringsAndParsesTheResponse()
    {
        _handler.Body = $$$"""{"uri":"at://{{{DidText}}}/com.example.note/3k2la","cid":"{{{CidText}}}","commit":{"cid":"{{{CidText}}}","rev":"3k2la2bcd5e2a"}}""";

        var created = await _client.Repo.CreateRecordAsync(
            Alice, Notes, new { text = "hi" }, RecordKey.Parse("3k2la"), swapCommit: Cid.Parse(CidText));

        using var body = JsonDocument.Parse(_handler.LastBody!);
        Assert.Equal(DidText, body.RootElement.GetProperty("repo").GetString());
        Assert.Equal("com.example.note", body.RootElement.GetProperty("collection").GetString());
        Assert.Equal("3k2la", body.RootElement.GetProperty("rkey").GetString());
        Assert.Equal(CidText, body.RootElement.GetProperty("swapCommit").GetString());

        Assert.Equal(RecordKey.Parse("3k2la"), created.Uri.RecordKey);
        Assert.Equal(CidText, created.Cid.Value);
        Assert.Equal(Tid.Parse("3k2la2bcd5e2a"), created.Commit!.Rev);
    }

    [Fact]
    public async Task GetRecordAsync_ByAtUri_SplitsTheUriIntoItsParameters()
    {
        _handler.Body = $$$"""{"uri":"at://{{{DidText}}}/com.example.note/n1","value":{}}""";

        var record = await _client.Repo.GetRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note/n1"));

        Assert.Equal(
            $"https://pds.example.com/xrpc/com.atproto.repo.getRecord?repo={DidText}&collection=com.example.note&rkey=n1",
            Uri.UnescapeDataString(_handler.LastUri!));
        Assert.Null(record.Cid);
    }

    [Fact]
    public async Task DeleteRecordAsync_ByAtUri_SendsItsParts()
    {
        _handler.Body = "{}";

        await _client.Repo.DeleteRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note/n1"));

        using var body = JsonDocument.Parse(_handler.LastBody!);
        Assert.Equal(DidText, body.RootElement.GetProperty("repo").GetString());
        Assert.Equal("n1", body.RootElement.GetProperty("rkey").GetString());
    }

    [Fact]
    public async Task GetRecordAsync_UriWithoutRecordKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.Repo.GetRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note")));
        Assert.Null(_handler.LastUri);
    }

    [Fact]
    public async Task ListRecordsAsync_SendsFiltersLimitAndCursor()
    {
        _handler.Body = """{"records":[]}""";

        await _client.Repo.ListRecordsAsync(Alice, Notes, reverse: true, limit: 5, cursor: "c");

        var query = Uri.UnescapeDataString(new Uri(_handler.LastUri!).Query);
        Assert.Contains("limit=5", query);
        Assert.Contains("cursor=c", query);
        Assert.Contains("reverse=true", query);
    }

    [Fact]
    public async Task Response_WithAnInvalidIdentifier_IsAResponseFormatError()
    {
        _handler.Body = $$$"""{"uri":"at://{{{DidText}}}/com.example.note/n1","cid":"not-a-cid","value":{}}""";

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _client.Repo.GetRecordAsync(Alice, Notes, RecordKey.Parse("n1")));

        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task CreateReportAsync_SubjectFirst_SerializesTheTypedSubject()
    {
        _handler.Body = $$"""{"id":1,"reasonType":"{{ReportReasons.Spam}}","subject":{"$type":"com.atproto.repo.strongRef","uri":"at://{{DidText}}/com.example.note/n1","cid":"{{CidText}}"},"reportedBy":"{{DidText}}","createdAt":"2024-01-01T00:00:00.000Z"}""";

        var report = await _client.Moderation.CreateReportAsync(
            new RecordSubject { Uri = AtUri.Parse($"at://{DidText}/com.example.note/n1"), Cid = Cid.Parse(CidText) },
            ReportReasons.Spam);

        using var body = JsonDocument.Parse(_handler.LastBody!);
        var subject = body.RootElement.GetProperty("subject");
        Assert.Equal("com.atproto.repo.strongRef", subject.GetProperty("$type").GetString());
        Assert.Equal(CidText, subject.GetProperty("cid").GetString());
        Assert.Equal(Alice, report.ReportedBy);
        Assert.Equal("2024-01-01T00:00:00.000Z", report.CreatedAt.ToString());
        Assert.Equal(Cid.Parse(CidText), Assert.IsType<RecordSubject>(report.Subject).Cid);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";

        public string? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri!.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
