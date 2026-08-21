using ATProtoNet.Crypto;
using ATProtoNet.Server.Spaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// Registers the EF Core-backed space server stores.
/// </summary>
/// <remarks>
/// <para>Each of these replaces one of the in-process defaults
/// <see cref="SpaceServerExtensions.AddAtProtoSpaces"/> falls back to, and can be called in any
/// order relative to it.</para>
/// <para>Register the context and its <c>IDbContextFactory</c> yourself — the stores open a context per operation,
/// so a factory is what they need rather than a scoped instance.</para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddDbContextFactory&lt;SpaceDbContext&gt;(options =>
///     options.UseNpgsql(builder.Configuration.GetConnectionString("spaces")));
///
/// builder.Services
///     .AddAtProtoSpaces(options => { /* … */ })
///     .AddAtProtoEfCoreSpaceReplayStore&lt;SpaceDbContext&gt;()
///     .AddAtProtoEfCoreSpaceAuthority&lt;SpaceDbContext&gt;(credentialSigningKey)
///     .AddAtProtoEfCoreSimpleSpace&lt;SpaceDbContext&gt;();
/// </code>
/// </example>
public static class SpaceStoreExtensions
{
    /// <summary>
    /// Registers the space-authority endpoints over an EF Core-backed
    /// <see cref="ISpaceAuthorityStore"/>.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> configured with
    /// <see cref="SpaceDbContext.ConfigureSpaceAuthorityModel"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="signingKey">
    /// The key credentials are signed with — the one published in the authority's DID document.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoEfCoreSpaceAuthority<TContext>(
        this IServiceCollection services, AtProtoKey signingKey)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSpaceAuthority<EfCoreSpaceAuthorityStore<TContext>>(signingKey);
    }

    /// <summary>
    /// Registers the <c>com.atproto.simplespace</c> endpoints over an EF Core-backed
    /// <see cref="ISimpleSpaceStore"/>.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> configured with
    /// <see cref="SpaceDbContext.ConfigureSimpleSpaceModel"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// A member list is never published to the network, so this — or another durable store — is
    /// what keeps a space's access control across a restart.
    /// </remarks>
    public static IServiceCollection AddAtProtoEfCoreSimpleSpace<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSimpleSpace<EfCoreSimpleSpaceStore<TContext>>();
    }

    /// <summary>
    /// Replaces the in-process <see cref="ISpaceReplayStore"/> with an EF Core-backed one, so
    /// single-use tokens are spent once across every instance rather than once per process.
    /// </summary>
    /// <typeparam name="TContext">
    /// A <see cref="DbContext"/> configured with
    /// <see cref="SpaceDbContext.ConfigureSpaceReplayModel"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoEfCoreSpaceReplayStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Replace rather than TryAdd: AddAtProtoSpaces() may already have registered the
        // in-process default, and this call is the deployment saying it wants the shared one.
        services.Replace(ServiceDescriptor.Singleton<ISpaceReplayStore, EfCoreSpaceReplayStore<TContext>>());

        return services;
    }
}
