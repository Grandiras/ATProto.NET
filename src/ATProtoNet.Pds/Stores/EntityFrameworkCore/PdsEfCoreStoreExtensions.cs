using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering the EF Core-backed PDS stores.
/// </summary>
public static class PdsEfCoreStoreExtensions
{
    /// <summary>
    /// Registers EF Core-backed <see cref="IAccountStore"/>, <see cref="IRepoStore"/>, and
    /// <see cref="IRepoCommitStore"/> implementations, replacing the in-memory defaults
    /// registered by <see cref="PdsHostingExtensions.AddAtProtoPds"/>.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> containing <see cref="DbSet{TEntity}"/>s for
    /// <see cref="PdsAccountEntity"/>, <see cref="PdsRecordEntity"/>,
    /// <see cref="PdsBlobEntity"/>, <see cref="PdsBlobRefEntity"/>, and
    /// <see cref="PdsRepoHeadEntity"/>. Use <see cref="PdsDbContext"/> or configure the
    /// entities in your own context via <see cref="PdsDbContext.ConfigurePdsModel"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional store configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>You must separately register <typeparamref name="TContext"/> and its
    /// <see cref="IDbContextFactory{TContext}"/> — the stores resolve a fresh context per
    /// operation, so they are safe as singletons alongside a scoped DbContext.</para>
    /// <para>Call order does not matter: this method removes any previously registered
    /// store, and <see cref="PdsHostingExtensions.AddAtProtoPds"/> only fills in the
    /// in-memory defaults when nothing else is registered. That includes the head store
    /// from <c>AddAtProtoPds&lt;TAccountStore, TRepoStore, TCommitStore&gt;()</c>, which
    /// this call replaces as well — register it after this one if you want to keep it.</para>
    /// <para>Calling this method twice replaces everything the first call registered,
    /// including the options: the last call wins outright rather than leaving the new stores
    /// wired to the first call's configuration.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddDbContextFactory&lt;PdsDbContext&gt;(options =>
    ///     options.UseNpgsql(builder.Configuration.GetConnectionString("Pds")));
    ///
    /// builder.Services.AddAtProtoPds(options => options.Hostname = "pds.example.com");
    /// builder.Services.AddAtProtoPdsEfCoreStores&lt;PdsDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProtoPdsEfCoreStores<TContext>(
        this IServiceCollection services,
        Action<PdsEfCoreStoreOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PdsEfCoreStoreOptions();
        configure?.Invoke(options);

        // Replace, not TryAdd: a second call re-registers the stores unconditionally, so the
        // options it was given have to replace the earlier instance too — otherwise the new
        // stores would resolve the first call's configuration and this one would be dropped.
        services.RemoveAll<PdsEfCoreStoreOptions>();
        services.AddSingleton(options);

        services.RemoveAll<IAccountStore>();
        services.RemoveAll<IRepoStore>();
        services.RemoveAll<IRepoCommitStore>();

        // Close over `options` rather than resolving it, so these stores use the configuration
        // from the call that registered them even if a later call replaces the registration.
        services.AddSingleton<IAccountStore>(sp => new EfCoreAccountStore<TContext>(
            sp.GetRequiredService<IDbContextFactory<TContext>>(),
            options));

        services.AddSingleton<IRepoStore>(sp => new EfCoreRepoStore<TContext>(
            sp.GetRequiredService<IDbContextFactory<TContext>>()));

        services.AddSingleton<IRepoCommitStore>(sp => new EfCoreRepoCommitStore<TContext>(
            sp.GetRequiredService<IDbContextFactory<TContext>>()));

        return services;
    }
}
