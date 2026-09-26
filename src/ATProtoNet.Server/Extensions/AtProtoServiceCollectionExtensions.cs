using ATProtoNet.Auth;
using ATProtoNet.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server;

/// <summary>
/// Registers the AT Protocol services with dependency injection.
/// </summary>
public static class AtProtoServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> every <see cref="AtProtoClient"/> the registration
    /// creates sends with (see <see cref="IAtProtoBuilder.HttpClient"/>).
    /// </summary>
    public const string HttpClientName = "ATProtoNet";

    /// <summary>
    /// Registers an <see cref="AtProtoClient"/> (a singleton by default) configured by
    /// <see cref="AtProtoClientOptions"/>, and returns the builder the rest of the AT Protocol
    /// registration hangs off.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configures the client options. It runs after configuration bound to them (by
    /// <c>AddAtProtoClient()</c> or <c>services.Configure&lt;AtProtoClientOptions&gt;(section)</c>),
    /// whichever order the calls are made in.
    /// </param>
    /// <returns>The builder, for the session store, the OAuth login, the client factory and the health check.</returns>
    /// <remarks>
    /// <para>The options go through <see cref="IOptions{TOptions}"/>: bind them from configuration
    /// with <c>services.Configure&lt;AtProtoClientOptions&gt;(section)</c> (or
    /// <c>builder.AddAtProtoClient()</c>, which binds the <c>AtProto</c> section), and a bad
    /// <see cref="AtProtoClientOptions.InstanceUrl"/> stops the host at startup.</para>
    /// <para>The registered client is for acting as one account, such as a bot or a service
    /// logging in with an app password. It persists its session to the registered
    /// <see cref="IAtProtoSessionStore"/>, if any, under the registered
    /// <see cref="ISessionRefreshCoordinator"/>. For users signed in with OAuth, add
    /// <c>WithOAuth()</c> and <see cref="AtProtoBuilderExtensions.WithClientFactory"/> and create
    /// their clients with <see cref="Services.IAtProtoClientFactory"/>.</para>
    /// <para>Calling this again adds the configuration to the same registration.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAtProto(options => options.InstanceUrl = "https://pds.example.com");
    ///
    /// public class MyService(AtProtoClient client) { … }
    /// </code>
    /// </example>
    public static IAtProtoBuilder AddAtProto(
        this IServiceCollection services,
        Action<AtProtoClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatedOptions(configure, ValidateClientOptions)
            // A client that shares its store with others must refresh under their coordinator.
            .PostConfigure<IServiceProvider>((options, provider) =>
                options.RefreshCoordinator ??= provider.GetService<ISessionRefreshCoordinator>());

        // No default User-Agent here: AtProtoClient sets one on every request.
        var httpClient = services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(AtProtoHttp.CreateHandler);

        services.TryAdd(new ServiceDescriptor(typeof(AtProtoClient), CreateClient, ServiceLifetime.Singleton));

        return new AtProtoBuilder(services, httpClient);
    }

    /// <summary>Creates the registered <see cref="AtProtoClient"/>.</summary>
    internal static AtProtoClient CreateClient(IServiceProvider services) => new(
        services.GetRequiredService<IOptions<AtProtoClientOptions>>().Value,
        services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
        services.GetService<IAtProtoSessionStore>(),
        services.GetService<ILogger<AtProtoClient>>());

    private static void ValidateClientOptions(AtProtoClientOptions options)
    {
        if (!AtProtoHttp.TryNormalizeBaseUrl(options.InstanceUrl, out _))
        {
            throw new ArgumentException(
                $"Must be an absolute http(s) URL with no query or fragment; got '{options.InstanceUrl}'.",
                nameof(AtProtoClientOptions.InstanceUrl));
        }

        ArgumentNullException.ThrowIfNull(options.RateLimit, nameof(AtProtoClientOptions.RateLimit));
    }
}
