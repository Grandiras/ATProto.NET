using System.Net;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Temp;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.repo.importRepo</c>'s stream upload, and <c>checkHandleAvailability</c>'s union
/// result. The other <c>com.atproto.admin</c> and <c>com.atproto.temp</c> gap methods are rows in
/// <see cref="EndpointRequestTests"/>.
/// </summary>
public sealed class ComAtProtoGapTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new();

    public ComAtProtoGapTests() => _fixture.Fallback("{}");

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(0, true, 8L)]
    [InlineData(2, true, 6L)] // starts at the stream's position
    [InlineData(0, false, null)] // a stream that cannot report its length goes chunked
    public async Task ImportRepoAsync_PostsTheCarFromTheStreamsPosition(int position, bool seekable, long? contentLength)
    {
        byte[] car = [0x3a, 0xa2, 0x65, 0x72, 0x6f, 0x6f, 0x74, 0x73];
        using var stream = seekable ? new MemoryStream(car) : new NonSeekableStream(car);
        stream.Position = position;

        await _fixture.Client.Repo.ImportRepoAsync(stream);

        var request = _fixture.AssertPost("com.atproto.repo.importRepo");
        Assert.Equal("application/vnd.ipld.car", request.ContentType);
        Assert.Equal(contentLength, request.ContentLength);
        Assert.Equal(car[position..], request.Body);
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

    [Fact]
    public async Task CheckHandleAvailabilityAsync_SendsEveryParameter()
    {
        _fixture.Fallback("""{"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#resultAvailable"}}""");

        await _fixture.Client.Temp.CheckHandleAvailabilityAsync(
            Handle.Parse("alice.bsky.social"), "a@example.com", AtDatetime.Parse("1990-01-01T00:00:00.000Z"));

        _fixture.AssertGet(
            "com.atproto.temp.checkHandleAvailability",
            "handle=alice.bsky.social&email=a@example.com&birthDate=1990-01-01T00:00:00.000Z");
    }

    [Theory]
    [InlineData("resultAvailable", "", typeof(HandleAvailable), true)]
    [InlineData("resultUnavailable", ""","suggestions":[{"handle":"alice1.bsky.social","method":"append-number"}]""", typeof(HandleUnavailable), false)]
    [InlineData("resultReserved", ""","until":"2027" """, typeof(UnknownHandleAvailabilityResult), false)]
    public async Task CheckHandleAvailabilityAsync_ReadsTheResultVariant(string def, string fields, Type expected, bool available)
    {
        _fixture.Fallback(
            $$$"""{"handle":"alice.bsky.social","result":{"$type":"com.atproto.temp.checkHandleAvailability#{{{def}}}"{{{fields}}}}}""");

        var response = await _fixture.Client.Temp.CheckHandleAvailabilityAsync(Handle.Parse("alice.bsky.social"));

        Assert.IsType(expected, response.Result);
        Assert.Equal(available, response.IsAvailable);
        if (response.Result is HandleUnavailable unavailable)
            Assert.Equal(("alice1.bsky.social", "append-number"), (unavailable.Suggestions[0].Handle.Value, unavailable.Suggestions[0].Method));
    }

    /// <summary>A stream that cannot report its length, like a download being passed straight on.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
