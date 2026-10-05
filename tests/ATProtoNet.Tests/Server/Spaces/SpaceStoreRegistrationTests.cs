using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// Which store a container ends up with. <c>AddAtProtoSpaces()</c> registers the in-process
/// defaults with <c>TryAdd</c>, so a durable store has to win whichever way round the two calls
/// are made — the order they appear in <c>Program.cs</c> is not something a deployment should
/// have to get right.
/// </summary>
public class SpaceStoreRegistrationTests : IDisposable
{
    // These tests only check which store type DI resolves; they never touch the database. One
    // connection held open for the whole test class is enough to keep the shared-cache database
    // alive across the several `AddDbContextFactory` registrations built from it.
    private readonly SqliteConnection _connection = new($"Data Source=space-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    public SpaceStoreRegistrationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void AddAtProtoEfCoreJtiReplayStore_AfterAddAtProtoSpaces_Wins()
    {
        var services = Services();
        services.AddAtProtoSpaces();
        services.AddAtProtoEfCoreJtiReplayStore<SpaceDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreJtiReplayStore<SpaceDbContext>>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreJtiReplayStore_BeforeAddAtProtoSpaces_Wins()
    {
        var services = Services();
        services.AddAtProtoEfCoreJtiReplayStore<SpaceDbContext>();
        services.AddAtProtoSpaces();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreJtiReplayStore<SpaceDbContext>>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreSpaceCredentialRevocationStore_AfterAddAtProtoSpaces_Wins()
    {
        var services = Services();
        services.AddAtProtoSpaces();
        services.AddAtProtoEfCoreSpaceCredentialRevocationStore<SpaceDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreSpaceCredentialRevocationStore<SpaceDbContext>>(provider.GetRequiredService<ISpaceCredentialRevocationStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreSimpleSpace_RegistersTheStoreAndTheBaselinePolicy()
    {
        var services = Services();
        services.AddAtProtoSpaces(options => options.ServiceDid = Did.Parse("did:web:pds.example.com"));
        services.AddAtProtoEfCoreSpaceAuthority<SpaceDbContext>(AtProtoCrypto.GenerateP256Key());
        services.AddAtProtoEfCoreSimpleSpace<SpaceDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreSimpleSpaceStore<SpaceDbContext>>(provider.GetRequiredService<ISimpleSpaceStore>());
        Assert.IsType<SimpleSpaceAccessPolicy>(provider.GetRequiredService<ISpaceAccessPolicy>());

        // Bridged, so a space created through createSpace is one the authority answers for.
        var authority = Assert.IsType<SimpleSpaceAuthorityStore>(
            provider.GetRequiredService<ISpaceAuthorityStore>());
        Assert.IsType<EfCoreSpaceAuthorityStore<SpaceDbContext>>(authority.Inner);
    }

    [Fact]
    public void AddSimpleSpace_BeforeAddSpaceAuthority_StillBridgesTheAuthorityStore()
    {
        // The order the two calls appear in is not something a deployment should have to get
        // right, so which store answers "does this space exist" is decided when it is resolved.
        var services = Services();
        services.AddAtProtoSpaces(options => options.ServiceDid = Did.Parse("did:web:pds.example.com"));
        services.AddSimpleSpace<InMemorySimpleSpaceStore>();
        services.AddSpaceAuthority<InMemorySpaceAuthorityStore>(AtProtoCrypto.GenerateP256Key());

        using var provider = services.BuildServiceProvider();

        var authority = Assert.IsType<SimpleSpaceAuthorityStore>(
            provider.GetRequiredService<ISpaceAuthorityStore>());
        Assert.IsType<InMemorySpaceAuthorityStore>(authority.Inner);
    }

    [Fact]
    public void AddSpaceAuthority_WithoutSimpleSpace_LeavesTheStoreUnwrapped()
    {
        // A service running a bespoke space type declares its spaces to the authority store
        // itself; there is no second store to read existence from.
        var services = Services();
        services.AddAtProtoSpaces(options => options.ServiceDid = Did.Parse("did:web:spaces.example.com"));
        services.AddSpaceAuthority<InMemorySpaceAuthorityStore>(AtProtoCrypto.GenerateP256Key());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemorySpaceAuthorityStore>(provider.GetRequiredService<ISpaceAuthorityStore>());
    }

    private ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<SpaceDbContext>(options => options.UseSqlite(_connection));

        return services;
    }
}
