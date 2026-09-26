using ATProtoNet.Streaming;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Streaming;

public class StreamCursorStoreTests
{
    [Fact]
    public async Task InMemory_GetCursor_ReturnsNull_WhenNotStored()
    {
        var store = new InMemoryStreamCursorStore();
        var result = await store.GetCursorAsync("stream1");
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemory_StoreThenGet_ReturnsCursor()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync("stream1", 42);
        var result = await store.GetCursorAsync("stream1");
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task InMemory_StoreOverwrites_PreviousValue()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync("stream1", 10);
        await store.StoreCursorAsync("stream1", 20);
        var result = await store.GetCursorAsync("stream1");
        Assert.Equal(20, result);
    }

    [Fact]
    public async Task InMemory_DifferentStreamIds_AreIndependent()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync("stream1", 100);
        await store.StoreCursorAsync("stream2", 200);

        Assert.Equal(100, await store.GetCursorAsync("stream1"));
        Assert.Equal(200, await store.GetCursorAsync("stream2"));
    }
}

public class CursorTrackerTests
{
    private static CursorTracker Tracker(IStreamCursorStore? store, int interval = 100) =>
        new(store, "stream", interval, NullLogger.Instance);

    [Fact]
    public void Advance_OnlyMovesForward()
    {
        var tracker = Tracker(store: null);
        tracker.Start(10);

        Assert.False(tracker.Advance(10));
        Assert.False(tracker.Advance(5));
        Assert.True(tracker.Advance(11));
        Assert.Equal(11, tracker.Current);
    }

    [Fact]
    public async Task FlushAsync_SavesTheFinalPosition()
    {
        var store = new InMemoryStreamCursorStore();
        var tracker = Tracker(store);
        tracker.Start(null);
        tracker.Advance(7);

        await tracker.FlushAsync();

        Assert.Equal(7, await store.GetCursorAsync("stream"));
    }

    [Fact]
    public async Task FlushAsync_NothingNew_DoesNotWrite()
    {
        var store = new RecordingStore();
        await store.StoreCursorAsync("stream", 5);
        var tracker = Tracker(store);
        tracker.Start(await tracker.LoadAsync(TestContext.Current.CancellationToken));

        await tracker.FlushAsync();

        Assert.Equal([5L], store.Writes);
    }

    [Fact]
    public async Task Advance_AtInterval_SavesInTheBackground()
    {
        var store = new RecordingStore();
        var tracker = Tracker(store, interval: 2);
        tracker.Start(null);

        tracker.Advance(1);
        tracker.Advance(2);
        await store.WaitForWritesAsync(1);

        Assert.Equal([2L], store.Writes);
    }

    [Fact]
    public async Task Advance_SlowStore_DoesNotBlockAndSavesTheNewestAfterward()
    {
        var store = new RecordingStore { Gate = new TaskCompletionSource() };
        var tracker = Tracker(store, interval: 1);
        tracker.Start(null);

        // The first save hangs on the gate; the moves after it return at once, and only the
        // newest of them is saved when the store frees up.
        tracker.Advance(1);
        await store.WaitForStartedAsync(1);
        tracker.Advance(2);
        tracker.Advance(3);
        Assert.Equal(3, tracker.Current);

        store.Gate.SetResult();
        await tracker.FlushAsync();

        Assert.Equal([1L, 3L], store.Writes);
    }

    [Fact]
    public async Task FlushAsync_FailingStore_DoesNotThrow()
    {
        var tracker = Tracker(new FailingStore());
        tracker.Start(null);
        tracker.Advance(1);

        await tracker.FlushAsync();
    }

    private sealed class RecordingStore : IStreamCursorStore
    {
        private readonly Lock _gate = new();
        private readonly List<long> _writes = [];
        private int _started;

        public TaskCompletionSource? Gate { get; init; }

        public IReadOnlyList<long> Writes
        {
            get
            {
                lock (_gate)
                    return [.. _writes];
            }
        }

        public ValueTask<long?> GetCursorAsync(string streamId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                return ValueTask.FromResult(_writes.Count > 0 ? _writes[^1] : (long?)null);
        }

        public async ValueTask StoreCursorAsync(string streamId, long cursor, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _started);
            if (Gate is not null)
                await Gate.Task;

            lock (_gate)
                _writes.Add(cursor);
        }

        public async Task WaitForStartedAsync(int count)
        {
            for (var i = 0; i < 500 && Volatile.Read(ref _started) < count; i++)
                await Task.Delay(10);
        }

        public async Task WaitForWritesAsync(int count)
        {
            for (var i = 0; i < 500 && Writes.Count < count; i++)
                await Task.Delay(10);
        }
    }

    private sealed class FailingStore : IStreamCursorStore
    {
        public ValueTask<long?> GetCursorAsync(string streamId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

        public ValueTask StoreCursorAsync(string streamId, long cursor, CancellationToken cancellationToken = default)
            => throw new IOException("disk full");
    }
}
