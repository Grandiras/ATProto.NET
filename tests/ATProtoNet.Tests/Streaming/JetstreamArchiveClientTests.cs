using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamArchiveClientTests
{
    /// <summary>Serves a scripted queue of responses and records what was asked for.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        public ScriptedHandler Json(object payload, HttpStatusCode status = HttpStatusCode.OK)
            => Respond(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            });

        public ScriptedHandler Bytes(byte[] payload, HttpStatusCode status = HttpStatusCode.OK)
            => Respond(_ => new HttpResponseMessage(status) { Content = new ByteArrayContent(payload) });

        public ScriptedHandler Error(HttpStatusCode status, string error, TimeSpan? retryAfter = null)
            => Respond(_ =>
            {
                var response = new HttpResponseMessage(status)
                {
                    Content = new StringContent($$"""{"error":"{{error}}"}""", Encoding.UTF8, "application/json"),
                };
                if (retryAfter is { } delay)
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
                return response;
            });

        public ScriptedHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _responses.Enqueue(factory);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return _responses.Count > 0
                ? _responses.Dequeue()(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
    }

    private static JetstreamArchiveClient Create(ScriptedHandler handler, string? apiKey = "test-key")
        => new(JetstreamEndpoints.UsEast, apiKey, new HttpClient(handler)) { MaxRetryAttempts = 2, MaxRetryDelay = TimeSpan.Zero };

    [Fact]
    public async Task PlanSnapshotAsync_PostsTheFilterAndParsesThePlan()
    {
        var handler = new ScriptedHandler().Json(new
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
        });

        var plan = await Create(handler).PlanSnapshotAsync(new JetstreamSnapshotRequest
        {
            Collections = ["app.bsky.feed.*"],
            Kinds = ["commit"],
            AfterSeq = 99,
        });

        Assert.Equal("POST", handler.Requests[0].Method.Method);
        Assert.EndsWith("/xrpc/network.bsky.jetstream.planSnapshot", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer test-key", handler.Requests[0].Headers.Authorization!.ToString());

        var body = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("app.bsky.feed.*", body.GetProperty("collections")[0].GetString());
        Assert.Equal(99, body.GetProperty("afterSeq").GetInt64());
        // An unset bound must be omitted, not sent as null — the server validates the shape.
        Assert.False(body.TryGetProperty("beforeSeq", out _));

        Assert.Equal(400, plan.PlannedThroughSeq);
        Assert.Equal(900, plan.SealedTipSeq);
        Assert.Equal(JetstreamSegmentDownloadMode.Segment, plan.Segments[0].DownloadMode);
        Assert.Equal(JetstreamSegmentDownloadMode.Blocks, plan.Segments[1].DownloadMode);
        Assert.Equal(new JetstreamBlockRange(3, 5), plan.Segments[1].Blocks![0]);
        Assert.Equal(3, plan.Stats!.BlocksMatched);
    }

    [Fact]
    public async Task PlannedSegment_AnUnknownModeFallsBackToWholeSegmentDownload()
    {
        var handler = new ScriptedHandler().Json(new
        {
            plannedThroughSeq = 1,
            sealedTipSeq = 1,
            segments = new object[]
            {
                new { name = "seg.jss", index = 0, checksum = "0123456789abcdef",
                      minSeq = 0, maxSeq = 1, mode = "something-new" },
            },
        });

        var plan = await Create(handler).PlanSnapshotAsync(new JetstreamSnapshotRequest());

        Assert.Equal(JetstreamSegmentDownloadMode.Segment, plan.Segments[0].DownloadMode);
    }

    [Fact]
    public async Task ListAllSegmentsAsync_FollowsThePaginationCursor()
    {
        var handler = new ScriptedHandler()
            .Json(new
            {
                cursor = "page-2",
                segments = new[] { Segment("seg_0.jss", 0) },
            })
            .Json(new
            {
                segments = new[] { Segment("seg_1.jss", 1) },
            });

        var segments = new List<JetstreamSegmentInfo>();
        await foreach (var segment in Create(handler).ListAllSegmentsAsync(pageSize: 1))
            segments.Add(segment);

        Assert.Equal(["seg_0.jss", "seg_1.jss"], segments.Select(s => s.Name));
        Assert.Contains("limit=1", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("cursor=page-2", handler.Requests[1].RequestUri!.Query);
        Assert.Equal(4096, segments[0].EventCount);

        static object Segment(string name, int index) => new
        {
            name,
            index,
            sizeBytes = 1234,
            checksum = "0123456789abcdef",
            eventCount = 4096,
            minSeq = 1,
            maxSeq = 2,
            minWitnessedAt = 3,
            maxWitnessedAt = 4,
        };
    }

    [Fact]
    public async Task GetBlockAsync_RequestsTheSegmentAndBlockIndex()
    {
        var handler = new ScriptedHandler().Bytes([1, 2, 3]);

        var frame = await Create(handler).GetBlockAsync("seg_0000000002.jss", 7);

        Assert.Equal([1, 2, 3], frame);
        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("segment=seg_0000000002.jss", query);
        Assert.Contains("blockIndex=7", query);
    }

    [Fact]
    public async Task GetSegmentAsync_SendsARangeHeaderWhenResuming()
    {
        var handler = new ScriptedHandler().Respond(_ =>
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
        var handler = new ScriptedHandler()
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream([1, 2, 3])),
            })
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.PartialContent)
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
        var handler = new ScriptedHandler()
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream([1, 2, 3])),
            })
            .Bytes([1, 2, 3, 4, 5]);

        using var destination = new MemoryStream();
        var written = await Create(handler).DownloadSegmentAsync("seg_0.jss", destination);

        Assert.Equal(5, written);
        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
    }

    [Fact]
    public async Task ByteQuotaExhaustion_IsRetriedAfterTheRequestedDelay()
    {
        var handler = new ScriptedHandler()
            .Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30))
            .Bytes([7]);

        var frame = await Create(handler).GetBlockAsync("seg_0.jss", 0);

        Assert.Equal([7], frame);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ByteQuotaExhaustion_SurfacesTheRetryAfterOnceAttemptsRunOut()
    {
        var handler = new ScriptedHandler()
            .Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30))
            .Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30))
            .Error(HttpStatusCode.TooManyRequests, "byte limit exceeded", TimeSpan.FromSeconds(30));

        var ex = await Assert.ThrowsAsync<JetstreamArchiveException>(
            () => Create(handler).GetBlockAsync("seg_0.jss", 0));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("byte limit exceeded", ex.Error);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task AnInvalidBearerCredential_IsNotRetried()
    {
        var handler = new ScriptedHandler()
            .Error(HttpStatusCode.Unauthorized, "invalid bearer credential")
            .Bytes([7]);

        var ex = await Assert.ThrowsAsync<JetstreamArchiveException>(
            () => Create(handler).GetBlockAsync("seg_0.jss", 0));

        Assert.Equal(401, ex.StatusCode);
        Assert.False(ex.IsRetryable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SegmentNotFound_IsReportedWithItsErrorName()
    {
        var handler = new ScriptedHandler().Error(HttpStatusCode.BadRequest, "SegmentNotFound");

        var ex = await Assert.ThrowsAsync<JetstreamArchiveException>(
            () => Create(handler).GetBlockAsync("missing.jss", 0));

        Assert.Equal("SegmentNotFound", ex.Error);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task NoApiKey_SendsNoAuthorizationHeader()
    {
        var handler = new ScriptedHandler().Bytes([1]);

        await Create(handler, apiKey: null).GetBlockAsync("seg_0.jss", 0);

        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public void ArchiveHostIsDerivedFromTheWebSocketUrl()
        => Assert.Equal("https://jetstream.us-east.bsky.network/",
            JetstreamArchiveClient.ToHttpUrl(JetstreamEndpoints.UsEast));

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
