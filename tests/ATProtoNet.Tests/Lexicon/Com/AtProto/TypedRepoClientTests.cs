using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// The typed com.atproto surface: identifiers go out as their strings and come back parsed, and
/// a response with an invalid one is a format error. The AT URI overloads' request shapes are
/// rows in <see cref="EndpointRequestTests"/>.
/// </summary>
public class TypedRepoClientTests : IDisposable
{
    private const string DidText = TestIds.ModDid;
    private const string CidText = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    private static readonly Did Alice = Did.Parse(DidText);
    private static readonly Nsid Notes = Nsid.Parse("com.example.note");

    private readonly XrpcTestClient _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CreateRecordAsync_WritesIdentifiersAsStringsAndParsesTheResponse()
    {
        _fixture.Fallback($$$"""{"uri":"at://{{{DidText}}}/com.example.note/3k2la","cid":"{{{CidText}}}","commit":{"cid":"{{{CidText}}}","rev":"3k2la2bcd5e2a"}}""");

        var created = await _fixture.Client.Repo.CreateRecordAsync(
            Alice, Notes, new { text = "hi" }, RecordKey.Parse("3k2la"), swapCommit: Cid.Parse(CidText));

        _fixture.AssertPost(
            "com.atproto.repo.createRecord",
            $$"""{"repo":"{{DidText}}","collection":"com.example.note","rkey":"3k2la","record":{"text":"hi"},"swapCommit":"{{CidText}}"}""");
        Assert.Equal(RecordKey.Parse("3k2la"), created.Uri.RecordKey);
        Assert.Equal(CidText, created.Cid.Value);
        Assert.Equal(Tid.Parse("3k2la2bcd5e2a"), created.Commit!.Rev);
    }

    [Fact]
    public async Task GetRecordAsync_UriWithoutRecordKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _fixture.Client.Repo.GetRecordAsync(AtUri.Parse($"at://{DidText}/com.example.note")));
        Assert.Empty(_fixture.Requests);
    }

    [Fact]
    public async Task Response_WithAnInvalidIdentifier_IsAResponseFormatError()
    {
        _fixture.Fallback($$$"""{"uri":"at://{{{DidText}}}/com.example.note/n1","cid":"not-a-cid","value":{}}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _fixture.Client.Repo.GetRecordAsync(Alice, Notes, RecordKey.Parse("n1")));

        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task CreateReportAsync_SubjectFirst_SerializesTheTypedSubject()
    {
        _fixture.Fallback($$"""{"id":1,"reasonType":"{{ReportReasons.Spam}}","subject":{"$type":"com.atproto.repo.strongRef","uri":"at://{{DidText}}/com.example.note/n1","cid":"{{CidText}}"},"reportedBy":"{{DidText}}","createdAt":"2024-01-01T00:00:00.000Z"}""");

        var report = await _fixture.Client.Moderation.CreateReportAsync(
            new RecordSubject { Uri = AtUri.Parse($"at://{DidText}/com.example.note/n1"), Cid = Cid.Parse(CidText) },
            ReportReasons.Spam);

        var subject = _fixture.Last.JsonBody.GetProperty("subject");
        Assert.Equal("com.atproto.repo.strongRef", subject.GetProperty("$type").GetString());
        Assert.Equal(CidText, subject.GetProperty("cid").GetString());
        Assert.Equal(Alice, report.ReportedBy);
        Assert.Equal("2024-01-01T00:00:00.000Z", report.CreatedAt.ToString());
        Assert.Equal(Cid.Parse(CidText), Assert.IsType<RecordSubject>(report.Subject).Cid);
    }
}
