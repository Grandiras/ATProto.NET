using System.Net;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Temp;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.repo.importRepo</c>'s stream upload, and <c>checkHandleAvailability</c>'s union
/// result. The other <c>com.atproto.admin</c> and <c>com.atproto.temp</c> gap methods are covered
/// by <see cref="ATProtoNet.Tests.Lexicon.EndpointRequestTests"/>.
/// </summary>
public sealed class ComAtProtoGapTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new();

    public ComAtProtoGapTests() => _fixture.Fallback("{}");

    public void Dispose() => _fixture.Dispose();

    private HttpStub.RecordedRequest Last => _fixture.Last;

    // ──────────────────────────────────────────────────────────
    //  com.atproto.repo.importRepo
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportRepoAsync_PostsTheCarWithItsLength()
    {
        byte[] car = [0x3a, 0xa2, 0x65, 0x72, 0x6f, 0x6f, 0x74, 0x73];
        using var stream = new MemoryStream(car);

        await _fixture.Client.Repo.ImportRepoAsync(stream);

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

        await _fixture.Client.Repo.ImportRepoAsync(stream);

        Assert.Equal(2, Last.ContentLength);
        Assert.Equal([0x01, 0x02], Last.Body);
    }

    [Fact]
    public async Task ImportRepoAsync_NonSeekableStream_IsSentChunked()
    {
        using var stream = new NonSeekableStream([0x01, 0x02, 0x03]);

        await _fixture.Client.Repo.ImportRepoAsync(stream);

        Assert.Null(Last.ContentLength);
        Assert.Equal([0x01, 0x02, 0x03], Last.Body);
    }

    [Fact]
    public async Task ImportRepoAsync_Rejected_ThrowsXrpcException()
    {
        _fixture.Fallback(_ => HttpStub.JsonResponse(
            """{"error":"InvalidRequest","message":"Service is not accepting repo imports"}""", HttpStatusCode.BadRequest));
        using var stream = new MemoryStream([0x01]);

        var ex = await Assert.ThrowsAsync<ATProtoNet.Http.XrpcException>(() => _fixture.Client.Repo.ImportRepoAsync(stream));

        Assert.Equal("InvalidRequest", ex.Error);
    }

    // com.atproto.admin.searchAccounts and .updateAccountSigningKey are covered by
    // ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    // ──────────────────────────────────────────────────────────
    //  com.atproto.temp
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHandleAvailabilityAsync_Available_ReadsTheResult()
    {
        _fixture.Fallback("""
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultAvailable"}}
            """);

        var response = await _fixture.Client.Temp.CheckHandleAvailabilityAsync(
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
        _fixture.Fallback("""
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultUnavailable",
              "suggestions":[{"handle":"alice1.bsky.social","method":"append-number"},{"handle":"alice-x.bsky.social","method":"append-word"}]}}
            """);

        var response = await _fixture.Client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        Assert.False(response.IsAvailable);
        var unavailable = Assert.IsType<HandleUnavailable>(response.Result);
        Assert.Equal(["alice1.bsky.social", "alice-x.bsky.social"], unavailable.Suggestions.Select(s => s.Handle.Value));
        Assert.Equal("append-number", unavailable.Suggestions[0].Method);
        Assert.Equal("/xrpc/com.atproto.temp.checkHandleAvailability?handle=alice.bsky.social", $"{Last.Path}?{Last.Query}");
    }

    [Fact]
    public async Task CheckHandleAvailabilityAsync_UnknownResult_IsKeptRaw()
    {
        _fixture.Fallback("""
            {"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultReserved","until":"2027"}}
            """);

        var response = await _fixture.Client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        var unknown = Assert.IsType<UnknownHandleAvailabilityResult>(response.Result);
        Assert.Equal("com.atproto.temp.checkHandleAvailability#resultReserved", unknown.Type);
        Assert.Equal("2027", unknown.Raw.GetProperty("until").GetString());
        Assert.False(response.IsAvailable);
    }

    // checkSignupQueue, dereferenceScope, requestPhoneVerification and revokeAccountCredentials
    // are covered by ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    /// <summary>A stream that cannot report its length, like a download being passed straight on.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
