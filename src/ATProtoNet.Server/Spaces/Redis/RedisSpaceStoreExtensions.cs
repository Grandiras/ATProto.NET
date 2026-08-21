using ATProtoNet.Server.Spaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace ATProtoNet.Server.Redis;

/// <summary>
/// Registers the Redis-backed space server stores.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddSingleton&lt;IConnectionMultiplexer&gt;(
///     _ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("redis")!));
///
/// builder.Services
///     .AddAtProtoSpaces(options => { /* … */ })
///     .AddAtProtoRedisSpaceReplayStore();
/// </code>
/// </example>
public static class RedisSpaceStoreExtensions
{
    /// <summary>
    /// Replaces the in-process <see cref="ISpaceReplayStore"/> with a Redis-backed one, taking
    /// the connection from <see cref="IConnectionMultiplexer"/> in the container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="keyPrefix">
    /// The prefix every key is written under. Defaults to
    /// <see cref="RedisSpaceReplayStore.DefaultKeyPrefix"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Register the <see cref="IConnectionMultiplexer"/> yourself — as a singleton, which is how
    /// StackExchange.Redis is meant to be held — or use the overload taking a database factory.
    /// </remarks>
    public static IServiceCollection AddAtProtoRedisSpaceReplayStore(
        this IServiceCollection services, string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddAtProtoRedisSpaceReplayStore(
            sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase(), keyPrefix);
    }

    /// <summary>
    /// Replaces the in-process <see cref="ISpaceReplayStore"/> with a Redis-backed one over a
    /// database the caller resolves.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databaseFactory">Resolves the Redis database to write to.</param>
    /// <param name="keyPrefix">The prefix every key is written under.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoRedisSpaceReplayStore(
        this IServiceCollection services, Func<IServiceProvider, IDatabase> databaseFactory, string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseFactory);

        // Replace rather than TryAdd: AddAtProtoSpaces() may already have registered the
        // in-process default, and this call is the deployment saying it wants the shared one.
        services.Replace(ServiceDescriptor.Singleton<ISpaceReplayStore>(sp => new RedisSpaceReplayStore(
            databaseFactory(sp),
            keyPrefix,
            sp.GetService<TimeProvider>())));

        return services;
    }
}
