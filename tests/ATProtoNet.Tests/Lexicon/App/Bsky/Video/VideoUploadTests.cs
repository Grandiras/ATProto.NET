using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.App.Bsky.Video;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;
using static ATProtoNet.Tests.Lexicon.App.Bsky.ScriptedXrpcHandler;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Video;

/// <summary>
/// The multipart video endpoints, and <see cref="VideoClient.UploadVideoAsync"/> driving them
/// against a scripted video service.
/// </summary>
public sealed class VideoUploadTests : IDisposable
{
    private const string Start = "app.bsky.video.startUpload";
    private const string Part = "app.bsky.video.uploadPart";
    private const string Finish = "app.bsky.video.finishUpload";
    private const string Status = "app.bsky.video.getJobStatus";
    private const string Abort = "app.bsky.video.abortUpload";

    private static readonly VideoUploadOptions NoWaiting = new()
    {
        RetryDelay = TimeSpan.Zero,
        PollInterval = TimeSpan.Zero,
    };

    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public VideoUploadTests() => _client = _handler.CreateClient();

    private VideoClient Video => _client.Bsky.Video;

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    // ──────────────────────────────────────────────────────────
    //  Endpoints
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StartUploadAsync_PostsSizeTypeAndAdvisoryFields()
    {
        _handler.On(Start, """{"jobId":"job-1","partSizeBytes":8388608,"partCount":3,"expiresAt":"2026-09-25T13:00:00.000Z"}""");

        var session = await Video.StartUploadAsync(
            20_000_000, "video/mp4", name: "clip.mp4", durationMs: 61_000, width: 1920, height: 1080);

        Assert.Equal(
            """{"sizeBytes":20000000,"mimeType":"video/mp4","name":"clip.mp4","durationMs":61000,"width":1920,"height":1080}""",
            Assert.Single(_handler.Requests).BodyText);
        Assert.Equal("job-1", session.JobId);
        Assert.Equal(8_388_608, session.PartSizeBytes);
        Assert.Equal(3, session.PartCount);
        Assert.Equal("2026-09-25T13:00:00.000Z", session.ExpiresAt.ToString());
    }

    [Fact]
    public async Task UploadPartAsync_SendsOctetStreamWithJobAndPartNumber()
    {
        _handler.On(Part, """{"partNumber":2,"sizeBytes":4}""");

        var stored = await Video.UploadPartAsync("job-1", 2, new MemoryStream([1, 2, 3, 4]));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("jobId=job-1&partNumber=2", request.Query);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(4, request.ContentLength);
        Assert.Equal([1, 2, 3, 4], request.Body);
        Assert.Equal(2, stored.PartNumber);
        Assert.Equal(4, stored.SizeBytes);
    }

    [Fact]
    public async Task FinishUploadAsync_BindsTheProcessingJob()
    {
        _handler.On(Finish, $$$"""{"completedJobId":"job-9","jobStatus":{"jobId":"job-9","did":"{{{AliceDid}}}","state":"JOB_STATE_ENCODING","progress":10}}""");

        var finished = await Video.FinishUploadAsync("job-1");

        Assert.Equal("""{"jobId":"job-1"}""", Assert.Single(_handler.Requests).BodyText);
        Assert.Equal("job-9", finished.CompletedJobId);
        Assert.Equal(JobState.Encoding, finished.JobStatus.State);
        Assert.Equal(10, finished.JobStatus.Progress);
    }

    [Fact]
    public async Task GetUploadStatusAsync_BindsTheSession()
    {
        _handler.On("app.bsky.video.getUploadStatus", """{"jobId":"job-1","partSizeBytes":5242880,"partCount":3,"receivedParts":[1,3],"expiresAt":"2026-09-25T13:00:00.000Z","state":"failed","failureReason":"parts missing at expiry"}""");

        var status = await Video.GetUploadStatusAsync("job-1");

        Assert.Equal("jobId=job-1", Assert.Single(_handler.Requests).Query);
        Assert.Equal(HttpMethod.Get, _handler.Requests[0].Method);
        Assert.Equal([1, 3], status.ReceivedParts);
        Assert.Equal(UploadState.Failed, status.State);
        Assert.Equal("parts missing at expiry", status.FailureReason);
        Assert.Null(status.JobStatus);
    }

    [Fact]
    public async Task AbortUploadAsync_BindsTheOutcome()
    {
        _handler.On(Abort, """{"state":"completed","completedJobId":"job-9"}""");

        var outcome = await Video.AbortUploadAsync("job-1");

        Assert.Equal("""{"jobId":"job-1"}""", Assert.Single(_handler.Requests).BodyText);
        Assert.Equal(UploadState.Completed, outcome.State);
        Assert.Equal("job-9", outcome.CompletedJobId);
    }

    [Fact]
    public async Task UploadVideoInOneRequestAsync_SendsTheWholeVideo()
    {
        _handler.On("app.bsky.video.uploadVideo", $$$"""{"jobStatus":{"jobId":"job-1","did":"{{{AliceDid}}}","state":"JOB_STATE_CREATED"}}""");

        var response = await Video.UploadVideoInOneRequestAsync(new MemoryStream([9, 9, 9]));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("video/mp4", request.ContentType);
        Assert.Equal([9, 9, 9], request.Body);
        Assert.Equal(JobState.Created, response.JobStatus.State);
    }

    // ──────────────────────────────────────────────────────────
    //  UploadVideoAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadVideoAsync_SplitsIntoPlannedParts_AndWaitsForTheBlob()
    {
        var video = Bytes(10);
        ScriptSession(partSize: 4, partCount: 3);
        _handler.On(Part, r => JsonResponse($$"""{"partNumber":{{r.Parameters["partNumber"]}},"sizeBytes":{{r.Body.Length}}}"""));
        _handler.On(Finish, JobJson("JOB_STATE_ENCODING", progress: 0));
        _handler.On(Status, JobJson("JOB_STATE_SCANNING", progress: 50));
        _handler.On(Status, JobJson("JOB_STATE_COMPLETED"));

        var job = await Video.UploadVideoAsync(new MemoryStream(video), "video/mp4", NoWaiting with { FileName = "a.mp4" });

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("video/mp4", job.Blob!.MimeType);

        var start = Assert.Single(_handler.To(Start));
        Assert.Equal("""{"sizeBytes":10,"mimeType":"video/mp4","name":"a.mp4"}""", start.BodyText);

        var parts = _handler.To(Part).ToList();
        Assert.Equal(["1", "2", "3"], parts.Select(p => p.Parameters["partNumber"]));
        Assert.All(parts, p => Assert.Equal("job-1", p.Parameters["jobId"]));
        Assert.All(parts, p => Assert.Equal("application/octet-stream", p.ContentType));
        Assert.Equal([4L, 4L, 2L], parts.Select(p => p.ContentLength!.Value));
        Assert.Equal(video, parts.SelectMany(p => p.Body));

        Assert.Equal("""{"jobId":"job-1"}""", Assert.Single(_handler.To(Finish)).BodyText);
        Assert.Equal(["jobId=job-9", "jobId=job-9"], _handler.To(Status).Select(r => r.Query));
        Assert.Empty(_handler.To(Abort));
    }

    [Fact]
    public async Task UploadVideoAsync_JobAlreadyCompleteOnFinish_DoesNotPoll()
    {
        ScriptSession(partSize: 16, partCount: 1);
        _handler.On(Part, """{"partNumber":1,"sizeBytes":5}""");
        _handler.On(Finish, JobJson("JOB_STATE_COMPLETED"));

        var job = await Video.UploadVideoAsync(new MemoryStream(Bytes(5)), options: NoWaiting);

        Assert.NotNull(job.Blob);
        Assert.Empty(_handler.To(Status));
        Assert.Equal(5, Assert.Single(_handler.To(Part)).ContentLength);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceOverloaded")]
    [InlineData(HttpStatusCode.InternalServerError, "InternalServerError")]
    [InlineData(HttpStatusCode.BadGateway, "UpstreamFailure")]
    public async Task UploadVideoAsync_TransientPartFailure_ResendsThePart(HttpStatusCode status, string error)
    {
        ScriptSession(partSize: 3, partCount: 2);
        _handler.On(Part, _ => JsonResponse("""{"partNumber":1,"sizeBytes":3}"""));
        _handler.On(Part, _ => ErrorResponse(status, error));
        _handler.On(Part, _ => JsonResponse("""{"partNumber":2,"sizeBytes":3}"""));
        _handler.On(Finish, JobJson("JOB_STATE_COMPLETED"));
        var video = Bytes(6);

        await Video.UploadVideoAsync(new MemoryStream(video), options: NoWaiting);

        var parts = _handler.To(Part).ToList();
        Assert.Equal(["1", "2", "2"], parts.Select(p => p.Parameters["partNumber"]));
        Assert.Equal(parts[1].Body, parts[2].Body);
        Assert.Equal(video[3..], parts[2].Body);
        Assert.Empty(_handler.To(Abort));
    }

    [Fact]
    public async Task UploadVideoAsync_PartKeepsFailing_GivesUpAfterMaxAttempts_AndAborts()
    {
        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, _ => ErrorResponse(HttpStatusCode.ServiceUnavailable, VideoErrors.ServiceOverloaded));
        _handler.On(Abort, """{"state":"aborted"}""");

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting with { MaxAttempts = 3 }));

        Assert.True(ex.Is(VideoErrors.ServiceOverloaded));
        Assert.Equal(3, _handler.To(Part).Count());
        Assert.Equal("""{"jobId":"job-1"}""", Assert.Single(_handler.To(Abort)).BodyText);
        Assert.Empty(_handler.To(Finish));
    }

    [Theory]
    [InlineData(VideoErrors.UploadExpired)]
    [InlineData(VideoErrors.PartSizeMismatch)]
    [InlineData(VideoErrors.InvalidPartNumber)]
    [InlineData(VideoErrors.UploadAborted)]
    public async Task UploadVideoAsync_PermanentPartError_ThrowsWithoutRetry_AndAborts(string error)
    {
        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, _ => ErrorResponse(HttpStatusCode.BadRequest, error));
        _handler.On(Abort, """{"state":"expired"}""");

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting));

        Assert.True(ex.Is(error));
        Assert.Single(_handler.To(Part));
        Assert.Single(_handler.To(Abort));
    }

    [Theory]
    [InlineData(JobFailureCode.ValidationFailure)]
    [InlineData(JobFailureCode.EncodingFailure)]
    [InlineData(JobFailureCode.PdsUploadFailure)]
    [InlineData(JobFailureCode.PdsUploadUnsupportedBlobSize)]
    [InlineData(JobFailureCode.GenericFailure)]
    public async Task UploadVideoAsync_ProcessingFails_ThrowsWithTheFailureCode(string failureCode)
    {
        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, """{"partNumber":1,"sizeBytes":8}""");
        _handler.On(Finish, JobJson("JOB_STATE_ENCODING"));
        _handler.On(Status, $$$"""{"jobStatus":{"jobId":"job-9","did":"{{{AliceDid}}}","state":"JOB_STATE_FAILED","error":"Video processing failed","failureCode":"{{{failureCode}}}","message":"The video could not be processed."}}""");

        var ex = await Assert.ThrowsAsync<VideoUploadException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting));

        Assert.Equal(failureCode, ex.FailureCode);
        Assert.Equal(JobState.Failed, ex.JobStatus!.State);
        Assert.Contains(failureCode, ex.Message, StringComparison.Ordinal);
        Assert.Contains("The video could not be processed.", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<AtProtoException>(ex);

        // The upload itself succeeded, so there is nothing to abort.
        Assert.Empty(_handler.To(Abort));
    }

    [Fact]
    public async Task UploadVideoAsync_FinishNotReady_RetriesTheFinish()
    {
        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, """{"partNumber":1,"sizeBytes":8}""");
        _handler.On(Finish, _ => ErrorResponse(HttpStatusCode.BadRequest, VideoErrors.UploadNotReady));
        _handler.On(Finish, JobJson("JOB_STATE_COMPLETED"));

        await Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting);

        Assert.Equal(2, _handler.To(Finish).Count());
    }

    [Fact]
    public async Task UploadVideoAsync_StartFailsWithAServerError_IsNotResent()
    {
        // A lost startUpload response may still have opened a session; resending would open a second.
        _handler.On(Start, _ => ErrorResponse(HttpStatusCode.InternalServerError, "InternalServerError"));

        await Assert.ThrowsAsync<XrpcException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting));

        Assert.Single(_handler.To(Start));
        Assert.Empty(_handler.To(Abort));
    }

    [Fact]
    public async Task UploadVideoAsync_StartRefusedAsOverloaded_IsResent()
    {
        _handler.On(Start, _ => ErrorResponse(HttpStatusCode.ServiceUnavailable, VideoErrors.ServiceOverloaded));
        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, """{"partNumber":1,"sizeBytes":8}""");
        _handler.On(Finish, JobJson("JOB_STATE_COMPLETED"));

        await Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting);

        Assert.Equal(2, _handler.To(Start).Count());
    }

    [Fact]
    public async Task UploadVideoAsync_StartRefusedAsTooLarge_Throws()
    {
        _handler.On(Start, _ => ErrorResponse(HttpStatusCode.BadRequest, VideoErrors.VideoTooLarge, "Video exceeds 300 MB"));

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting));

        Assert.True(ex.Is(VideoErrors.VideoTooLarge));
        Assert.Empty(_handler.To(Part));
    }

    [Fact]
    public async Task UploadVideoAsync_NonSeekableStreamWithLength_IsUploaded()
    {
        var video = Bytes(7);
        ScriptSession(partSize: 4, partCount: 2);
        _handler.On(Part, r => JsonResponse($$"""{"partNumber":{{r.Parameters["partNumber"]}},"sizeBytes":{{r.Body.Length}}}"""));
        _handler.On(Finish, JobJson("JOB_STATE_COMPLETED"));

        await Video.UploadVideoAsync(new ForwardOnlyStream(video), options: NoWaiting with { Length = 7 });

        Assert.Equal(video, _handler.To(Part).SelectMany(p => p.Body));
        Assert.Contains("\"sizeBytes\":7", Assert.Single(_handler.To(Start)).BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadVideoAsync_NonSeekableStreamWithoutLength_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Video.UploadVideoAsync(new ForwardOnlyStream(Bytes(7)), options: NoWaiting));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UploadVideoAsync_StreamShorterThanDeclared_Throws_AndAborts()
    {
        ScriptSession(partSize: 4, partCount: 3);
        _handler.On(Part, """{"partNumber":1,"sizeBytes":4}""");
        _handler.On(Abort, """{"state":"aborted"}""");

        await Assert.ThrowsAsync<ArgumentException>(
            () => Video.UploadVideoAsync(new ForwardOnlyStream(Bytes(6)), options: NoWaiting with { Length = 10 }));

        Assert.Single(_handler.To(Part));
        Assert.Single(_handler.To(Abort));
    }

    [Fact]
    public async Task UploadVideoAsync_PlanDoesNotFitTheSize_Throws_AndAborts()
    {
        ScriptSession(partSize: 4, partCount: 2);
        _handler.On(Abort, """{"state":"aborted"}""");

        await Assert.ThrowsAsync<VideoUploadException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(10)), options: NoWaiting));

        Assert.Empty(_handler.To(Part));
        Assert.Single(_handler.To(Abort));
    }

    [Fact]
    public async Task UploadVideoAsync_CancelledDuringParts_AbortsTheSession()
    {
        using var cts = new CancellationTokenSource();
        ScriptSession(partSize: 4, partCount: 3);
        _handler.On(Part, _ =>
        {
            cts.Cancel();
            return JsonResponse("""{"partNumber":1,"sizeBytes":4}""");
        });
        _handler.On(Abort, """{"state":"aborted"}""");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(12)), options: NoWaiting, cancellationToken: cts.Token));

        Assert.Single(_handler.To(Part));
        Assert.Single(_handler.To(Abort));
        Assert.Empty(_handler.To(Finish));
    }

    [Fact]
    public async Task UploadVideoAsync_AbortFails_SurfacesTheOriginalError()
    {
        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, _ => ErrorResponse(HttpStatusCode.BadRequest, VideoErrors.UploadExpired));
        _handler.On(Abort, _ => ErrorResponse(HttpStatusCode.BadRequest, VideoErrors.UploadNotFound));

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(8)), options: NoWaiting));

        Assert.True(ex.Is(VideoErrors.UploadExpired));
    }

    [Fact]
    public async Task UploadVideoAsync_BacksOffExponentially()
    {
        var time = new ManualTime();
        using var http = new HttpClient(_handler, disposeHandler: false);
        var video = new VideoClient(new XrpcClient(http, new Uri("https://pds.example.com/")) { TimeProvider = time });

        ScriptSession(partSize: 8, partCount: 1);
        _handler.On(Part, _ => ErrorResponse(HttpStatusCode.ServiceUnavailable, VideoErrors.ServiceOverloaded));
        _handler.On(Part, _ => ErrorResponse(HttpStatusCode.ServiceUnavailable, VideoErrors.ServiceOverloaded));
        _handler.On(Part, _ => ErrorResponse(HttpStatusCode.ServiceUnavailable, VideoErrors.ServiceOverloaded));
        _handler.On(Part, """{"partNumber":1,"sizeBytes":8}""");
        _handler.On(Finish, JobJson("JOB_STATE_COMPLETED"));

        await video.UploadVideoAsync(
            new MemoryStream(Bytes(8)),
            options: new VideoUploadOptions { RetryDelay = TimeSpan.FromMilliseconds(250) });

        Assert.Equal(
            [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)],
            time.Delays);
    }

    [Fact]
    public async Task UploadVideoAsync_InvalidOptions_Throw()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(1)), options: new VideoUploadOptions { MaxAttempts = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Video.UploadVideoAsync(new MemoryStream(Bytes(1)), options: new VideoUploadOptions { PollInterval = TimeSpan.FromSeconds(-1) }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Video.UploadVideoAsync(new MemoryStream(), options: NoWaiting));

        Assert.Empty(_handler.Requests);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private void ScriptSession(long partSize, int partCount) =>
        _handler.On(Start, $$"""{"jobId":"job-1","partSizeBytes":{{partSize}},"partCount":{{partCount}},"expiresAt":"2026-09-25T13:00:00.000Z"}""");

    private static string JobJson(string state, int? progress = null)
    {
        var progressJson = progress is { } p ? $",\"progress\":{p}" : "";
        var blob = state == "JOB_STATE_COMPLETED"
            ? $$""","blob":{"$type":"blob","ref":{"$link":"bafkreifj76geu7wghvlvcbob6piob6hxfo3jvkshxhkyv3ght3n7zb75ey"},"mimeType":"video/mp4","size":123}"""
            : "";
        return $$$"""{"completedJobId":"job-9","jobStatus":{"jobId":"job-9","did":"{{{AliceDid}}}","state":"{{{state}}}"{{{progressJson}}}{{{blob}}}}}""";
    }

    private static byte[] Bytes(int count) => Enumerable.Range(1, count).Select(i => (byte)i).ToArray();

    /// <summary>A readable stream that cannot seek, like a network stream.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A clock whose timers fire at once, recording each delay asked for.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                Delays.Add(dueTime);
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }

            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
