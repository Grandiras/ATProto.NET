using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// The contract every <see cref="IJtiReplayStore"/> must satisfy, plus the sweep behaviour
/// specific to holding entries in memory with a background scan.
/// </summary>
public class InMemoryJtiReplayStoreTests : JtiReplayStoreContractTests
{
    protected override IJtiReplayStore CreateStore() => new InMemoryJtiReplayStore();

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
public class JtiReplayStoreRegistrationTests : IDisposable
{
    // These tests only check which store type DI resolves; they never touch the database. One
    // connection held open for the whole test class is enough to keep the shared-cache database
    // alive across the several `AddDbContextFactory` registrations built from it.
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection =
        new($"Data Source=jti-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    public JtiReplayStoreRegistrationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

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

    private ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<JtiReplayDbContext>(options => options.UseSqlite(_connection));

        return services;
    }
}
