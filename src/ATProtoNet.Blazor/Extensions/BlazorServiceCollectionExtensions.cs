using ATProtoNet.Auth;
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
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure client options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // In Program.cs
    /// builder.Services.AddAtProtoBlazor(options =>
    /// {
    ///     options.InstanceUrl = "https://bsky.social";
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

        services.TryAddSingleton<AtProtoAuthStateProvider>();
        services.TryAddSingleton<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<AtProtoAuthStateProvider>());

        services.AddAuthorizationCore();

        return services;
    }
}
