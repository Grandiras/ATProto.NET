using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using ATProtoNet.Http;

namespace ATProtoNet.Tests;

/// <summary>
/// How record reads report absence and malformed values.
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
        _handler.Next = (HttpStatusCode.OK, $$"""{"did":"{{Did}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}""");
        await _client.LoginAsync("alice.test", "password");
        return _client.GetCollection<Note>("com.example.note");
    }

    [Fact]
    public async Task ExistsAsync_WhenTheRecordExists_IsTrue()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.note/n1","cid":"bafyrei","value":{"text":"hi"}}""");

        Assert.True(await notes.ExistsAsync("n1"));
    }

    [Fact]
    public async Task ExistsAsync_OnRecordNotFound_IsFalse()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.BadRequest, """{"error":"RecordNotFound","message":"Could not locate record"}""");

        Assert.False(await notes.ExistsAsync("missing"));
    }

    [Fact]
    public async Task ExistsAsync_OnAnyOtherInvalidRequest_Throws()
    {
        // A malformed key or a missing repo is InvalidRequest too; answering "does not exist"
        // for it would hide the actual problem.
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.BadRequest, """{"error":"InvalidRequest","message":"Could not find repo"}""");

        var ex = await Assert.ThrowsAsync<XrpcException>(() => notes.ExistsAsync("n1"));

        Assert.True(ex.Is(XrpcErrors.InvalidRequest));
    }

    [Fact]
    public async Task GetAsync_WhenTheValueIsNotTheRecordType_ThrowsResponseFormatException()
    {
        var notes = await LoginAsync();
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.note/n1","cid":"bafyrei","value":{"text":42}}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(() => notes.GetAsync("n1"));

        Assert.Equal("com.atproto.repo.getRecord", ex.Nsid);
        Assert.Contains($"at://{Did}/com.example.note/n1", ex.Message);
    }

    [Fact]
    public async Task RepoGetRecordAsyncOfT_WhenTheValueIsNotTheRecordType_ThrowsResponseFormatException()
    {
        _handler.Next = (HttpStatusCode.OK, $$$"""{"uri":"at://{{{Did}}}/com.example.note/n1","cid":"bafyrei","value":{"text":[1]}}""");

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _client.Repo.GetRecordAsync<Note>(Did, "com.example.note", "n1"));

        Assert.Equal("com.atproto.repo.getRecord", ex.Nsid);
    }

    private sealed class Note
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Next { get; set; } = (HttpStatusCode.OK, "{}");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Next.Status)
            {
                Content = new StringContent(Next.Body, Encoding.UTF8, "application/json"),
            });
    }
}
