using ATProtoNet.Aspire;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Services;
using ATProtoNet.Server.TokenStore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server;

/// <summary>
/// The registrations that hang off <see cref="IAtProtoBuilder"/>: the client's lifetime, the
/// session store, the per-user client factory and the health check.
/// </summary>
public static class AtProtoBuilderExtensions
{
    /// <summary>
    /// Registers the <see cref="AtProtoClient"/> with another lifetime than the default
    /// singleton: <see cref="ServiceLifetime.Scoped"/> for one client per request, each with its
    /// own session.
    /// </summary>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <param name="lifetime">The client's lifetime.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// This replaces the <see cref="AtProtoClient"/> registration. A transient client resolved from
    /// the root provider is kept until the application stops, as any disposable transient is; use
    /// a scope.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not a defined value.</exception>
    public static IAtProtoBuilder WithLifetime(this IAtProtoBuilder builder, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Not a service lifetime.");

        builder.Services.Replace(new ServiceDescriptor(
            typeof(AtProtoClient), AtProtoServiceCollectionExtensions.CreateClient, lifetime));
        return builder;
    }

    /// <summary>
    /// Stores sessions in <typeparamref name="TStore"/>, a singleton, instead of the in-memory
    /// default.
    /// </summary>
    /// <remarks>
    /// The store is registered as a singleton and called concurrently, so it must not depend on a
    /// scoped service such as a <c>DbContext</c>, which would be captured for the application's
    /// lifetime: take an <c>IDbContextFactory</c> and open a context per call.
    /// </remarks>
    /// <typeparam name="TStore">The session store: durable, shared by the instances, and
    /// encrypting what it stores (an <see cref="OAuthSession"/> carries its DPoP private key).</typeparam>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IAtProtoBuilder WithSessionStore<TStore>(this IAtProtoBuilder builder)
        where TStore : class, IAtProtoSessionStore
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSessionStore(ServiceDescriptor.Singleton<IAtProtoSessionStore, TStore>());
    }

    /// <summary>
    /// Stores sessions in the store <paramref name="factory"/> creates, a singleton, instead of
    /// the in-memory default.
    /// </summary>
    /// <remarks>
    /// <paramref name="factory"/> runs once, against the root provider: resolve no scoped service
    /// (a <c>DbContext</c>, say) from it.
    /// </remarks>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <param name="factory">Creates the session store.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IAtProtoBuilder WithSessionStore(
        this IAtProtoBuilder builder, Func<IServiceProvider, IAtProtoSessionStore> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        return builder.UseSessionStore(ServiceDescriptor.Singleton(factory));
    }

    /// <summary>
    /// Keeps sessions in memory (<see cref="InMemoryAtProtoSessionStore"/>) on purpose, which
    /// <see cref="WithClientFactory"/> otherwise does with a warning at startup.
    /// </summary>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Sessions are lost on a restart, so every user has to sign in again, and a second instance
    /// does not see them: fit for development and tests.
    /// </remarks>
    public static IAtProtoBuilder WithInMemorySessionStore(this IAtProtoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSessionStore(ServiceDescriptor.Singleton<IAtProtoSessionStore, InMemoryAtProtoSessionStore>());
        builder.Services.TryAddSingleton<InMemorySessionStoreChoice>();
        return builder;
    }

    /// <summary>
    /// Stores each session in its own file, encrypted with ASP.NET Core Data Protection
    /// (<see cref="FileAtProtoSessionStore"/>), for a single-server deployment.
    /// </summary>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <param name="configure">Configures the store; <see cref="FileSessionStoreOptions.Directory"/>
    /// defaults to <c>{LocalApplicationData}/ATProtoNet/tokens</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Keep the Data Protection key ring somewhere that survives a restart (it does by default on
    /// a developer machine, not in a container), or the stored sessions cannot be read.
    /// </remarks>
    public static IAtProtoBuilder WithFileSessionStore(
        this IAtProtoBuilder builder, Action<FileSessionStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddValidatedOptions(configure, static options =>
        {
            if (options.Directory is not null && string.IsNullOrWhiteSpace(options.Directory))
                throw new ArgumentException("Must be a path, or null for the default.", nameof(FileSessionStoreOptions.Directory));
        });
        builder.Services.AddDataProtection();

        return builder.UseSessionStore(ServiceDescriptor.Singleton<IAtProtoSessionStore>(sp => new FileAtProtoSessionStore(
            sp.GetRequiredService<IDataProtectionProvider>(),
            sp.GetRequiredService<ILogger<FileAtProtoSessionStore>>(),
            sp.GetRequiredService<FileSessionStoreOptions>())));
    }

    /// <summary>
    /// Replaces the session store registration with <paramref name="store"/>, so the chosen store
    /// wins over the default whichever order the registrations run in.
    /// </summary>
    private static IAtProtoBuilder UseSessionStore(this IAtProtoBuilder builder, ServiceDescriptor store)
    {
        builder.Services.RemoveAll<IAtProtoSessionStore>();
        builder.Services.RemoveAll<InMemorySessionStoreChoice>();
        builder.Services.Add(store);
        return builder;
    }

    /// <summary>
    /// Registers <see cref="IAtProtoClientFactory"/>, which creates a client for each signed-in
    /// user from their stored session, and what it needs: a session store (in memory unless one is
    /// chosen, with a warning at startup) and the <see cref="ISessionRefreshCoordinator"/> its
    /// clients refresh under.
    /// </summary>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <para>With the hosted OAuth login (<c>WithOAuth()</c>), the login stores each user's session
    /// in the store and the factory's clients refresh and revoke it through the login's
    /// <see cref="OAuthClient"/>, which the factory resolves on first use, so the factory itself
    /// can be resolved before the server has started.</para>
    /// <para>The per-user clients send with the builder's <see cref="IAtProtoBuilder.HttpClient"/>
    /// and take <see cref="AtProtoClientOptions.UserAgent"/> and
    /// <see cref="AtProtoClientOptions.RateLimit"/> from the options of <c>AddAtProto()</c>. The
    /// other options describe the registered client's own session and do not apply to them: each
    /// addresses its user's PDS and refreshes on demand under the coordinator.</para>
    /// <para>Pick a durable store for production: <see cref="WithFileSessionStore"/>,
    /// <c>WithEfCoreSessionStore&lt;TContext&gt;()</c> (the <c>ATProtoNet.Server.EntityFrameworkCore</c>
    /// package) or <see cref="WithSessionStore{TStore}"/>. The in-process coordinator covers one
    /// process; instances sharing a store need a distributed <see cref="ISessionRefreshCoordinator"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAtProto()
    ///     .WithOAuth()
    ///     .WithClientFactory()
    ///     .WithFileSessionStore();
    ///
    /// app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
    /// {
    ///     await using var client = await factory.CreateClientForUserAsync(user);
    ///     if (client is null) return Results.Unauthorized();
    ///     return Results.Ok(await client.Bsky.Actor.GetProfileAsync(client.Session!.Did));
    /// });
    /// </code>
    /// </example>
    public static IAtProtoBuilder WithClientFactory(this IAtProtoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.TryAddSingleton<IAtProtoSessionStore, InMemoryAtProtoSessionStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InMemorySessionStoreWarning>());
        services.TryAddSingleton<ISessionRefreshCoordinator, InProcessSessionRefreshCoordinator>();
        services.TryAddSingleton<IAtProtoClientFactory>(sp => new AtProtoClientFactory(
            sp.GetRequiredService<IAtProtoSessionStore>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILoggerFactory>(),
            // Resolved on first use: the development login's client takes its callback from the
            // server's address, which is not known until the server has started.
            () => sp.GetService<OAuthClient>(),
            sp.GetService<ISessionRefreshCoordinator>(),
            sp.GetService<IOptions<AtProtoClientOptions>>()?.Value));

        return builder;
    }

    /// <summary>
    /// Adds a health check (<see cref="AtProtoPdsHealthCheck"/>) that calls
    /// <c>com.atproto.server.describeServer</c> through the registered <see cref="AtProtoClient"/>.
    /// </summary>
    /// <param name="builder">The AT Protocol builder.</param>
    /// <param name="name">The health check's name. Default: <c>atproto-pds</c>.</param>
    /// <param name="failureStatus">The status an unreachable PDS reports. Default:
    /// <see cref="HealthStatus.Degraded"/>.</param>
    /// <param name="tags">Tags to filter health checks by. Default: <c>atproto</c> and <c>ready</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// A check already registered under <paramref name="name"/>, by an earlier call or by
    /// <c>AddAtProtoClient()</c>, is replaced: the health check service refuses two of one name.
    /// </remarks>
    public static IAtProtoBuilder WithHealthCheck(
        this IAtProtoBuilder builder,
        string name = "atproto-pds",
        HealthStatus? failureStatus = HealthStatus.Degraded,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var registration = new HealthCheckRegistration(
            name,
            sp => new AtProtoPdsHealthCheck(sp.GetRequiredService<AtProtoClient>()),
            failureStatus,
            tags ?? ["atproto", "ready"]);

        builder.Services.AddHealthChecks();
        builder.Services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (var existing in options.Registrations.Where(r => r.Name == name).ToList())
                options.Registrations.Remove(existing);

            options.Registrations.Add(registration);
        });

        return builder;
    }
}
