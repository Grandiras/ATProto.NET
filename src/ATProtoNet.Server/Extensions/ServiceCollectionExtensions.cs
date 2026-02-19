using ATProtoNet.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server;

/// <summary>
/// Extension methods for registering ATProtoNet services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the AT Protocol client to the DI container.
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
    /// Useful for multi-user server applications.
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
