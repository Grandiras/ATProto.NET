using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server;

/// <summary>
/// The AT Protocol services of an application, as <see cref="AtProtoServiceCollectionExtensions.AddAtProto"/>
/// registers them: the rest of the registration (session store, OAuth login, client factory,
/// health check) hangs off it.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddAtProto()
///     .WithOAuth(o => o.ClientName = "My App")
///     .WithClientFactory()
///     .WithFileSessionStore(o => o.Directory = "/var/lib/myapp/sessions");
/// </code>
/// </example>
public interface IAtProtoBuilder
{
    /// <summary>The service collection the services are registered in.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The named <see cref="HttpClient"/> (<see cref="AtProtoServiceCollectionExtensions.HttpClientName"/>)
    /// every <see cref="AtProtoClient"/> of the registration sends with: the registered client, and
    /// the per-user clients of <see cref="Services.IAtProtoClientFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>Add handlers for logging, telemetry or a proxy here. The SDK already retries a
    /// <c>429</c> within <see cref="AtProtoClientOptions.RateLimit"/>, refreshes an expired
    /// session, and signs a fresh DPoP proof for every attempt, so do not add a handler that
    /// retries on its own: it would send a non-idempotent <c>POST</c> (a <c>createRecord</c>,
    /// say) twice, and resend a DPoP proof the server has already seen, which it refuses.</para>
    /// <para>Aspire service defaults add the standard resilience handler to every client through
    /// <c>ConfigureHttpClientDefaults</c>; remove it from this one with
    /// <c>HttpClient.RemoveAllResilienceHandlers()</c> (<c>Microsoft.Extensions.Http.Resilience</c>, experimental:
    /// <c>EXTEXP0001</c>).</para>
    /// </remarks>
    IHttpClientBuilder HttpClient { get; }
}

/// <summary>The <see cref="IAtProtoBuilder"/> of one <c>AddAtProto()</c> call.</summary>
internal sealed class AtProtoBuilder(IServiceCollection services, IHttpClientBuilder httpClient) : IAtProtoBuilder
{
    public IServiceCollection Services { get; } = services;

    public IHttpClientBuilder HttpClient { get; } = httpClient;
}
