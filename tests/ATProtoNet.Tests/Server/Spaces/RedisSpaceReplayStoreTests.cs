using ATProtoNet.Server.Redis;
using NSubstitute;
using StackExchange.Redis;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// What the Redis replay store asks Redis for. The behaviour that matters is a property of the
/// command it issues — <c>SET … NX</c> with the token's own lifetime as the TTL — so these
/// assert on the command rather than on a server's answer to it; a live server is exercised by
/// the gated integration test.
/// </summary>
public class RedisSpaceReplayStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-08-21T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task TryConsumeAsync_SetsTheKeyOnlyIfItDoesNotExist()
    {
        // NX is the whole replay check: it is what makes the test and the write one atomic step
        // that two instances cannot interleave with each other.
        var database = Substitute.For<IDatabase>();
        var call = CaptureSet(database, result: true);
        var store = new RedisSpaceReplayStore(database, timeProvider: new FakeClock(Now));

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", Now.AddSeconds(60)));

        Assert.Equal(When.NotExists, call.When);
    }

    [Fact]
    public async Task TryConsumeAsync_KeyAlreadyPresent_ReportsAReplay()
    {
        var database = Substitute.For<IDatabase>();
        CaptureSet(database, result: false);
        var store = new RedisSpaceReplayStore(database, timeProvider: new FakeClock(Now));

        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", Now.AddSeconds(60)));
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiresTheEntryWithTheToken()
    {
        // The TTL is what keeps the key space bounded without a sweep: the entry goes away as the
        // token it guards becomes rejectable on its own expiry.
        var database = Substitute.For<IDatabase>();
        var call = CaptureSet(database, result: true);
        var store = new RedisSpaceReplayStore(database, timeProvider: new FakeClock(Now));

        await store.TryConsumeAsync("did:plc:a", "nonce", Now.AddSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), call.Expiry);
    }

    [Fact]
    public async Task TryConsumeAsync_AlreadyExpiredToken_StillRecordsTheIdentifier()
    {
        // Redis refuses a non-positive TTL, and an entry that never gets written is an identifier
        // that could be spent twice. The caller rejects the token on its expiry regardless.
        var database = Substitute.For<IDatabase>();
        var call = CaptureSet(database, result: true);
        var store = new RedisSpaceReplayStore(database, timeProvider: new FakeClock(Now));

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", Now.AddSeconds(-60)));

        Assert.NotNull(call.Expiry);
        Assert.True(call.Expiry > TimeSpan.Zero);
    }

    [Fact]
    public async Task TryConsumeAsync_KeysOnIssuerNonceAndExpiry()
    {
        var database = Substitute.For<IDatabase>();
        var call = CaptureSet(database, result: true);
        var store = new RedisSpaceReplayStore(database, "test:", new FakeClock(Now));
        var expiry = Now.AddSeconds(60);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.Equal($"test:did:plc:a|nonce|{expiry.ToUnixTimeSeconds()}", call.Key.ToString());
    }

    [Fact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsADifferentKey()
    {
        var database = Substitute.For<IDatabase>();
        var call = CaptureSet(database, result: true);
        var store = new RedisSpaceReplayStore(database, timeProvider: new FakeClock(Now));

        await store.TryConsumeAsync("did:plc:a", "nonce", Now.AddSeconds(60));
        var first = call.Key.ToString();
        await store.TryConsumeAsync("did:plc:b", "nonce", Now.AddSeconds(60));

        Assert.NotEqual(first, call.Key.ToString());
    }

    /// <summary>
    /// Records the arguments of the <c>SET</c> the store issues, whichever overload the compiler
    /// bound it to.
    /// </summary>
    private static SetCall CaptureSet(IDatabase database, bool result)
    {
        var call = new SetCall();

        database.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), when: Arg.Any<When>())
            .Returns(info =>
            {
                var arguments = info.Args();
                call.Key = (RedisKey)arguments[0]!;
                call.Expiry = arguments.OfType<TimeSpan>().Cast<TimeSpan?>().FirstOrDefault();
                call.When = arguments.OfType<When>().First();
                return result;
            });

        return call;
    }

    private sealed class SetCall
    {
        public RedisKey Key { get; set; }
        public TimeSpan? Expiry { get; set; }
        public When When { get; set; }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
