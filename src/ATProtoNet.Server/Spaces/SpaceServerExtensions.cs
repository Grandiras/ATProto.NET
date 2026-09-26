using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
///         options.ServiceDid = Did.Parse("did:web:pds.example.com");
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
    /// The service key of the <see cref="IDidResolver"/> the space server resolves DID documents
    /// through: a cache of its own, configured by <see cref="SpaceServerOptions.DidCache"/>.
    /// </summary>
    public const string DidResolverKey = "ATProtoNet.Server.Spaces";

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
        })
            // Every URL this client reaches comes from a party the service does not control: a
            // subscriber's or managing app's DID document, a client ID. So it connects only to
            // public addresses, follows no redirects, and honours the identity development opt-out.
            .ConfigurePrimaryHttpMessageHandler(sp =>
                IdentityNetworkPolicy.CreateHandler(sp.GetRequiredService<IdentityResolverOptions>().AllowPrivateNetworks));

        services.TryAddSingleton<IJtiReplayStore, InMemoryJtiReplayStore>();

        // The in-process defaults are the right choice for a single instance and the wrong one
        // for two, and nothing about a deployment says which it is — so the service says at
        // startup which stores it ended up with.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, InMemorySpaceStoreWarning>());

        // Every authenticated request resolves a DID document. The space server keeps a cache of
        // its own, with a lifetime short enough that a rotated key stops verifying quickly, and
        // fetches under the shared identity options.
        services.AddAtProtoIdentity();
        services.TryAddKeyedSingleton<IDidResolver>(DidResolverKey, (sp, _) =>
        {
            var cache = sp.GetRequiredService<SpaceServerOptions>().DidCache;
            return new CachingDidResolver(
                new DidResolver(sp.GetRequiredService<IdentityResolverOptions>()),
                cache,
                cache.UseDistributedCache ? sp.GetRequiredService<IDistributedCache>() : null,
                sp.GetService<TimeProvider>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<CachingDidResolver>>(),
                ownsInner: true);
        });
        services.TryAddSingleton<ISpaceCallerResolver, ClaimsSpaceCallerResolver>();

        // The one outbound fetch that needs the named, SSRF-safe client.
        services.TryAddSingleton<ISpaceClientMetadataResolver>(sp => new HttpSpaceClientMetadataResolver(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<SpaceServerOptions>()));

        // The verifiers take the space server's own resolver through [FromKeyedServices], and a
        // registered TimeProvider when there is one.
        services.TryAddSingleton<DPoPProofValidator>();
        services.TryAddSingleton<SpaceDelegationTokenVerifier>();
        services.TryAddSingleton<SpaceCredentialVerifier>();
        services.TryAddSingleton<SpaceClientAttestationVerifier>();
        services.TryAddSingleton<SpaceServiceAuthVerifier>();
        services.TryAddSingleton<SpaceRequestAuthenticator>();

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
    /// document at the entry <see cref="SpaceServerOptions.CredentialKeyId"/> names:
    /// <c>#atproto</c> by default, or <c>#atproto_space</c> for a dedicated key.
    /// </param>
    /// <param name="serviceAuthKey">
    /// The service's <c>#atproto</c> key, which signs outbound service auth (forwarded
    /// notifications, deletion notices, managing-app checks) as
    /// <see cref="SpaceServerOptions.ServiceDid"/>. Defaults to <paramref name="signingKey"/>,
    /// which is that key unless credentials are signed with a dedicated <c>#atproto_space</c>
    /// key. With a dedicated key, pass this or register an <see cref="ISpaceAccountSigner"/> that
    /// signs as the service's DID; the host refuses to start with neither, since service auth is
    /// only ever accepted from an <c>#atproto</c> key.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>An access policy must also be registered — <see cref="AddSimpleSpace{TStore}"/>
    /// supplies the baseline one, or register your own <see cref="ISpaceAccessPolicy"/> before
    /// calling this.</para>
    /// <para>When an <see cref="ISimpleSpaceStore"/> is also registered — in either order — the
    /// authority store is wrapped in a <see cref="SimpleSpaceAuthorityStore"/>, so a space
    /// created through <c>com.atproto.simplespace.createSpace</c> is one this authority answers
    /// <c>listRepos</c>, <c>registerNotify</c>, and <c>notifyWrite</c> for, and one deleted
    /// through <c>deleteSpace</c> answers <c>SpaceDeleted</c>. A store registered as
    /// <see cref="ISpaceAuthorityStore"/> before this call is left exactly as registered; wrap it
    /// yourself if it needs the same bridge.</para>
    /// <para>Outbound notifications and managing-app checks are signed as
    /// <see cref="SpaceServerOptions.ServiceDid"/> with <paramref name="signingKey"/>, unless an
    /// <see cref="ISpaceAccountSigner"/> is registered and holds the key of the account a call
    /// speaks for — which a service hosting its users' repos needs, since the reference authority
    /// accepts a write notification only from its writer.</para>
    /// </remarks>
    public static IServiceCollection AddSpaceAuthority<TStore>(
        this IServiceCollection services, AtProtoKey signingKey, AtProtoKey? serviceAuthKey = null)
        where TStore : class, ISpaceAuthorityStore
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signingKey);

        services.TryAddSingleton<TStore>();

        // Which spaces exist is space-management state, and when com.atproto.simplespace is the
        // space management in use it is the one holding it — so the authority reads existence and
        // deletion from there rather than keeping a second copy that createSpace never writes to.
        // Resolved rather than decided here, so the two registrations can be made in either order.
        services.TryAddSingleton<ISpaceAuthorityStore>(sp =>
        {
            var store = sp.GetRequiredService<TStore>();

            return sp.GetService<ISimpleSpaceStore>() is { } spaces
                ? new SimpleSpaceAuthorityStore(store, spaces)
                : store;
        });

        services.TryAddSingleton<ISpaceCredentialIssuer>(sp =>
            new SpaceCredentialIssuer(signingKey, sp.GetRequiredService<SpaceServerOptions>()));

        services.TryAddSingleton(sp => CreateServiceAuth(
            sp.GetRequiredService<SpaceServerOptions>(),
            signingKey,
            serviceAuthKey,
            hasAccountSigner: sp.GetService<ISpaceAccountSigner>() is not null));

        // Resolved at startup rather than at the first notification, so a key configuration that
        // can only produce refused service auth stops the host instead of dropping every forward.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SpaceAuthorityStartupCheck>());

        services.TryAddSingleton(sp => new SpaceWriteNotifier(
            sp.GetRequiredService<ISpaceAuthorityStore>(),
            sp.GetRequiredKeyedService<IDidResolver>(DidResolverKey),
            sp.GetRequiredService<ServiceAuthGenerator>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<ISpaceAccountSigner>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<SpaceWriteNotifier>>()));

        services.AddXrpcEndpoint<GetSpaceCredentialEndpoint>();
        services.AddXrpcEndpoint<ListSpaceReposEndpoint>();
        services.AddXrpcEndpoint<RegisterNotifyEndpoint>();
        services.AddXrpcEndpoint<UnregisterNotifyEndpoint>();
        services.AddXrpcEndpoint<NotifyWriteEndpoint>();

        return services;
    }

    /// <summary>
    /// Builds the generator outbound service auth falls back to: signed as the service DID with
    /// its <c>#atproto</c> key.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No service DID is configured, or credentials use a dedicated <c>#atproto_space</c> key and
    /// neither a service auth key nor an account signer was supplied.
    /// </exception>
    internal static ServiceAuthGenerator CreateServiceAuth(
        SpaceServerOptions options, AtProtoKey signingKey, AtProtoKey? serviceAuthKey, bool hasAccountSigner)
    {
        if (options.ServiceDid is null)
        {
            throw new InvalidOperationException(
                $"A space authority must know its own DID; set {nameof(SpaceServerOptions)}.{nameof(SpaceServerOptions.ServiceDid)}.");
        }

        if (serviceAuthKey is not null)
            return new ServiceAuthGenerator(options.ServiceDid, serviceAuthKey);

        if (!UsesDedicatedCredentialKey(options))
            return new ServiceAuthGenerator(options.ServiceDid, signingKey);

        if (!hasAccountSigner)
        {
            throw new InvalidOperationException(
                $"Credentials are signed with the dedicated {SpaceAuthority.SigningKeyId} key, but service auth is only " +
                $"accepted from an #atproto key. Pass the service's #atproto key to {nameof(AddSpaceAuthority)} as " +
                $"serviceAuthKey, or register an {nameof(ISpaceAccountSigner)} that signs as {options.ServiceDid}.");
        }

        // The signer answers for the service's own DID. This generator is reached only for an
        // account the signer holds no key for, and says truthfully which key it signs with; a
        // receiver refuses it, as it would any service auth not signed by the account.
        return new ServiceAuthGenerator(options.ServiceDid, signingKey, SpaceAuthority.SigningKeyId);
    }

    private static bool UsesDedicatedCredentialKey(SpaceServerOptions options) =>
        options.CredentialKeyId is { } keyId &&
        string.Equals(keyId.TrimStart('#'), SpaceAuthority.SigningKeyId.TrimStart('#'), StringComparison.Ordinal);

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
            sp.GetRequiredKeyedService<IDidResolver>(DidResolverKey),
            sp.GetRequiredService<ServiceAuthGenerator>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<ISpaceAccountSigner>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<SimpleSpaceManagingAppClient>>()));

        services.TryAddSingleton<ISpaceAccessPolicy, SimpleSpaceAccessPolicy>();

        services.AddXrpcEndpoint<CreateSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<UpdateSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<DeleteSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<GetSimpleSpaceEndpoint>();
        services.AddXrpcEndpoint<PutSimpleSpaceMemberEndpoint>();
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
