using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Authentication;

public class InMemoryJtiReplayStoreTests
{
    [Fact]
    public async Task TryConsumeAsync_FirstUse_Succeeds()
    {
        var store = new InMemoryJtiReplayStore();

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task TryConsumeAsync_SecondUse_Fails()
    {
        var store = new InMemoryJtiReplayStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsNotACollision()
    {
        // Entries are keyed on (issuer, jti, expiry): two issuers picking the same nonce are not
        // the same token, and one must not be able to burn the other's.
        var store = new InMemoryJtiReplayStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.True(await store.TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiredEntries_AreSweptOut()
    {
        var clock = new ManualClock();
        var store = new InMemoryJtiReplayStore(clock);

        await store.TryConsumeAsync("did:plc:a", "short", clock.GetUtcNow().AddSeconds(30));
        Assert.Equal(1, store.Count);

        // Past both the entry's expiry and the sweep interval. The sweep runs in the background,
        // off the consumption that found it due.
        clock.Advance(TimeSpan.FromMinutes(2));
        await store.TryConsumeAsync("did:plc:a", "fresh", clock.GetUtcNow().AddMinutes(1));
        await store.LastSweep;

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task TryConsumeAsync_EntryDueLaterWithinTheSecond_SurvivesTheSweep()
    {
        // The sweep at 60.2 s must drop the entry that expired at 10 s and keep the one that must
        // outlive 60.5 s, and the replay check runs only once that sweep has finished.
        var clock = new ManualClock();
        var start = clock.GetUtcNow();
        var store = new InMemoryJtiReplayStore(clock);
        await store.TryConsumeAsync("did:plc:a", "old", start.AddSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(60.2));
        var retainUntil = start.AddSeconds(60.5);

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", retainUntil));
        await store.LastSweep;

        Assert.Equal(1, store.Count);
        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", retainUntil));
    }

    [Fact]
    public async Task TryConsumeAsync_WhenASweepIsDue_DoesNotWaitForIt()
    {
        var clock = new ManualClock();
        var store = new InMemoryJtiReplayStore(clock);
        for (var i = 0; i < 1000; i++)
            await store.TryConsumeAsync("did:plc:a", $"n{i}", clock.GetUtcNow().AddSeconds(30));

        clock.Advance(TimeSpan.FromMinutes(2));
        var consumed = store.TryConsumeAsync("did:plc:a", "fresh", clock.GetUtcNow().AddMinutes(1));

        // The consumption completes synchronously; the scan is a separate task.
        Assert.True(consumed.IsCompletedSuccessfully);
        Assert.True(await consumed);
        await store.LastSweep;
        Assert.Equal(1, store.Count);
    }
}

/// <summary>
/// The shared store wins over the in-process default whichever way round the registrations go,
/// and one store serves service auth and the space server alike.
/// </summary>
public class JtiReplayStoreRegistrationTests
{
    [Fact]
    public void AddAtProtoEfCoreJtiReplayStore_AfterAddAtProtoServiceAuth_Wins()
    {
        var services = Services();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:example.com#svc"));
        services.AddAtProtoEfCoreJtiReplayStore<JtiReplayDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreJtiReplayStore<JtiReplayDbContext>>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreJtiReplayStore_BeforeAddAtProtoServiceAuth_Wins()
    {
        var services = Services();
        services.AddAtProtoEfCoreJtiReplayStore<JtiReplayDbContext>();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:example.com#svc"));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreJtiReplayStore<JtiReplayDbContext>>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void ServiceAuthAndSpaces_ShareOneStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtProtoSpaces();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:example.com#svc"));

        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IJtiReplayStore));
        Assert.IsType<InMemoryJtiReplayStore>(provider.GetRequiredService<IJtiReplayStore>());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<JtiReplayDbContext>(
            options => options.UseInMemoryDatabase($"registration-{Guid.NewGuid():N}"));

        return services;
    }
}
