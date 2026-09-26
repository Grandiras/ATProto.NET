using ATProtoNet.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server;

/// <summary>
/// Registers the SDK's identity resolvers with dependency injection.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDidResolver"/> (a <see cref="CachingDidResolver"/> over a
    /// <see cref="DidResolver"/>), <see cref="IHandleResolver"/> and <see cref="IIdentityResolver"/>
    /// as singletons, so every consumer shares one DID document cache.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="IdentityResolverOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>Registration is idempotent: a resolver already registered is kept, and the first
    /// call's options win. <c>AddAtProtoSpaces</c> calls this, so call it first to configure the
    /// resolvers a space server uses.</para>
    /// <para>With <see cref="DidCacheOptions.UseDistributedCache"/> set, the cache is backed by the
    /// registered <see cref="IDistributedCache"/>, which must then be registered as well.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost");
    /// builder.Services.AddAtProtoIdentity(o =>
    /// {
    ///     o.Cache.UseDistributedCache = true;
    ///     o.DnsOverHttpsUrl = new Uri("https://cloudflare-dns.com/dns-query");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProtoIdentity(
        this IServiceCollection services, Action<IdentityResolverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new IdentityResolverOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddSingleton<IDidResolver>(sp =>
        {
            var resolved = sp.GetRequiredService<IdentityResolverOptions>();
            return new CachingDidResolver(
                new DidResolver(resolved),
                resolved.Cache,
                resolved.Cache.UseDistributedCache ? sp.GetRequiredService<IDistributedCache>() : null,
                sp.GetService<TimeProvider>(),
                sp.GetService<ILogger<CachingDidResolver>>(),
                ownsInner: true);
        });

        services.TryAddSingleton<IHandleResolver>(sp => new HandleResolver(
            sp.GetRequiredService<IdentityResolverOptions>(),
            sp.GetService<ILogger<HandleResolver>>()));

        services.TryAddSingleton<IIdentityResolver>(sp => new IdentityResolver(
            sp.GetRequiredService<IDidResolver>(),
            sp.GetRequiredService<IHandleResolver>(),
            sp.GetService<ILogger<IdentityResolver>>()));

        return services;
    }
}
