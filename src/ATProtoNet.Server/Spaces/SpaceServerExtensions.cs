using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Server.Xrpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Registers the space server: the credential verifiers, and the XRPC endpoints for a space
/// authority, a repo host, or both.
/// </summary>
/// <remarks>
/// <para>The two halves are registered separately because they are separate services in
/// practice. A PDS is a repo host for its accounts and — through
/// <c>com.atproto.simplespace</c> — an authority for the personal-data spaces anchored on them.
/// A dedicated space service is an authority and nothing else. Register only the half a service
/// actually implements: a route that answers is a route that has to be secured.</para>
/// <para>Endpoints are registered through the ordinary XRPC handler routing, so
/// <c>MapXrpcEndpoints()</c> maps them alongside an application's own.</para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services
///     .AddAtProtoSpaces(options =>
///     {
///         options.ServiceDid = "did:web:pds.example.com";
///         options.PublicBaseUrl = "https://pds.example.com";
///     })
///     .AddSpaceAuthority&lt;MyAuthorityStore&gt;(signingKey)
///     .AddSimpleSpace&lt;MySimpleSpaceStore&gt;()
///     .AddSpaceRepoHost&lt;MyRepoHost&gt;();
///
/// app.MapXrpcEndpoints();
/// </code>
/// </example>
public static class SpaceServerExtensions
{
    /// <summary>The named <see cref="HttpClient"/> the space server's outbound calls use.</summary>
    public const string HttpClientName = "AtProtoSpaces";

    /// <summary>
    /// Registers the credential verification layer: DPoP proof, delegation token, space
    /// credential, client attestation, and service auth verification.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="SpaceServerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This is the half that has to be right, and it is registered on its own so a service that
    /// only needs to <em>verify</em> — a moderation service, a proxy, a test harness — can take
    /// it without also standing up an endpoint surface.
    /// </remarks>
    public static IServiceCollection AddAtProtoSpaces(
        this IServiceCollection services, Action<SpaceServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SpaceServerOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.AddHttpClient(HttpClientName, client =>
        {
            // Both outbound paths here — fetching a client's metadata, asking a managing app —
            // sit on the critical path of a credential request, so they fail fast rather than
            // holding the exchange open.
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.TryAddSingleton<ISpaceReplayStore, InMemorySpaceReplayStore>();
        services.TryAddSingleton<ISpaceDidDocumentResolver>(sp =>
            new CachingSpaceDidDocumentResolver(sp.GetRequiredService<SpaceServerOptions>()));
        services.TryAddSingleton<ISpaceCallerResolver, ClaimsSpaceCallerResolver>();

        services.TryAddSingleton<ISpaceClientMetadataResolver>(sp => new HttpSpaceClientMetadataResolver(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton(sp => new DPoPProofValidator(
            sp.GetRequiredService<ISpaceReplayStore>(), sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton(sp => new SpaceDelegationTokenVerifier(
            sp.GetRequiredService<ISpaceDidDocumentResolver>(),
            sp.GetRequiredService<ISpaceReplayStore>(),
            sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton(sp => new SpaceCredentialVerifier(
            sp.GetRequiredService<ISpaceDidDocumentResolver>(),
            sp.GetRequiredService<DPoPProofValidator>()));

        services.TryAddSingleton(sp => new SpaceClientAttestationVerifier(
            sp.GetRequiredService<ISpaceClientMetadataResolver>(),
            sp.GetRequiredService<ISpaceReplayStore>(),
            sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton<ISpaceServiceAuthVerifier>(sp => new SpaceServiceAuthVerifier(
            sp.GetRequiredService<ISpaceDidDocumentResolver>(),
            sp.GetRequiredService<ISpaceReplayStore>(),
            sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton(sp => new SpaceRequestAuthenticator(
            sp.GetRequiredService<SpaceDelegationTokenVerifier>(),
            sp.GetRequiredService<SpaceCredentialVerifier>(),
            sp.GetRequiredService<DPoPProofValidator>(),
            sp.GetRequiredService<SpaceClientAttestationVerifier>(),
            sp.GetRequiredService<SpaceServerOptions>()));

        return services;
    }

    /// <summary>
    /// Registers the space-authority endpoints: <c>getSpaceCredential</c>, <c>listRepos</c>,
    /// <c>registerNotify</c>, <c>unregisterNotify</c>, and <c>notifyWrite</c>.
    /// </summary>
    /// <typeparam name="TStore">The authority's state store.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="signingKey">
    /// The key credentials are signed with. It must be the one published in the authority's DID
    /// document at <c>#atproto_space</c>, or at <c>#atproto</c> when it publishes no dedicated
    /// entry.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// An access policy must also be registered — <see cref="AddSimpleSpace{TStore}"/> supplies
    /// the baseline one, or register your own <see cref="ISpaceAccessPolicy"/> before calling
    /// this.
    /// </remarks>
    public static IServiceCollection AddSpaceAuthority<TStore>(
        this IServiceCollection services, AtProtoKey signingKey)
        where TStore : class, ISpaceAuthorityStore
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signingKey);

        services.TryAddSingleton<ISpaceAuthorityStore, TStore>();
        services.TryAddSingleton<ISpaceCredentialIssuer>(sp =>
            new SpaceCredentialIssuer(signingKey, sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<SpaceServerOptions>();
            if (string.IsNullOrWhiteSpace(options.ServiceDid))
            {
                throw new InvalidOperationException(
                    $"A space authority must know its own DID; set {nameof(SpaceServerOptions)}.{nameof(SpaceServerOptions.ServiceDid)}.");
            }

            return new ServiceAuthGenerator(options.ServiceDid, signingKey);
        });

        services.TryAddSingleton(sp => new SpaceWriteNotifier(
            sp.GetRequiredService<ISpaceAuthorityStore>(),
            sp.GetRequiredService<ISpaceDidDocumentResolver>(),
            sp.GetRequiredService<ServiceAuthGenerator>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<SpaceWriteNotifier>>()));

        services.AddXrpcEndpoint<GetSpaceCredentialEndpoint>();
        services.AddXrpcEndpoint<ListSpaceReposEndpoint>();
        services.AddXrpcEndpoint<RegisterNotifyEndpoint>();
        services.AddXrpcEndpoint<UnregisterNotifyEndpoint>();
        services.AddXrpcEndpoint<NotifyWriteEndpoint>();

        return services;
    }

    /// <summary>
    /// Registers the <c>com.atproto.simplespace</c> endpoints and its access policy — the
    /// space-management baseline every PDS must support.
    /// </summary>
    /// <typeparam name="TStore">The store holding the spaces and their member lists.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Registering this also supplies the <see cref="ISpaceAccessPolicy"/> the authority's
    /// credential exchange consults, so a service using <c>simplespace</c> needs no policy of its
    /// own. A service running a bespoke space type registers its own policy instead and skips
    /// this entirely.
    /// </remarks>
    public static IServiceCollection AddSimpleSpace<TStore>(this IServiceCollection services)
        where TStore : class, ISimpleSpaceStore
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISimpleSpaceStore, TStore>();

        services.TryAddSingleton<ISimpleSpaceManagingAppClient>(sp => new SimpleSpaceManagingAppClient(
            sp.GetRequiredService<ISpaceDidDocumentResolver>(),
            sp.GetRequiredService<ServiceAuthGenerator>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        services.TryAddSingleton<ISpaceAccessPolicy>(sp => new SimpleSpaceAccessPolicy(
            sp.GetRequiredService<ISimpleSpaceStore>(),
            sp.GetRequiredService<ISimpleSpaceManagingAppClient>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<SimpleSpaceAccessPolicy>>()));

        services.AddXrpcEndpoint<CreateSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<UpdateSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<DeleteSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<GetSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<AddSimpleSpaceMemberEndpoint>();
        services.AddXrpcEndpoint<RemoveSimpleSpaceMemberEndpoint>();
        services.AddXrpcEndpoint<ListSimpleSpaceMembersEndpoint>();

        return services;
    }

    /// <summary>
    /// Registers the repo-host endpoints: <c>getRecord</c>, <c>listRecords</c>,
    /// <c>getLatestCommit</c>, <c>getRepo</c>, <c>listRepoOps</c>, <c>getBlob</c>, and
    /// <c>listBlobs</c>.
    /// </summary>
    /// <typeparam name="THost">The implementation serving this host's permissioned repos.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSpaceRepoHost<THost>(this IServiceCollection services)
        where THost : class, ISpaceRepoHost
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISpaceRepoHost, THost>();

        services.AddXrpcEndpoint<GetSpaceRecordEndpoint>();
        services.AddXrpcEndpoint<ListSpaceRecordsEndpoint>();
        services.AddXrpcEndpoint<GetSpaceLatestCommitEndpoint>();
        services.AddXrpcEndpoint<GetSpaceRepoEndpoint>();
        services.AddXrpcEndpoint<ListSpaceRepoOpsEndpoint>();
        services.AddXrpcEndpoint<GetSpaceBlobEndpoint>();
        services.AddXrpcEndpoint<ListSpaceBlobsEndpoint>();

        return services;
    }
}
