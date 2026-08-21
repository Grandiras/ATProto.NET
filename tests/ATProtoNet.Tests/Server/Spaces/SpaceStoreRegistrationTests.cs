using ATProtoNet.Crypto;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Redis;
using ATProtoNet.Server.Spaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// Which store a container ends up with. <c>AddAtProtoSpaces()</c> registers the in-process
/// defaults with <c>TryAdd</c>, so a durable store has to win whichever way round the two calls
/// are made — the order they appear in <c>Program.cs</c> is not something a deployment should
/// have to get right.
/// </summary>
public class SpaceStoreRegistrationTests
{
    [Fact]
    public void AddAtProtoEfCoreSpaceReplayStore_AfterAddAtProtoSpaces_Wins()
    {
        var services = Services();
        services.AddAtProtoSpaces();
        services.AddAtProtoEfCoreSpaceReplayStore<SpaceDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreSpaceReplayStore<SpaceDbContext>>(provider.GetRequiredService<ISpaceReplayStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreSpaceReplayStore_BeforeAddAtProtoSpaces_Wins()
    {
        var services = Services();
        services.AddAtProtoEfCoreSpaceReplayStore<SpaceDbContext>();
        services.AddAtProtoSpaces();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreSpaceReplayStore<SpaceDbContext>>(provider.GetRequiredService<ISpaceReplayStore>());
    }

    [Fact]
    public void AddAtProtoRedisSpaceReplayStore_ReplacesTheInProcessDefault()
    {
        var services = Services();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddAtProtoSpaces();
        services.AddAtProtoRedisSpaceReplayStore();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RedisSpaceReplayStore>(provider.GetRequiredService<ISpaceReplayStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreSimpleSpace_RegistersTheStoreAndTheBaselinePolicy()
    {
        var services = Services();
        services.AddAtProtoSpaces(options => options.ServiceDid = "did:web:pds.example.com");
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
        services.AddAtProtoSpaces(options => options.ServiceDid = "did:web:pds.example.com");
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
        services.AddAtProtoSpaces(options => options.ServiceDid = "did:web:spaces.example.com");
        services.AddSpaceAuthority<InMemorySpaceAuthorityStore>(AtProtoCrypto.GenerateP256Key());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemorySpaceAuthorityStore>(provider.GetRequiredService<ISpaceAuthorityStore>());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<SpaceDbContext>(
            options => options.UseInMemoryDatabase($"registration-{Guid.NewGuid():N}"));

        return services;
    }
}
