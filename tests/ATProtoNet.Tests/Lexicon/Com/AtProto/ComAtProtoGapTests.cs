using System.Net;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Temp;
using ATProtoNet.Tests.TestSupport;

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

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public ComAtProtoGapTests()
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

    // ──────────────────────────────────────────────────────────
    //  com.atproto.repo.importRepo
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportRepoAsync_PostsTheCarWithItsLength()
    {
        byte[] car = [0x3a, 0xa2, 0x65, 0x72, 0x6f, 0x6f, 0x74, 0x73];
        using var stream = new MemoryStream(car);

        await _client.Repo.ImportRepoAsync(stream);

        Assert.Equal(HttpMethod.Post, Last.Method);
        Assert.Equal("/xrpc/com.atproto.repo.importRepo", Last.Path);
        Assert.Equal("application/vnd.ipld.car", Last.ContentType);
        Assert.Equal(car.Length, Last.ContentLength);
        Assert.Equal(car, Last.Body);
    }

    [Fact]
    public async Task ImportRepoAsync_StartsAtTheStreamsPosition()
    {
        using var stream = new MemoryStream([0xff, 0xff, 0x01, 0x02]);
        stream.Position = 2;

        await _client.Repo.ImportRepoAsync(stream);

        Assert.Equal(2, Last.ContentLength);
        Assert.Equal([0x01, 0x02], Last.Body);
    }

    [Fact]
    public async Task ImportRepoAsync_NonSeekableStream_IsSentChunked()
    {
        using var stream = new NonSeekableStream([0x01, 0x02, 0x03]);

        await _client.Repo.ImportRepoAsync(stream);

        Assert.Null(Last.ContentLength);
        Assert.Equal([0x01, 0x02, 0x03], Last.Body);
    }

    [Fact]
    public async Task ImportRepoAsync_Rejected_ThrowsXrpcException()
    {
        _stub.Fallback(_ => HttpStub.JsonResponse(
            """{"error":"InvalidRequest","message":"Service is not accepting repo imports"}""", HttpStatusCode.BadRequest));
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
        _stub.Fallback($$"""
            {"cursor":"c2","accounts":[{"did":"{{DidText}}","handle":"alice.test","email":"a@example.com","indexedAt":"2026-01-01T00:00:00.000Z"}]}
            """);

        var page = await _client.Admin.SearchAccountsAsync(email: "a@example.com", limit: 10, cursor: "c1");

        Assert.Equal(HttpMethod.Get, Last.Method);
        Assert.Equal(
            "/xrpc/com.atproto.admin.searchAccounts?email=a@example.com&limit=10&cursor=c1",
            $"{Last.Path}?{Uri.UnescapeDataString(Last.Query)}");
        Assert.Equal("c2", page.Cursor);
        var account = Assert.Single(page.Accounts);
        Assert.Equal(Did.Parse(DidText), account.Did);
        Assert.Equal("a@example.com", account.Email);
    }

    [Fact]
    public async Task EnumerateSearchAccountsAsync_FollowsTheCursor()
    {
        _stub.On("com.atproto.admin.searchAccounts", $$"""{"cursor":"next","accounts":[{"did":"{{DidText}}","handle":"a.test","indexedAt":"2026-01-01T00:00:00.000Z"}]}""");
        _stub.On("com.atproto.admin.searchAccounts", """{"accounts":[{"did":"did:plc:bbbbbbbbbbbbbbbbbbbbbbbb","handle":"b.test","indexedAt":"2026-01-01T00:00:00.000Z"}]}""");

        var accounts = await _client.Admin.EnumerateSearchAccountsAsync(pageSize: 1).ToListAsync();

        Assert.Equal(["a.test", "b.test"], accounts.Select(a => a.Handle.Value));
        Assert.Contains("cursor=next", Last.Query);
    }

    [Fact]
    public async Task UpdateAccountSigningKeyAsync_PostsTheDidAndKey()
    {
        await _client.Admin.UpdateAccountSigningKeyAsync(Did.Parse(DidText), Did.Parse(KeyText));

        Assert.Equal(HttpMethod.Post, Last.Method);
        Assert.Equal("/xrpc/com.atproto.admin.updateAccountSigningKey", Last.Path);
        Assert.Equal(DidText, Last.JsonBody.GetProperty("did").GetString());
        Assert.Equal(KeyText, Last.JsonBody.GetProperty("signingKey").GetString());
    }

    // ──────────────────────────────────────────────────────────
    //  com.atproto.temp
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHandleAvailabilityAsync_Available_ReadsTheResult()
    {
        _stub.Fallback("""
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultAvailable"}}
            """);

        var response = await _client.Temp.CheckHandleAvailabilityAsync(
            Handle.Parse("alice.bsky.social"), "a@example.com", AtDatetime.Parse("1990-01-01T00:00:00.000Z"));

        Assert.Equal(HttpMethod.Get, Last.Method);
        Assert.Equal(
            "/xrpc/com.atproto.temp.checkHandleAvailability?handle=alice.bsky.social&email=a@example.com&birthDate=1990-01-01T00:00:00.000Z",
            $"{Last.Path}?{Uri.UnescapeDataString(Last.Query)}");
        Assert.True(response.IsAvailable);
        Assert.IsType<HandleAvailable>(response.Result);
    }

    [Fact]
    public async Task CheckHandleAvailabilityAsync_Unavailable_ReadsTheSuggestions()
    {
        _stub.Fallback("""
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultUnavailable",
              "suggestions":[{"handle":"alice1.bsky.social","method":"append-number"},{"handle":"alice-x.bsky.social","method":"append-word"}]}}
            """);

        var response = await _client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        Assert.False(response.IsAvailable);
        var unavailable = Assert.IsType<HandleUnavailable>(response.Result);
        Assert.Equal(["alice1.bsky.social", "alice-x.bsky.social"], unavailable.Suggestions.Select(s => s.Handle.Value));
        Assert.Equal("append-number", unavailable.Suggestions[0].Method);
        Assert.Equal("/xrpc/com.atproto.temp.checkHandleAvailability?handle=alice.bsky.social", $"{Last.Path}?{Last.Query}");
    }

    [Fact]
    public async Task CheckHandleAvailabilityAsync_UnknownResult_IsKeptRaw()
    {
        _stub.Fallback("""
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultReserved","until":"2027"}}
            """);

        var response = await _client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        var unknown = Assert.IsType<UnknownHandleAvailabilityResult>(response.Result);
        Assert.Equal("com.atproto.temp.checkHandleAvailability#resultReserved", unknown.Type);
        Assert.Equal("2027", unknown.Raw.GetProperty("until").GetString());
        Assert.False(response.IsAvailable);
    }

    [Fact]
    public async Task CheckSignupQueueAsync_ReadsThePlaceInQueue()
    {
        _stub.Fallback("""{"activated":false,"placeInQueue":12,"estimatedTimeMs":3600000}""");

        var queue = await _client.Temp.CheckSignupQueueAsync();

        Assert.Equal(HttpMethod.Get, Last.Method);
        Assert.Equal("/xrpc/com.atproto.temp.checkSignupQueue", Last.Path);
        Assert.False(queue.Activated);
        Assert.Equal(12, queue.PlaceInQueue);
        Assert.Equal(3_600_000, queue.EstimatedTimeMs);
    }

    [Fact]
    public async Task DereferenceScopeAsync_ReturnsTheFullScope()
    {
        _stub.Fallback("""{"scope":"atproto repo:app.bsky.feed.post?action=create"}""");

        var scope = await _client.Temp.DereferenceScopeAsync("ref:abc123");

        Assert.Equal("/xrpc/com.atproto.temp.dereferenceScope?scope=ref:abc123", $"{Last.Path}?{Uri.UnescapeDataString(Last.Query)}");
        Assert.Equal("atproto repo:app.bsky.feed.post?action=create", scope);
    }

    [Fact]
    public async Task RequestPhoneVerificationAsync_PostsTheNumber()
    {
        await _client.Temp.RequestPhoneVerificationAsync("+15555550123");

        Assert.Equal(HttpMethod.Post, Last.Method);
        Assert.Equal("/xrpc/com.atproto.temp.requestPhoneVerification", Last.Path);
        Assert.Equal("+15555550123", Last.JsonBody.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task RevokeAccountCredentialsAsync_PostsTheAccount()
    {
        await _client.Temp.RevokeAccountCredentialsAsync(Did.Parse(DidText));

        Assert.Equal(HttpMethod.Post, Last.Method);
        Assert.Equal("/xrpc/com.atproto.temp.revokeAccountCredentials", Last.Path);
        Assert.Equal(DidText, Last.JsonBody.GetProperty("account").GetString());
    }

    /// <summary>A stream that cannot report its length, like a download being passed straight on.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
