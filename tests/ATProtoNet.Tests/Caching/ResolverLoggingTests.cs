using System.Collections.Concurrent;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ATProtoNet.Tests.Caching;

/// <summary>
/// What the two caching resolvers log over their shared core: each keeps its own messages and
/// structured property names, which dashboards and alerts key on.
/// </summary>
public class ResolverLoggingTests
{
    private static readonly Did Alice = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Nsid Post = Nsid.Parse("com.example.post");

    private readonly FakeTimeProvider _clock = new();
    private readonly StructuredLogger _logger = new();

    [Fact]
    public async Task CachingDidResolver_DistributedCacheFails_WarnsUnderItsDidProperty()
    {
        var inner = Substitute.For<IDidResolver>();
        inner.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns(new DidDocument { Id = Alice });
        var distributed = Substitute.For<IDistributedCache>();
        distributed.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]?>(new IOException("redis down")));
        using var cache = new CachingDidResolver(inner, new DidCacheOptions(), distributed, _clock, _logger);

        await cache.ResolveAsync(Alice, TestContext.Current.CancellationToken);

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("Could not read {Did} from the distributed DID document cache.", warning.Format);
        Assert.Equal(Alice, warning.Properties["Did"]);
    }

    [Fact]
    public async Task CachingDidResolver_FailedRefreshOfACachedDocument_LogsNothing()
    {
        var inner = Substitute.For<IDidResolver>();
        inner.ResolveAsync(Alice, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new DidDocument { Id = Alice }),
            Task.FromException<DidDocument>(new DidResolutionException("down", DidResolutionErrorKind.NetworkError, Alice)));
        using var cache = new CachingDidResolver(inner, new DidCacheOptions(), null, _clock, _logger);
        await cache.ResolveAsync(Alice, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<DidResolutionException>(() => cache.RefreshAsync(Alice, TestContext.Current.CancellationToken));

        // The cached document is still served; a refresh that fails is the caller's to report.
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task CachingLexiconResolver_FailedRefreshOfACachedSchema_LogsUnderItsNsidProperty()
    {
        var inner = Substitute.For<ILexiconResolver>();
        inner.ResolveAsync(Post, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ResolvedLexicon
            {
                Uri = AtUri.Parse($"at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/com.atproto.lexicon.schema/{Post}"),
                Cid = Cid.Parse("bafyreibleucvt34j2gzwyzpamdn4vcqvwoheqhqdhn57qqivqkgfjwvfdy"),
                Schema = new LexiconSchemaRecord { Lexicon = 1, Id = Post },
            }),
            Task.FromException<ResolvedLexicon>(new LexiconResolutionException("gone", Post, LexiconResolutionErrorKind.NotFound)));
        using var cache = new CachingLexiconResolver(inner, new LexiconCacheOptions(), _clock, _logger);
        await cache.ResolveAsync(Post, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(6));

        await cache.ResolveAsync(Post, TestContext.Current.CancellationToken); // stale: refreshed in the background

        var entry = await _logger.WaitForAsync(e => e.Level == LogLevel.Information);
        Assert.Equal("Refreshing Lexicon {Nsid} failed; serving the cached schema.", entry.Format);
        Assert.Equal(Post, entry.Properties["Nsid"]);
    }

    private sealed record Entry(LogLevel Level, string? Format, IReadOnlyDictionary<string, object?> Properties);

    private sealed class StructuredLogger : ILogger
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            var map = properties.ToDictionary(p => p.Key, p => p.Value);
            Entries.Enqueue(new Entry(logLevel, map.GetValueOrDefault("{OriginalFormat}") as string, map));
        }

        public async Task<Entry> WaitForAsync(Func<Entry, bool> match)
        {
            for (var i = 0; i < 500; i++)
            {
                if (Entries.FirstOrDefault(match) is { } entry)
                    return entry;
                await Task.Delay(10);
            }

            throw new TimeoutException("The expected log entry never came.");
        }
    }
}
