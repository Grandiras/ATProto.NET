using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering the EF Core-backed AT Protocol token store.
/// </summary>
public static class AtProtoTokenStoreExtensions
{
    /// <summary>
    /// Registers an EF Core-backed <see cref="IAtProtoTokenStore"/> that stores OAuth tokens
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
    /// <item><description><see cref="IAtProtoTokenStore"/> backed by EF Core</description></item>
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
    /// // Register the EF Core token store
    /// builder.Services.AddAtProtoEfCoreTokenStore&lt;AtProtoTokenDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProtoEfCoreTokenStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddDataProtection();
        services.AddSingleton<IAtProtoTokenStore, EfCoreAtProtoTokenStore<TContext>>();

        return services;
    }
}
