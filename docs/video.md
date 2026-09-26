# Video Upload & Processing

ATProto.NET supports video upload and processing via the `app.bsky.video.*` endpoints. Access it through `client.Bsky.Video`.

A video may be up to **300 MB**. Bluesky's video service also enforces a daily allowance of videos
and bytes per account; `GetUploadLimitsAsync` reports what is left.

## Upload a Video

`UploadVideoAsync` uploads a stream in parts, waits for the service to process the video, and
returns the finished job. Its `Blob` goes into a `VideoEmbed`:

```csharp
await using var video = File.OpenRead("my-video.mp4");

var job = await client.Bsky.Video.UploadVideoAsync(video, "video/mp4");

await client.Bsky.PostAsync("Check out this video!", new PostOptions
{
    Embed = new VideoEmbed
    {
        Video = job.Blob!,
        AspectRatio = new AspectRatio { Width = 1920, Height = 1080 },
        Alt = "A description of the video for accessibility",
    },
});
```

What it does:

1. **Start** (`app.bsky.video.startUpload`): declares the exact size and MIME type. The service
   answers with a job ID, the part size and the part count, and rejects a video that is too large
   (`VideoTooLarge`), of an unsupported type, or over the daily allowance before anything is sent.
2. **Parts** (`app.bsky.video.uploadPart`): sends each part as `application/octet-stream`, with the
   job ID and part number as query parameters. One part is held in memory at a time.
3. **Finish** (`app.bsky.video.finishUpload`): hands the assembled video to a processing job. When
   the service recognizes a video it has already processed, the job it returns is that video's.
4. **Wait** (`app.bsky.video.getJobStatus`): polls the job until it completes or fails.

Parts are idempotent, so a transient failure (a lost connection, a timeout, a 5xx or 429 response,
or `ServiceOverloaded`) is retried with exponential backoff. Any other error is thrown as an
`XrpcException`; match it with the `VideoErrors` constants. When the upload fails or is cancelled
before it is finished, the session is aborted, which releases its share of the daily allowance.

If processing fails, `UploadVideoAsync` throws a `VideoUploadException` whose `FailureCode` says
why:

```csharp
try
{
    var job = await client.Bsky.Video.UploadVideoAsync(video, "video/mp4");
}
catch (XrpcException ex) when (ex.Is(VideoErrors.VideoTooLarge))
{
    Console.WriteLine("Videos are limited to 300 MB.");
}
catch (VideoUploadException ex) when (ex.FailureCode == JobFailureCode.ValidationFailure)
{
    Console.WriteLine($"Not a usable video: {ex.JobStatus?.Message}");
}
```

| `JobFailureCode` | Meaning |
|------------------|---------|
| `ValidationFailure` | The upload is not a valid video, or breaks a limit (length, aspect ratio, …) |
| `EncodingFailure` | The video could not be encoded |
| `PdsUploadFailure` | The encoded video could not be uploaded to the account's PDS |
| `PdsUploadUnsupportedBlobSize` | The account's PDS does not accept a blob of the video's size |
| `GenericFailure` | Any other failure |

### Options

`VideoUploadOptions` tunes the upload:

```csharp
var job = await client.Bsky.Video.UploadVideoAsync(stream, "video/mp4", new VideoUploadOptions
{
    Length = contentLength,                 // required for a stream that cannot seek
    FileName = "holiday.mp4",
    Duration = TimeSpan.FromSeconds(95),    // advisory: lets the service reject early
    Width = 1920,
    Height = 1080,
    MaxAttempts = 4,                        // per request, for transient failures
    RetryDelay = TimeSpan.FromSeconds(1),   // doubles after each failure, up to 30 s
    PollInterval = TimeSpan.FromSeconds(1),
});
```

A seekable stream is uploaded from its current position to its end. For a stream that cannot seek,
such as a network stream, pass its exact `Length`.

Cancelling the token before the upload is finished aborts the session. Cancelling while the video
is processing only stops waiting; the job carries on. To keep the job ID across a restart, use the
endpoints below.

## The Multipart Endpoints

The steps are available on their own, for resumable uploads or progress reporting:

```csharp
var session = await client.Bsky.Video.StartUploadAsync(sizeBytes, "video/mp4", name: "clip.mp4");

for (var part = 1; part <= session.PartCount; part++)
{
    // Each part is PartSizeBytes long, except the last, which holds the rest.
    await using var chunk = OpenPart(part);
    await client.Bsky.Video.UploadPartAsync(session.JobId, part, chunk);
}

var finished = await client.Bsky.Video.FinishUploadAsync(session.JobId);
var jobId = finished.CompletedJobId; // poll this one
```

- `UploadPartAsync` needs the part's exact length, so pass a seekable stream (the service checks
  `Content-Length`). Parts may be sent in any order and resent.
- `GetUploadStatusAsync(jobId)` reports the session's `State` (see `UploadState`: `created`,
  `finishing`, `completed`, `failed`, `aborted`, `expired`), the `ReceivedParts`, and when it
  `ExpiresAt`.
- `AbortUploadAsync(jobId)` ends an open session. A session that already ended reports its outcome
  instead.

## One-Request Upload

`UploadVideoInOneRequestAsync` sends the whole video in a single request
(`app.bsky.video.uploadVideo`) and returns the job without waiting for it; poll it yourself:

```csharp
await using var video = File.OpenRead("my-video.mp4");
var upload = await client.Bsky.Video.UploadVideoInOneRequestAsync(video, "video/mp4");

JobStatus status = upload.JobStatus;
while (status.State is not (JobState.Completed or JobState.Failed))
{
    await Task.Delay(TimeSpan.FromSeconds(2));
    status = (await client.Bsky.Video.GetJobStatusAsync(status.JobId)).JobStatus;
}
```

### Job States

`JobStatus.State` is a string; the well-known values are constants on `JobState`:

| Constant | Wire value | Description |
|----------|------------|-------------|
| `JobState.Created` | `JOB_STATE_CREATED` | Job created, not yet processing |
| `JobState.Encoding` | `JOB_STATE_ENCODING` | Video is being encoded |
| `JobState.Encoded` | `JOB_STATE_ENCODED` | Encoding finished |
| `JobState.Scanning` | `JOB_STATE_SCANNING` | Video is being scanned |
| `JobState.Scanned` | `JOB_STATE_SCANNED` | Scanning finished |
| `JobState.Uploading` | `JOB_STATE_UPLOADING` | The blob is being uploaded to the PDS |
| `JobState.Uploaded` | `JOB_STATE_UPLOADED` | The blob is on the PDS |
| `JobState.Completed` | `JOB_STATE_COMPLETED` | Processing complete, blob ready |
| `JobState.Failed` | `JOB_STATE_FAILED` | Processing failed; see `FailureCode` |

The server may report states beyond these, so treat "not completed and not failed" as still in
progress rather than matching the intermediate states exhaustively.

## Check Upload Limits

Before uploading, check the current upload limits and remaining capacity:

```csharp
var limits = await client.Bsky.Video.GetUploadLimitsAsync();

Console.WriteLine($"Can upload: {limits.CanUpload}");
Console.WriteLine($"Remaining daily bytes: {limits.RemainingDailyBytes}");
Console.WriteLine($"Remaining daily videos: {limits.RemainingDailyVideos}");
```

## Next Steps

- [Blob Upload](blob-upload.md) — Upload images and other files
- [API Reference](api-reference.md) — Complete VideoClient methods
