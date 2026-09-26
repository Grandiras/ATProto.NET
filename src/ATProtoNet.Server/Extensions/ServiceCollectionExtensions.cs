using ATProtoNet.Auth;
using ATProtoNet.Server.Services;
using ATProtoNet.Server.TokenStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server;

/// <summary>
/// Extension methods for registering ATProtoNet services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers server-side AT Protocol services: session store and client factory.
    /// Use alongside <c>AddAtProtoAuthentication()</c> to enable backend AT Protocol API access
    /// for logged-in users.
    /// </summary>
    /// <remarks>
    /// <para>Registers:</para>
    /// <list type="bullet">
    /// <item><description><see cref="IAtProtoSessionStore"/> — for storing sessions server-side (default: <see cref="FileAtProtoSessionStore"/>)</description></item>
    /// <item><description><see cref="IAtProtoClientFactory"/> — for creating per-request authenticated <see cref="AtProtoClient"/> instances</description></item>
    /// <item><description><see cref="ISessionRefreshCoordinator"/> — an <see cref="InProcessSessionRefreshCoordinator"/>, under which those clients, the OAuth login and sign-out change a user's stored session one at a time</description></item>
    /// </list>
    /// <para>When the hosted OAuth login (<c>AtProtoOAuthService</c>) finds an <see cref="IAtProtoSessionStore"/>
    /// registered, it stores the OAuth session after login, and revokes and removes it on logout.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Program.cs
    /// builder.Services.AddAtProtoAuthentication(); // OAuth cookie login
    /// builder.Services.AddAtProtoServer();          // Backend AT Proto access
    ///
    /// // In a Minimal API endpoint or controller:
    /// app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
    /// {
    ///     await using var client = await factory.CreateClientForUserAsync(user);
    ///     if (client is null) return Results.Unauthorized();
    ///     var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
    ///     return Results.Ok(profile);
    /// });
    /// </code>
    /// </example>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoServer(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.AddHttpClient(AtProtoClientFactory.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(Http.AtProtoHttp.CreateHandler);

        services.TryAddSingleton<IAtProtoSessionStore, FileAtProtoSessionStore>();
        services.TryAddSingleton<ISessionRefreshCoordinator, InProcessSessionRefreshCoordinator>();
        services.TryAddSingleton<IAtProtoClientFactory, AtProtoClientFactory>();

        return services;
    }

    /// <summary>
    /// Registers server-side AT Protocol services with a custom session store directory.
    /// Sessions are encrypted and persisted to files using ASP.NET Core Data Protection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tokenDirectory">
    /// Directory where encrypted session files are stored.
    /// Defaults to <c>{LocalApplicationData}/ATProtoNet/tokens</c>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoServer(this IServiceCollection services, string tokenDirectory)
    {
        services.AddSingleton(new FileSessionStoreOptions { Directory = tokenDirectory });
        return services.AddAtProtoServer();
    }

    /// <summary>
    /// Registers server-side AT Protocol services with a custom session store implementation.
    /// </summary>
    /// <typeparam name="TSessionStore">
    /// Custom <see cref="IAtProtoSessionStore"/> implementation
    /// (e.g., backed by a database, Redis, or encrypted file store).
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoServer<TSessionStore>(this IServiceCollection services)
        where TSessionStore : class, IAtProtoSessionStore
    {
        services.AddHttpClient(AtProtoClientFactory.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(Http.AtProtoHttp.CreateHandler);

        services.AddSingleton<IAtProtoSessionStore, TSessionStore>();
        services.TryAddSingleton<ISessionRefreshCoordinator, InProcessSessionRefreshCoordinator>();
        services.TryAddSingleton<IAtProtoClientFactory, AtProtoClientFactory>();

        return services;
    }

    /// <summary>
    /// Add a standalone AT Protocol client to the DI container.
    /// This is for server-to-server scenarios where you log in with app credentials
    /// (not user OAuth tokens). For user-authenticated access, use <see cref="AddAtProtoServer(IServiceCollection)"/> instead.
    /// The client persists its session to the registered <see cref="IAtProtoSessionStore"/>, if any.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the client options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddAtProto(options =>
    /// {
    ///     options.InstanceUrl = "https://bsky.social";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProto(
        this IServiceCollection services,
        Action<AtProtoClientOptions>? configure = null)
    {
        services.AddHttpClient<AtProtoClient>()
            .ConfigurePrimaryHttpMessageHandler(Http.AtProtoHttp.CreateHandler);

        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton(sp =>
        {
            var options = new AtProtoClientOptions();
            configure?.Invoke(options);

            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AtProtoClient));
            var sessionStore = sp.GetService<IAtProtoSessionStore>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AtProtoClient>>();

            return new AtProtoClient(options, httpClient, sessionStore, logger);
        });

        return services;
    }

    /// <summary>
    /// Add the AT Protocol client with a session store it persists its session to.
    /// </summary>
    public static IServiceCollection AddAtProto<TSessionStore>(
        this IServiceCollection services,
        Action<AtProtoClientOptions>? configure = null)
        where TSessionStore : class, IAtProtoSessionStore
    {
        services.AddSingleton<IAtProtoSessionStore, TSessionStore>();
        return services.AddAtProto(configure);
    }

    /// <summary>
    /// Add a scoped AT Protocol client (one per request / per user).
    /// Useful for multi-user server applications with app-password authentication.
    /// For OAuth-based multi-user access, use <see cref="AddAtProtoServer(IServiceCollection)"/> instead.
    /// </summary>
    public static IServiceCollection AddAtProtoScoped(
        this IServiceCollection services,
        Action<AtProtoClientOptions>? configure = null)
    {
        services.AddHttpClient<AtProtoClient>()
            .ConfigurePrimaryHttpMessageHandler(Http.AtProtoHttp.CreateHandler);

        services.AddScoped(sp =>
        {
            var options = new AtProtoClientOptions();
            configure?.Invoke(options);

            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AtProtoClient));
            var sessionStore = sp.GetService<IAtProtoSessionStore>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AtProtoClient>>();

            return new AtProtoClient(options, httpClient, sessionStore, logger);
        });

        return services;
    }
}
