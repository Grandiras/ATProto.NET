using ATProtoNet.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>Extension methods for registering the EF Core-backed AT Protocol session store.</summary>
public static class AtProtoTokenStoreExtensions
{
    /// <summary>
    /// Stores sessions in a relational database, encrypted at rest with ASP.NET Core Data
    /// Protection (<see cref="EfCoreAtProtoSessionStore{TContext}"/>), instead of the in-memory
    /// default.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> that contains a <see cref="DbSet{TEntity}"/>
    /// for <see cref="AtProtoTokenEntity"/>. Use <see cref="AtProtoTokenDbContext"/>
    /// or configure the entity in your own context via
    /// <see cref="AtProtoTokenDbContext.ConfigureAtProtoTokenModel"/>.
    /// </typeparam>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <para>This also registers Data Protection. Register the <typeparamref name="TContext"/>'s
    /// <see cref="IDbContextFactory{TContext}"/> yourself (<c>AddDbContextFactory</c>): the store
    /// opens a context per operation.</para>
    /// <para>For several instances sharing the database, share the Data Protection key ring too,
    /// and give the instances a distributed <see cref="ISessionRefreshCoordinator"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddDbContextFactory&lt;AtProtoTokenDbContext&gt;(options =>
    ///     options.UseSqlite("Data Source=tokens.db"));
    ///
    /// builder.Services.AddAtProto()
    ///     .WithOAuth()
    ///     .WithClientFactory()
    ///     .WithEfCoreSessionStore&lt;AtProtoTokenDbContext&gt;();
    /// </code>
    /// </example>
    public static IAtProtoBuilder WithEfCoreSessionStore<TContext>(this IAtProtoBuilder builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDataProtection();
        return builder.WithSessionStore<EfCoreAtProtoSessionStore<TContext>>();
    }
}
