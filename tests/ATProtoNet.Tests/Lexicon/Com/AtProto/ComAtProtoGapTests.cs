using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Temp;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.repo.importRepo</c>, <c>admin.searchAccounts</c> /
/// <c>updateAccountSigningKey</c> and the <c>com.atproto.temp</c> methods: what goes on the wire,
/// and how the answers read back.
/// </summary>
public sealed class ComAtProtoGapTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string KeyText = "did:key:zQ3shTbmthbWnaziXpJifLJvXvGwVyW3zeWetTatAXUtA1b2S";

    private readonly CapturingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public ComAtProtoGapTests()
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

    // ──────────────────────────────────────────────────────────
    //  com.atproto.repo.importRepo
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportRepoAsync_PostsTheCarWithItsLength()
    {
        _handler.Body = "";
        byte[] car = [0x3a, 0xa2, 0x65, 0x72, 0x6f, 0x6f, 0x74, 0x73];
        using var stream = new MemoryStream(car);

        await _client.Repo.ImportRepoAsync(stream);

        Assert.Equal(HttpMethod.Post, _handler.LastMethod);
        Assert.Equal("/xrpc/com.atproto.repo.importRepo", _handler.LastUri!.PathAndQuery);
        Assert.Equal("application/vnd.ipld.car", _handler.LastContentType);
        Assert.Equal(car.Length, _handler.LastContentLength);
        Assert.Equal(car, _handler.LastBytes);
    }

    [Fact]
    public async Task ImportRepoAsync_StartsAtTheStreamsPosition()
    {
        _handler.Body = "";
        using var stream = new MemoryStream([0xff, 0xff, 0x01, 0x02]);
        stream.Position = 2;

        await _client.Repo.ImportRepoAsync(stream);

        Assert.Equal(2, _handler.LastContentLength);
        Assert.Equal([0x01, 0x02], _handler.LastBytes);
    }

    [Fact]
    public async Task ImportRepoAsync_NonSeekableStream_IsSentChunked()
    {
        _handler.Body = "";
        using var stream = new NonSeekableStream([0x01, 0x02, 0x03]);

        await _client.Repo.ImportRepoAsync(stream);

        Assert.Null(_handler.LastContentLength);
        Assert.Equal([0x01, 0x02, 0x03], _handler.LastBytes);
    }

    [Fact]
    public async Task ImportRepoAsync_Rejected_ThrowsXrpcException()
    {
        _handler.Status = HttpStatusCode.BadRequest;
        _handler.Body = """{"error":"InvalidRequest","message":"Service is not accepting repo imports"}""";
        using var stream = new MemoryStream([0x01]);

        var ex = await Assert.ThrowsAsync<ATProtoNet.Http.XrpcException>(() => _client.Repo.ImportRepoAsync(stream));

        Assert.Equal("InvalidRequest", ex.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  com.atproto.admin
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAccountsAsync_SendsFiltersAndReadsAccounts()
    {
        _handler.Body = $$"""
            {"cursor":"c2","accounts":[{"did":"{{DidText}}","handle":"alice.test","email":"a@example.com","indexedAt":"2026-01-01T00:00:00.000Z"}]}
            """;

        var page = await _client.Admin.SearchAccountsAsync(email: "a@example.com", limit: 10, cursor: "c1");

        Assert.Equal(HttpMethod.Get, _handler.LastMethod);
        Assert.Equal(
            "/xrpc/com.atproto.admin.searchAccounts?email=a@example.com&limit=10&cursor=c1",
            Uri.UnescapeDataString(_handler.LastUri!.PathAndQuery));
        Assert.Equal("c2", page.Cursor);
        var account = Assert.Single(page.Accounts);
        Assert.Equal(Did.Parse(DidText), account.Did);
        Assert.Equal("a@example.com", account.Email);
    }

    [Fact]
    public async Task EnumerateSearchAccountsAsync_FollowsTheCursor()
    {
        _handler.Responses.Enqueue($$"""{"cursor":"next","accounts":[{"did":"{{DidText}}","handle":"a.test","indexedAt":"2026-01-01T00:00:00.000Z"}]}""");
        _handler.Responses.Enqueue("""{"accounts":[{"did":"did:plc:bbbbbbbbbbbbbbbbbbbbbbbb","handle":"b.test","indexedAt":"2026-01-01T00:00:00.000Z"}]}""");

        var accounts = await _client.Admin.EnumerateSearchAccountsAsync(pageSize: 1).ToListAsync();

        Assert.Equal(["a.test", "b.test"], accounts.Select(a => a.Handle.Value));
        Assert.Contains("cursor=next", _handler.LastUri!.Query);
    }

    [Fact]
    public async Task UpdateAccountSigningKeyAsync_PostsTheDidAndKey()
    {
        _handler.Body = "";

        await _client.Admin.UpdateAccountSigningKeyAsync(Did.Parse(DidText), Did.Parse(KeyText));

        Assert.Equal(HttpMethod.Post, _handler.LastMethod);
        Assert.Equal("/xrpc/com.atproto.admin.updateAccountSigningKey", _handler.LastUri!.PathAndQuery);
        using var body = JsonDocument.Parse(_handler.LastBytes);
        Assert.Equal(DidText, body.RootElement.GetProperty("did").GetString());
        Assert.Equal(KeyText, body.RootElement.GetProperty("signingKey").GetString());
    }

    // ──────────────────────────────────────────────────────────
    //  com.atproto.temp
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHandleAvailabilityAsync_Available_ReadsTheResult()
    {
        _handler.Body = """
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultAvailable"}}
            """;

        var response = await _client.Temp.CheckHandleAvailabilityAsync(
            Handle.Parse("alice.bsky.social"), "a@example.com", AtDatetime.Parse("1990-01-01T00:00:00.000Z"));

        Assert.Equal(HttpMethod.Get, _handler.LastMethod);
        Assert.Equal(
            "/xrpc/com.atproto.temp.checkHandleAvailability?handle=alice.bsky.social&email=a@example.com&birthDate=1990-01-01T00:00:00.000Z",
            Uri.UnescapeDataString(_handler.LastUri!.PathAndQuery));
        Assert.True(response.IsAvailable);
        Assert.IsType<HandleAvailable>(response.Result);
    }

    [Fact]
    public async Task CheckHandleAvailabilityAsync_Unavailable_ReadsTheSuggestions()
    {
        _handler.Body = """
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultUnavailable",
              "suggestions":[{"handle":"alice1.bsky.social","method":"append-number"},{"handle":"alice-x.bsky.social","method":"append-word"}]}}
            """;

        var response = await _client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        Assert.False(response.IsAvailable);
        var unavailable = Assert.IsType<HandleUnavailable>(response.Result);
        Assert.Equal(["alice1.bsky.social", "alice-x.bsky.social"], unavailable.Suggestions.Select(s => s.Handle.Value));
        Assert.Equal("append-number", unavailable.Suggestions[0].Method);
        Assert.Equal("/xrpc/com.atproto.temp.checkHandleAvailability?handle=alice.bsky.social", _handler.LastUri!.PathAndQuery);
    }

    [Fact]
    public async Task CheckHandleAvailabilityAsync_UnknownResult_IsKeptRaw()
    {
        _handler.Body = """
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultReserved","until":"2027"}}
            """;

        var response = await _client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        var unknown = Assert.IsType<UnknownHandleAvailabilityResult>(response.Result);
        Assert.Equal("com.atproto.temp.checkHandleAvailability#resultReserved", unknown.Type);
        Assert.Equal("2027", unknown.Raw.GetProperty("until").GetString());
        Assert.False(response.IsAvailable);
    }

    [Fact]
    public async Task CheckSignupQueueAsync_ReadsThePlaceInQueue()
    {
        _handler.Body = """{"activated":false,"placeInQueue":12,"estimatedTimeMs":3600000}""";

        var queue = await _client.Temp.CheckSignupQueueAsync();

        Assert.Equal(HttpMethod.Get, _handler.LastMethod);
        Assert.Equal("/xrpc/com.atproto.temp.checkSignupQueue", _handler.LastUri!.PathAndQuery);
        Assert.False(queue.Activated);
        Assert.Equal(12, queue.PlaceInQueue);
        Assert.Equal(3_600_000, queue.EstimatedTimeMs);
    }

    [Fact]
    public async Task DereferenceScopeAsync_ReturnsTheFullScope()
    {
        _handler.Body = """{"scope":"atproto repo:app.bsky.feed.post?action=create"}""";

        var scope = await _client.Temp.DereferenceScopeAsync("ref:abc123");

        Assert.Equal("/xrpc/com.atproto.temp.dereferenceScope?scope=ref:abc123", Uri.UnescapeDataString(_handler.LastUri!.PathAndQuery));
        Assert.Equal("atproto repo:app.bsky.feed.post?action=create", scope);
    }

    [Fact]
    public async Task RequestPhoneVerificationAsync_PostsTheNumber()
    {
        _handler.Body = "";

        await _client.Temp.RequestPhoneVerificationAsync("+15555550123");

        Assert.Equal(HttpMethod.Post, _handler.LastMethod);
        Assert.Equal("/xrpc/com.atproto.temp.requestPhoneVerification", _handler.LastUri!.PathAndQuery);
        using var body = JsonDocument.Parse(_handler.LastBytes);
        Assert.Equal("+15555550123", body.RootElement.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task RevokeAccountCredentialsAsync_PostsTheAccount()
    {
        _handler.Body = "";

        await _client.Temp.RevokeAccountCredentialsAsync(Did.Parse(DidText));

        Assert.Equal(HttpMethod.Post, _handler.LastMethod);
        Assert.Equal("/xrpc/com.atproto.temp.revokeAccountCredentials", _handler.LastUri!.PathAndQuery);
        using var body = JsonDocument.Parse(_handler.LastBytes);
        Assert.Equal(DidText, body.RootElement.GetProperty("account").GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";

        /// <summary>Bodies to answer with in order, before falling back to <see cref="Body"/>.</summary>
        public Queue<string> Responses { get; } = new();

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        public string? LastContentType { get; private set; }

        public long? LastContentLength { get; private set; }

        public byte[] LastBytes { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastContentLength = request.Content?.Headers.ContentLength;
            LastBytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            var body = Responses.Count > 0 ? Responses.Dequeue() : Body;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>A stream that cannot report its length, like a download being passed straight on.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
