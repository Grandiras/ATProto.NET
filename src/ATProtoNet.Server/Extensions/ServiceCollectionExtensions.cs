using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Services;
using ATProtoNet.Server.TokenStore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server;

/// <summary>
/// Extension methods for registering ATProtoNet services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers server-side AT Protocol services: token store and client factory.
    /// Use alongside <c>AddAtProtoAuthentication()</c> from ATProtoNet.Blazor to enable
    /// backend AT Protocol API access for logged-in users.
    /// </summary>
    /// <remarks>
    /// <para>Registers:</para>
    /// <list type="bullet">
    /// <item><description><see cref="IAtProtoTokenStore"/> — for storing OAuth tokens server-side (default: in-memory)</description></item>
    /// <item><description><see cref="IAtProtoClientFactory"/> — for creating per-request authenticated <see cref="AtProtoClient"/> instances</description></item>
    /// </list>
    /// <para>When the Blazor <c>AtProtoOAuthService</c> detects that <see cref="IAtProtoTokenStore"/>
    /// is registered, it automatically stores OAuth tokens after login and removes them on logout.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Program.cs
    /// builder.Services.AddAtProtoAuthentication(); // Blazor OAuth login
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
        services.AddHttpClient("AtProtoClient");

        services.TryAddSingleton<IAtProtoTokenStore, FileAtProtoTokenStore>();
        services.TryAddSingleton<IAtProtoClientFactory, AtProtoClientFactory>();

        return services;
    }

    /// <summary>
    /// Registers server-side AT Protocol services with a custom token store directory.
    /// Tokens are encrypted and persisted to files using ASP.NET Core Data Protection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tokenDirectory">
    /// Directory where encrypted token files are stored.
    /// Defaults to <c>{LocalApplicationData}/ATProtoNet/tokens</c>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoServer(this IServiceCollection services, string tokenDirectory)
    {
        services.AddSingleton(new FileTokenStoreOptions { Directory = tokenDirectory });
        return services.AddAtProtoServer();
    }

    /// <summary>
    /// Registers server-side AT Protocol services with a custom token store implementation.
    /// </summary>
    /// <typeparam name="TTokenStore">
    /// Custom <see cref="IAtProtoTokenStore"/> implementation
    /// (e.g., backed by a database, Redis, or encrypted file store).
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoServer<TTokenStore>(this IServiceCollection services)
        where TTokenStore : class, IAtProtoTokenStore
    {
        services.AddHttpClient("AtProtoClient");

        services.AddSingleton<IAtProtoTokenStore, TTokenStore>();
        services.TryAddSingleton<IAtProtoClientFactory, AtProtoClientFactory>();

        return services;
    }

    /// <summary>
    /// Add a standalone AT Protocol client to the DI container.
    /// This is for server-to-server scenarios where you log in with app credentials
    /// (not user OAuth tokens). For user-authenticated access, use <see cref="AddAtProtoServer(IServiceCollection)"/> instead.
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
        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();

        services.AddHttpClient<AtProtoClient>();

        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton(sp =>
        {
            var options = new AtProtoClientOptions();
            configure?.Invoke(options);

            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AtProtoClient));
            var sessionStore = sp.GetRequiredService<ISessionStore>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AtProtoClient>>();

            return new AtProtoClient(options, httpClient, sessionStore, logger);
        });

        return services;
    }

    /// <summary>
    /// Add the AT Protocol client with a custom session store.
    /// </summary>
    public static IServiceCollection AddAtProto<TSessionStore>(
        this IServiceCollection services,
        Action<AtProtoClientOptions>? configure = null)
        where TSessionStore : class, ISessionStore
    {
        services.AddSingleton<ISessionStore, TSessionStore>();
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
        services.TryAddScoped<ISessionStore, InMemorySessionStore>();

        services.AddHttpClient<AtProtoClient>();

        services.AddScoped(sp =>
        {
            var options = new AtProtoClientOptions();
            configure?.Invoke(options);

            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AtProtoClient));
            var sessionStore = sp.GetRequiredService<ISessionStore>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AtProtoClient>>();

            return new AtProtoClient(options, httpClient, sessionStore, logger);
        });

        return services;
    }
}
