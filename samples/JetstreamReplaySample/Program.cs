// Jetstream v2 archive sample — backfills history over the HTTP replay endpoints and cuts over
// into the live tail without a gap at the seam. See docs/jetstream.md for full documentation.
//
//   JETSTREAM_API_KEY=<key> dotnet run                 # replay: history, then live
//   JETSTREAM_API_KEY=<key> dotnet run -- --snapshot   # snapshot: history only, then stop
//
// The HTTP endpoints are metered and need an API key on the Bluesky-hosted instances; the live
// WebSocket tail is unauthenticated.

using ATProtoNet.Streaming;
using ZstdSharp;

var snapshotOnly = args.Contains("--snapshot");
var apiKey = Environment.GetEnvironmentVariable("JETSTREAM_API_KEY");

Console.WriteLine("ATProto.NET Jetstream Replay Sample");
Console.WriteLine("===================================");
Console.WriteLine($"Mode:        {(snapshotOnly ? "snapshot (archive only)" : "replay (archive, then live)")}");
Console.WriteLine($"Collections: app.bsky.feed.post");
Console.WriteLine($"API key:     {(string.IsNullOrEmpty(apiKey) ? "(none — only an unmetered instance will answer)" : "set")}");
Console.WriteLine("Press Ctrl+C to stop.\n");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var options = new JetstreamConsumerOptions
{
    ServiceUrl = JetstreamEndpoints.UsEast,
    Protocol = JetstreamProtocol.V2,          // the archive is v2-only
    WantedCollections = ["app.bsky.feed.post"],
    WantedKinds = [JetstreamEventKind.Commit],
    // A replay cursor is the same v2 sequence number the live tail persists, so one store spans
    // both phases and a restart resumes the backfill where it stopped.
    CursorStore = new InMemoryFirehoseCursorStore(),
    MaxReconnectAttempts = -1,
    Archive = new JetstreamArchiveOptions
    {
        ApiKey = apiKey,
        BlockDecompressor = new ZstdBlockDecompressor(),
        SnapshotOnly = snapshotOnly,
        DownloadParallelism = 4,
    },
};

using var consumer = new JetstreamReplayConsumer(options);
var archived = 0;
var live = 0;
var switched = false;

try
{
    await foreach (var evt in consumer.ReplayAsync(cancellationToken: cts.Token))
    {
        if (!consumer.IsBackfilling && !switched)
        {
            switched = true;
            Console.WriteLine($"\n--- caught up: {archived} archived event(s), now tailing live ---\n");
        }

        if (consumer.IsBackfilling)
            archived++;
        else
            live++;

        // Creates, updates, and deletes all arrive in sequence order: the stream is folded, not
        // filtered, so a record you will later delete shows up transiently first. Key writes on
        // the at:// URI and they stay idempotent.
        if (evt is JetstreamCommitEvent commit && (archived + live) % 500 == 0)
            Console.WriteLine($"[{commit.Cursor}] {commit.Operation} {commit.Uri}");
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C
}
catch (JetstreamArchiveException ex) when (ex.StatusCode == 401)
{
    Console.Error.WriteLine("The archive refused the API key. Set JETSTREAM_API_KEY to a valid key.");
}
catch (JetstreamConnectException ex)
{
    // The pinned tip aged out of the live socket's lookback window and re-planning could not
    // catch up; resume from the persisted cursor on the next run.
    Console.Error.WriteLine($"Cutover failed: {ex.Message}");
}

Console.WriteLine($"\nStopped at sequence {consumer.LastCursor}. " +
                  $"{archived} archived event(s), {live} live event(s).");

/// <summary>
/// Segment blocks are plain zstd frames with no dictionary — unlike the dictionary-compressed
/// WebSocket frames an <see cref="IJetstreamDecompressor"/> handles.
/// </summary>
internal sealed class ZstdBlockDecompressor : IJetstreamBlockDecompressor
{
    public byte[] Decompress(ReadOnlySpan<byte> frame)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(frame).ToArray();
    }
}
