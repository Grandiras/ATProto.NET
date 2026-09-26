using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests;

/// <summary>
/// What <see cref="RecordCollection{T}"/> sends and how its reads report absence and malformed
/// values.
/// </summary>
public class RecordCollectionErrorTests : IDisposable
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private readonly StubHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public RecordCollectionErrorTests()
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

    private async Task<RecordCollection<Note>> LoginAsync()
    {
        await SignInAsync();
        return _client.GetCollection<Note>(Nsid.Parse("com.example.note"));
    }

    private async Task<RecordCollection<StampedNote>> LoginAsync(Nsid collection)
    {
        await SignInAsync();
        return _client.GetCollection<StampedNote>(collection);
    }

    private async Task SignInAsync()
    {
        _handler.Next = (HttpStatusCode.OK, $$"""{"did":"{{Did}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}""");
        await _client.LoginAsync("alice.test", "password");
    }

    private const string NoteUri = $"at://{Did}/com.example.note/n1";
    private const string NoteCid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    [Fact]
    public async Task FindAsync_WhenTheRecordExists_ReturnsIt()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"{{{NoteUri}}}","cid":"{{{NoteCid}}}","value":{"text":"hi"}}""");

        var note = await notes.FindAsync(RecordKey.Parse("n1"));

        Assert.NotNull(note);
        Assert.Equal("hi", note.Value.Text);
        Assert.Equal("n1", note.RecordKey);
        Assert.Equal(Cid.Parse(NoteCid), note.Cid);
    }

    [Fact]
    public async Task FindAsync_OnRecordNotFound_ReturnsNull()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.BadRequest, """{"error":"RecordNotFound","message":"Could not locate record"}""");

        Assert.Null(await notes.FindAsync(RecordKey.Parse("missing")));
        Assert.Null(await notes.FindFromAsync(AtIdentifier.Parse("did:plc:bob"), RecordKey.Parse("missing")));
    }

    [Fact]
    public async Task FindAsync_OnAnyOtherError_Throws()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.BadRequest, """{"error":"InvalidRequest","message":"Could not find repo"}""");

        var ex = await Assert.ThrowsAsync<XrpcException>(() => notes.FindAsync(RecordKey.Parse("n1")));

        Assert.True(ex.Is(XrpcErrors.InvalidRequest));
    }

    [Fact]
    public async Task OwnRepoCalls_WithoutASession_ThrowAuthenticationRequired()
    {
        var notes = _client.GetCollection<Note>(Nsid.Parse("com.example.note"));

        var ex = await Assert.ThrowsAsync<XrpcAuthenticationException>(() => notes.GetAsync(RecordKey.Parse("n1")));
        Assert.True(ex.Is(XrpcErrors.AuthenticationRequired));

        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => notes.CreateAsync(new Note()));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => notes.FindAsync(RecordKey.Parse("n1")));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => notes.PutAsync(RecordKey.Parse("n1"), new Note()));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => notes.DeleteAsync(RecordKey.Parse("n1")));
        await Assert.ThrowsAsync<XrpcAuthenticationException>(() => notes.ListAsync());
        Assert.Throws<XrpcAuthenticationException>(() => notes.EnumerateAsync());
        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task CreateAsync_WithoutCreatedAt_StampsTheCurrentTime()
    {
        var notes = await LoginAsync(Nsid.Parse("com.example.stamped"));
        var record = new StampedNote { Text = "hi" };
        _handler.Next = (HttpStatusCode.OK, $$"""{"uri":"at://{{Did}}/com.example.stamped/3l2abc","cid":"{{NoteCid}}"}""");
        var before = DateTimeOffset.UtcNow;

        var created = await notes.CreateAsync(record);

        var sent = _handler.LastBody!.Value.GetProperty("record").GetProperty("createdAt").GetString();
        Assert.Equal(record.CreatedAt?.ToString(), sent);
        Assert.InRange(AtDatetime.Parse(sent!).Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal("3l2abc", created.RecordKey);
    }

    [Fact]
    public async Task CreateAsync_WithCreatedAt_KeepsIt()
    {
        var notes = await LoginAsync(Nsid.Parse("com.example.stamped"));
        var record = new StampedNote { Text = "hi", CreatedAt = AtDatetime.Parse("2020-01-01T00:00:00Z") };
        _handler.Next = (HttpStatusCode.OK, $$"""{"uri":"at://{{Did}}/com.example.stamped/3l2abc","cid":"{{NoteCid}}"}""");

        await notes.CreateAsync(record);

        Assert.Equal(
            "2020-01-01T00:00:00Z",
            _handler.LastBody!.Value.GetProperty("record").GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task PutAsync_OfARecordReadWithoutCreatedAt_DoesNotAddOne()
    {
        var notes = await LoginAsync(Nsid.Parse("com.example.stamped"));
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.stamped/n1","cid":"{{{NoteCid}}}","value":{"text":"old"}}""");
        var read = await notes.GetAsync(RecordKey.Parse("n1"));
        read.Value.Text = "new";
        _handler.Next = (HttpStatusCode.OK, $$"""{"uri":"at://{{Did}}/com.example.stamped/n1","cid":"{{NoteCid}}"}""");

        await notes.PutAsync(read.RecordKey, read.Value, swapRecord: read.Cid);

        var sent = _handler.LastBody!.Value;
        Assert.False(sent.GetProperty("record").TryGetProperty("createdAt", out _));
        Assert.Equal(NoteCid, sent.GetProperty("swapRecord").GetString());
    }

    [Fact]
    public async Task ListAsync_DeserializesEveryRecordOnThePage()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""
            {"cursor":"next","records":[
              {"uri":"at://{{{Did}}}/com.example.note/a","cid":"{{{NoteCid}}}","value":{"text":"one"}},
              {"uri":"at://{{{Did}}}/com.example.note/b","cid":"{{{NoteCid}}}","value":{"text":"two","extra":true}}]}
            """);

        var page = await notes.ListAsync(limit: 2);

        Assert.Equal(["one", "two"], page.Records.Select(r => r.Value.Text));
        Assert.Equal(["a", "b"], page.Records.Select(r => r.RecordKey.Value));
        Assert.Equal("next", page.Cursor);
    }

    [Fact]
    public async Task ListAsync_WhenARecordIsNotTheRecordType_ThrowsResponseFormatExceptionNamingTheCollection()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""
            {"records":[{"uri":"at://{{{Did}}}/com.example.note/a","cid":"{{{NoteCid}}}","value":{"text":false}}]}
            """);

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(() => notes.ListAsync());

        Assert.Equal("com.atproto.repo.listRecords", ex.Nsid);
        Assert.Contains($"at://{Did}/com.example.note", ex.Message);
        Assert.Contains("$.records[0].value.text", ex.Message);
    }

    [Fact]
    public async Task GetAsync_WhenTheValueIsNull_ThrowsResponseFormatException()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$"""{"uri":"{{NoteUri}}","cid":"{{NoteCid}}","value":null}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(() => notes.GetAsync(RecordKey.Parse("n1")));

        Assert.Contains(NoteUri, ex.Message);
    }

    [Fact]
    public async Task ExistsAsync_WhenTheRecordExists_IsTrue()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.note/n1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","value":{"text":"hi"}}""");

        Assert.True(await notes.ExistsAsync(RecordKey.Parse("n1")));
    }

    [Fact]
    public async Task ExistsAsync_OnRecordNotFound_IsFalse()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.BadRequest, """{"error":"RecordNotFound","message":"Could not locate record"}""");

        Assert.False(await notes.ExistsAsync(RecordKey.Parse("missing")));
    }

    [Fact]
    public async Task ExistsAsync_OnAnyOtherInvalidRequest_Throws()
    {
        // A malformed key or a missing repo is InvalidRequest too; answering "does not exist"
        // for it would hide the actual problem.
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.BadRequest, """{"error":"InvalidRequest","message":"Could not find repo"}""");

        var ex = await Assert.ThrowsAsync<XrpcException>(() => notes.ExistsAsync(RecordKey.Parse("n1")));

        Assert.True(ex.Is(XrpcErrors.InvalidRequest));
    }

    [Fact]
    public async Task GetAsync_WhenTheValueIsNotTheRecordType_ThrowsResponseFormatException()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.note/n1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","value":{"text":42}}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(() => notes.GetAsync(RecordKey.Parse("n1")));

        Assert.Equal("com.atproto.repo.getRecord", ex.Nsid);
        Assert.Contains($"at://{Did}/com.example.note/n1", ex.Message);
    }

    [Fact]
    public async Task RepoGetRecordAsyncOfT_WhenTheValueIsNotTheRecordType_ThrowsResponseFormatException()
    {
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.note/n1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","value":{"text":[1]}}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _client.Repo.GetRecordAsync<Note>(AtIdentifier.Parse(Did), Nsid.Parse("com.example.note"), RecordKey.Parse("n1")));

        Assert.Equal("com.atproto.repo.getRecord", ex.Nsid);
    }

    private sealed class Note
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    private sealed class StampedNote : AtProtoRecord
    {
        public override string Type => "com.example.stamped";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Next { get; set; } = (HttpStatusCode.OK, "{}");

        public int Requests { get; private set; }

        public JsonElement? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Content is not null)
            {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                LastBody = body.RootElement.Clone();
            }

            return new HttpResponseMessage(Next.Status)
            {
                Content = new StringContent(Next.Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
