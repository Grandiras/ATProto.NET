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
        Assert.IsType<EfCoreSpaceAuthorityStore<SpaceDbContext>>(
            provider.GetRequiredService<ISpaceAuthorityStore>());
        Assert.IsType<SimpleSpaceAccessPolicy>(provider.GetRequiredService<ISpaceAccessPolicy>());
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
