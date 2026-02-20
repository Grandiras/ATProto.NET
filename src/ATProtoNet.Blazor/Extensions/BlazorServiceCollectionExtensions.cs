using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Blazor.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Blazor;

/// <summary>
/// Extension methods for registering ATProtoNet Blazor services.
/// </summary>
public static class BlazorServiceCollectionExtensions
{
    /// <summary>
    /// Add AT Protocol services for Blazor, including authentication state provider.
    /// Supports both legacy password auth and OAuth.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure client options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Basic setup (password auth, dynamic PDS via LoginForm)
    /// builder.Services.AddAtProtoBlazor();
    ///
    /// // With OAuth
    /// builder.Services.AddAtProtoBlazor(options =>
    /// {
    ///     options.InstanceUrl = "https://bsky.social";
    ///     options.OAuth = new OAuthOptions
    ///     {
    ///         ClientMetadata = new OAuthClientMetadata
    ///         {
    ///             ClientId = "https://myapp.example.com/oauth/client-metadata.json",
    ///             ClientName = "My AT Proto App",
    ///             RedirectUris = ["https://myapp.example.com/oauth/callback"],
    ///         },
    ///     };
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProtoBlazor(
        this IServiceCollection services,
        Action<AtProtoClientOptions>? configure = null)
    {
        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();

        services.TryAddSingleton(sp =>
        {
            var options = new AtProtoClientOptions();
            configure?.Invoke(options);

            var sessionStore = sp.GetRequiredService<ISessionStore>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<AtProtoClient>>();

            return new AtProtoClient(options, null, sessionStore, logger);
        });

        // Register OAuthClient if OAuth options are provided
        services.TryAddSingleton(sp =>
        {
            var options = new AtProtoClientOptions();
            configure?.Invoke(options);

            if (options.OAuth is null)
                return (OAuthClient?)null;

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
                $"ATProtoNet/{typeof(AtProtoClient).Assembly.GetName().Version}");

            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<OAuthClient>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OAuthClient>.Instance;

            return (OAuthClient?)new OAuthClient(options.OAuth, httpClient, logger);
        });

        services.TryAddSingleton(sp =>
        {
            var client = sp.GetRequiredService<AtProtoClient>();
            var oauthClient = sp.GetService<OAuthClient?>();

            return oauthClient is not null
                ? new AtProtoAuthStateProvider(client, oauthClient)
                : new AtProtoAuthStateProvider(client);
        });

        services.TryAddSingleton<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<AtProtoAuthStateProvider>());

        services.AddAuthorizationCore();

        return services;
    }
}
