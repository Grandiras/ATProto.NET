using ATProtoNet.Caching;

namespace ATProtoNet.Tests.Caching;

public class ResolutionCacheTests
{
    [Fact]
    public async Task GetAsync_FirstCallerOnAContextThatNeverRuns_DoesNotStallTheSharedFetch()
    {
        // A resolver that awaits without ConfigureAwait(false) resumes on whatever context it
        // started on. A shared fetch must not start on its first caller's: a caller that blocks, or
        // a context that stops pumping, would stall it for every caller that joins it.
        var cache = Create(async key =>
        {
            await Task.Delay(1);
            return key;
        });

        Task<string> first;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NeverRunsContext());
        try
        {
            first = cache.GetAsync("k", CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal("k", await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal("k", await cache.GetAsync("k", TestContext.Current.CancellationToken));
    }

    private static ResolutionCache<string, string, InvalidOperationException> Create(Func<string, Task<string>> fetch) =>
        new(
            fetch,
            static cached => new InvalidOperationException(cached.Message, cached),
            capacity: 10,
            failureCapacity: null,
            staleAfter: TimeSpan.FromMinutes(1),
            expireAfter: TimeSpan.FromMinutes(2),
            failureTtl: TimeSpan.FromMinutes(1),
            TimeProvider.System);

    // A context whose queue is never run, as a UI thread that is blocked on the call.
    private sealed class NeverRunsContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException();
    }
}
