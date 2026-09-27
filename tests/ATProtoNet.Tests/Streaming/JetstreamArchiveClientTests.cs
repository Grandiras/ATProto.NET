using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamArchiveClientTests
{
    private const string PlanSnapshot = "network.bsky.jetstream.planSnapshot";
    private const string ListSegments = "network.bsky.jetstream.listSegments";
    private const string GetSegment = "network.bsky.jetstream.getSegment";
    private const string GetBlock = "network.bsky.jetstream.getBlock";

    private static Func<HttpStub.RecordedRequest, HttpResponseMessage> Json(object payload) =>
        _ => HttpStub.JsonResponse(JsonSerializer.Serialize(payload));

    private static Func<HttpStub.RecordedRequest, HttpResponseMessage> Bytes(byte[] payload) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    private static Func<HttpStub.RecordedRequest, HttpResponseMessage> Error(HttpStatusCode status, string error, TimeSpan? retryAfter = null) =>
        _ =>
        {
            var response = HttpStub.JsonResponse($$"""{"error":"{{error}}"}""", status);
            if (retryAfter is { } delay)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
            return response;
        };

    private static JetstreamArchiveClient Create(HttpStub handler, string? apiKey = "test-key")
        => new(JetstreamEndpoints.UsEast, apiKey, new HttpClient(handler)) { MaxRetryAttempts = 2, MaxRetryDelay = TimeSpan.Zero };

    [Fact]
    public async Task PlanSnapshotAsync_PostsTheFilterAndParsesThePlan()
    {
        var handler = new HttpStub().On(PlanSnapshot, Json(new
        {
            plannedThroughSeq = 400,
            sealedTipSeq = 900,
            segments = new object[]
            {
                new { name = "seg_0000000001.jss", index = 1, checksum = "0123456789abcdef",
                      minSeq = 100, maxSeq = 200, mode = "segment" },
                new { name = "seg_0000000002.jss", index = 2, checksum = "fedcba9876543210",
                      minSeq = 201, maxSeq = 400, mode = "blocks",
                      blocks = new[] { new { first = 3, last = 5 } } },
            },
            stats = new { segmentsExamined = 9, segmentsMatched = 2, blocksMatched = 3, entries = 4 },
        }));

        var plan = await Create(handler).PlanSnapshotAsync(new JetstreamSnapshotRequest
        {
            Collections = ["app.bsky.feed.*"],
            Kinds = ["commit"],
            AfterSeq = 99,
        });

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("/xrpc/network.bsky.jetstream.planSnapshot", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("Bearer test-key", handler.Requests[0].Headers.Authorization!.ToString());

        var body = JsonDocument.Parse(handler.Requests[0].BodyText).RootElement;
        Assert.Equal("app.bsky.feed.*", body.GetProperty("collections")[0].GetString());
        Assert.Equal(99, body.GetProperty("afterSeq").GetInt64());
        // An unset bound must be omitted, not sent as null — the server validates the shape.
        Assert.False(body.TryGetProperty("beforeSeq", out _));

        Assert.Equal(400, plan.PlannedThroughSeq);
        Assert.Equal(900, plan.SealedTipSeq);
        Assert.Equal(JetstreamSegmentDownloadMode.Segment, plan.Segments[0].DownloadMode);
        Assert.Equal(JetstreamSegmentDownloadMode.Blocks, plan.Segments[1].DownloadMode);
        Assert.Equal(new JetstreamBlockRange(3, 5), plan.Segments[1].Blocks![0]);
    }

    [Fact]
    public async Task PlannedSegment_AnUnknownModeFallsBackToWholeSegmentDownload()
    {
        var handler = new HttpStub().On(PlanSnapshot, Json(new
        {
            plannedThroughSeq = 1,
            sealedTipSeq = 1,
            segments = new object[]
            {
                new { name = "seg.jss", index = 0, checksum = "0123456789abcdef",
                      minSeq = 0, maxSeq = 1, mode = "something-new" },
            },
        }));

        var plan = await Create(handler).PlanSnapshotAsync(new JetstreamSnapshotRequest());

        Assert.Equal(JetstreamSegmentDownloadMode.Segment, plan.Segments[0].DownloadMode);
    }

    // EnumerateSegmentsAsync's cursor and repeated-cursor stop: the enumerator theory in Http/PaginationTests.

    [Fact]
    public async Task ListSegmentsAsync_ReadsEachSegmentsFields()
    {
        var handler = new HttpStub().On(ListSegments, Json(new
        {
            cursor = "page-2",
            segments = new[]
            {
                new
                {
                    name = "seg_0.jss", index = 0, sizeBytes = 1234, checksum = "0123456789abcdef", eventCount = 4096,
                    minSeq = 1, maxSeq = 2, minWitnessedAt = 3, maxWitnessedAt = 4,
                },
            },
        }));

        var page = await Create(handler).ListSegmentsAsync(limit: 1);

        var segment = Assert.Single(page.Segments);
        Assert.Equal(
            ("seg_0.jss", 1234L, 4096L, 1L, 2L, 3L, 4L),
            (segment.Name, segment.SizeBytes, segment.EventCount, segment.MinSeq, segment.MaxSeq, segment.MinWitnessedAt, segment.MaxWitnessedAt));
        Assert.Equal("page-2", page.Cursor);
        Assert.Equal("limit=1", handler.Last.Query);
    }

    [Fact]
    public async Task GetBlockAsync_RequestsTheSegmentAndBlockIndex()
    {
        var handler = new HttpStub().On(GetBlock, Bytes([1, 2, 3]));

        var frame = await Create(handler).GetBlockAsync("seg_0000000002.jss", 7);

        Assert.Equal([1, 2, 3], frame);
        var query = handler.Requests[0].Uri.Query;
        Assert.Contains("segment=seg_0000000002.jss", query);
        Assert.Contains("blockIndex=7", query);
    }

    [Fact]
    public async Task GetSegmentAsync_SendsARangeHeaderWhenResuming()
    {
        var handler = new HttpStub().On(GetSegment, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([9, 9]),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"0123456789abcdef\"");
            return response;
        });

        using var download = await Create(handler).GetSegmentAsync("seg_0.jss", rangeStart: 1024);

        Assert.Equal("bytes=1024-", handler.Requests[0].Headers.Range!.ToString());
        Assert.True(download.IsPartial);
        Assert.Equal("0123456789abcdef", download.ETag);
    }

    [Fact]
    public async Task DownloadSegmentAsync_ResumesFromTheByteOffsetItStoppedAt()
    {
        // Running out of metered quota mid-download closes the stream cleanly; the bytes already
        // received are intact and are not re-charged when the rest is fetched with a Range.
        var handler = new HttpStub()
            .On(GetSegment, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream([1, 2, 3])),
            })
            .On(GetSegment, _ => new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([4, 5]),
            });

        using var destination = new MemoryStream();
        var written = await Create(handler).DownloadSegmentAsync("seg_0.jss", destination);

        Assert.Equal(5, written);
        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
        Assert.Null(handler.Requests[0].Headers.Range);
        Assert.Equal("bytes=3-", handler.Requests[1].Headers.Range!.ToString());
    }

    [Fact]
    public async Task DownloadSegmentAsync_RestartsWhenTheServerIgnoresTheRange()
    {
        // A proxy that drops the Range header answers 200 with the whole file; appending it to
        // what we already have would produce a segment with a duplicated prefix.
        var handler = new HttpStub()
            .On(GetSegment, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream([1, 2, 3])),
            })
            .On(GetSegment, Bytes([1, 2, 3, 4, 5]));

        using var destination = new MemoryStream();
        var written = await Create(handler).DownloadSegmentAsync("seg_0.jss", destination);

        Assert.Equal(5, written);
        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
    }

    [Fact]
    public async Task ByteQuotaExhaustion_IsRetriedAfterTheRequestedDelay()
    {
        var handler = new HttpStub()
            .On(GetBlock, Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30)))
            .On(GetBlock, Bytes([7]));

        var frame = await Create(handler).GetBlockAsync("seg_0.jss", 0);

        Assert.Equal([7], frame);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ByteQuotaExhaustion_SurfacesTheRetryAfterOnceAttemptsRunOut()
    {
        var handler = new HttpStub()
            .On(GetBlock, Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30)))
            .On(GetBlock, Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30)))
            .On(GetBlock, Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30)));

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => Create(handler).GetBlockAsync("seg_0.jss", 0));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("byte limit exceeded", ex.Error);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task AnInvalidBearerCredential_IsNotRetried()
    {
        var handler = new HttpStub()
            .On(GetBlock, Error(HttpStatusCode.Unauthorized, "invalid bearer credential"))
            .On(GetBlock, Bytes([7]));

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => Create(handler).GetBlockAsync("seg_0.jss", 0));

        Assert.Equal(401, ex.StatusCode);
        Assert.False(ex.IsRetryable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SegmentNotFound_IsReportedWithItsErrorName()
    {
        var handler = new HttpStub().On(GetBlock, Error(HttpStatusCode.BadRequest, "SegmentNotFound"));

        var ex = await Assert.ThrowsAsync<JetstreamException>(
            () => Create(handler).GetBlockAsync("missing.jss", 0));

        Assert.Equal("SegmentNotFound", ex.Error);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task NoApiKey_SendsNoAuthorizationHeader()
    {
        var handler = new HttpStub().On(GetBlock, Bytes([1]));

        await Create(handler, apiKey: null).GetBlockAsync("seg_0.jss", 0);

        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task ArchiveHostIsDerivedFromTheWebSocketUrl()
    {
        var handler = new HttpStub().On(GetBlock, Bytes([1]));

        await Create(handler).GetBlockAsync("seg_0.jss", 0);

        Assert.Equal("https://jetstream.us-east.bsky.network", handler.Requests[0].Uri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>A response body that dies part-way through, the way a cut-off download does.</summary>
    private sealed class FailingStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= prefix.Length)
                throw new IOException("connection reset");

            var take = Math.Min(count, prefix.Length - _position);
            Array.Copy(prefix, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
