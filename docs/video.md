# Video Upload & Processing

ATProto.NET supports video upload and processing via the `app.bsky.video.*` endpoints. Access it through `client.Bsky.Video`.

## Upload a Video

`UploadVideoAsync` takes a stream:

```csharp
await using var video = File.OpenRead("my-video.mp4");

var response = await client.Bsky.Video.UploadVideoAsync(video, "video/mp4");

Console.WriteLine($"Job ID: {response.JobStatus.JobId}");
Console.WriteLine($"State: {response.JobStatus.State}");
```

## Check Job Status

Video processing is asynchronous. After uploading, poll the job status:

```csharp
var status = await client.Bsky.Video.GetJobStatusAsync(jobId: response.JobStatus.JobId);

Console.WriteLine($"State: {status.JobStatus.State}");
Console.WriteLine($"Progress: {status.JobStatus.Progress}");
```

### Job States

`JobStatus.State` is a string; the well-known values are constants on `JobState`:

| Constant | Wire value | Description |
|----------|------------|-------------|
| `JobState.Created` | `JOB_STATE_CREATED` | Job created, not yet processing |
| `JobState.Encoding` | `JOB_STATE_ENCODING` | Video is being encoded |
| `JobState.Scanning` | `JOB_STATE_SCANNING` | Video is being scanned |
| `JobState.Completed` | `JOB_STATE_COMPLETED` | Processing complete, blob ready |
| `JobState.Failed` | `JOB_STATE_FAILED` | Processing failed |

The server may report states beyond these, so treat "not completed and not failed" as still in
progress rather than matching the intermediate states exhaustively.

## Wait for Processing

```csharp
await using var video = File.OpenRead("my-video.mp4");
var upload = await client.Bsky.Video.UploadVideoAsync(video, "video/mp4");
var jobId = upload.JobStatus.JobId;

// Poll until complete
JobStatus status;
do
{
    await Task.Delay(TimeSpan.FromSeconds(2));
    var result = await client.Bsky.Video.GetJobStatusAsync(jobId);
    status = result.JobStatus;
    Console.WriteLine($"Status: {status.State} ({status.Progress}%)");
}
while (status.State is not (JobState.Completed or JobState.Failed));

if (status.State == JobState.Completed && status.Blob is not null)
{
    // Use the blob reference in a post
    Console.WriteLine("Video ready!");
}
else
{
    Console.WriteLine($"Video processing failed: {status.Error}");
}
```

## Check Upload Limits

Before uploading, check the current upload limits and remaining capacity:

```csharp
var limits = await client.Bsky.Video.GetUploadLimitsAsync();

Console.WriteLine($"Can upload: {limits.CanUpload}");
Console.WriteLine($"Remaining daily bytes: {limits.RemainingDailyBytes}");
Console.WriteLine($"Remaining daily videos: {limits.RemainingDailyVideos}");
```

## Create a Post with Video

After the video is processed, use the blob reference to create a post:

```csharp
// Upload and wait for processing
await using var video = File.OpenRead("my-video.mp4");
var upload = await client.Bsky.Video.UploadVideoAsync(video, "video/mp4");

// ... poll for completion ...

// Create a post with the video embed
await client.PostAsync("Check out this video!", embed: new VideoEmbed
{
    Video = status.Blob!,
    AspectRatio = new AspectRatio { Width = 1920, Height = 1080 },
    Alt = "A description of the video for accessibility",
});
```

## Next Steps

- [Blob Upload](blob-upload.md) — Upload images and other files
- [API Reference](api-reference.md) — Complete VideoClient methods
