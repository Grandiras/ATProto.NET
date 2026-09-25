using ATProtoNet.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering the EF Core-backed AT Protocol session store.
/// </summary>
public static class AtProtoTokenStoreExtensions
{
    /// <summary>
    /// Registers an EF Core-backed <see cref="IAtProtoSessionStore"/> that stores sessions
    /// in a relational database with encryption at rest.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> that contains a <see cref="DbSet{TEntity}"/>
    /// for <see cref="AtProtoTokenEntity"/>. Use <see cref="AtProtoTokenDbContext"/>
    /// or configure the entity in your own context via
    /// <see cref="AtProtoTokenDbContext.ConfigureAtProtoTokenModel"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>This method registers:</para>
    /// <list type="bullet">
    /// <item><description><see cref="IAtProtoSessionStore"/> backed by EF Core</description></item>
    /// <item><description>Data Protection (required for token encryption)</description></item>
    /// </list>
    /// <para>You must separately register the <typeparamref name="TContext"/> DbContext
    /// and its <see cref="IDbContextFactory{TContext}"/> before calling this method.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register your DbContext with a factory
    /// builder.Services.AddDbContextFactory&lt;AtProtoTokenDbContext&gt;(options =>
    ///     options.UseSqlite("Data Source=tokens.db"));
    ///
    /// // Register the EF Core session store
    /// builder.Services.AddAtProtoEfCoreSessionStore&lt;AtProtoTokenDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProtoEfCoreSessionStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddDataProtection();
        services.AddSingleton<IAtProtoSessionStore, EfCoreAtProtoSessionStore<TContext>>();

        return services;
    }
}
