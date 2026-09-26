using System.Security.Claims;
using System.Text.Encodings.Web;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using XrpcErrorBody = ATProtoNet.Server.Xrpc.XrpcErrorBody;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// Names used by the service auth authentication scheme.
/// </summary>
public static class AtProtoServiceAuthDefaults
{
    /// <summary>The scheme name <c>AddAtProtoServiceAuth()</c> registers under by default.</summary>
    public const string AuthenticationScheme = "AtProtoServiceAuth";

    /// <summary>The claim carrying the caller's DID (the token's <c>iss</c>), and the identity's name.</summary>
    public const string DidClaimType = "did";

    /// <summary>The claim carrying the XRPC method the token was scoped to (<c>lxm</c>), when it named one.</summary>
    public const string LexiconMethodClaimType = "lxm";

    /// <summary>The claim carrying the audience the token addressed (<c>aud</c>).</summary>
    public const string AudienceClaimType = "aud";
}

/// <summary>
/// Options for the service auth authentication scheme: who this service is, and how strictly
/// tokens are checked.
/// </summary>
public sealed class AtProtoServiceAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The <c>aud</c> values this service answers to. Required.
    /// </summary>
    /// <remarks>
    /// <para>The spec's form is this service's DID with the fragment of its service entry, e.g.
    /// <c>did:web:feed.example.com#bsky_fg</c>. Add the bare DID as well only while callers still
    /// send it — PDS service proxying does until the ecosystem finishes moving over — since a bare
    /// DID names no service in particular.</para>
    /// </remarks>
    public IList<string> Audiences { get; } = new List<string>();

    /// <inheritdoc cref="ServiceAuthVerifierOptions.AllowedKeyIds"/>
    public ISet<string> AllowedKeyIds { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ServiceAuthVerifierOptions.DefaultKeyId };

    /// <inheritdoc cref="ServiceAuthVerifierOptions.RequireLexiconMethod"/>
    public bool RequireLexiconMethod { get; set; } = true;

    /// <inheritdoc cref="ServiceAuthVerifierOptions.ClockSkew"/>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="ServiceAuthVerifierOptions.MaxTokenLifetime"/>
    public TimeSpan MaxTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">An option is missing or invalid.</exception>
    public override void Validate()
    {
        base.Validate();

        if (Audiences.Count == 0)
        {
            throw new InvalidOperationException(
                $"Service auth needs the audiences this service answers to; add at least one to {nameof(Audiences)}, " +
                "such as 'did:web:feed.example.com#bsky_fg'.");
        }

        foreach (var audience in Audiences)
        {
            if (!ServiceAuthSyntax.IsAudience(audience))
            {
                throw new InvalidOperationException(
                    $"A service auth audience is a DID with an optional service fragment; got '{audience}'.");
            }
        }

        if (AllowedKeyIds.Count == 0)
            throw new InvalidOperationException($"Service auth needs at least one of {nameof(AllowedKeyIds)}.");

        foreach (var keyId in AllowedKeyIds)
        {
            if (!ServiceAuthSyntax.IsKeyId(keyId))
            {
                throw new InvalidOperationException(
                    $"An allowed key ID is a verification-method fragment such as '#atproto'; got '{keyId}'.");
            }
        }

        if (ClockSkew < TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(ClockSkew)} cannot be negative.");

        if (MaxTokenLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(MaxTokenLifetime)} must be positive.");
    }

    internal ServiceAuthVerifierOptions CreateVerifierOptions()
    {
        var options = new ServiceAuthVerifierOptions
        {
            RequireLexiconMethod = RequireLexiconMethod,
            ClockSkew = ClockSkew,
            MaxTokenLifetime = MaxTokenLifetime,
        };

        options.AllowedKeyIds.Clear();
        options.AllowedKeyIds.UnionWith(AllowedKeyIds);
        return options;
    }
}

/// <summary>
/// Authenticates a request by the service auth token in its <c>Authorization: Bearer</c> header,
/// binding the token to the XRPC method the endpoint serves — on an endpoint whose authorization
/// asks for this scheme, and nowhere else.
/// </summary>
internal sealed class AtProtoServiceAuthHandler : AuthenticationHandler<AtProtoServiceAuthOptions>
{
    private readonly IDidResolver _resolver;
    private readonly IJtiReplayStore _replayStore;

    public AtProtoServiceAuthHandler(
        IOptionsMonitor<AtProtoServiceAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDidResolver resolver,
        IJtiReplayStore replayStore)
        : base(options, logger, encoder)
    {
        _resolver = resolver;
        _replayStore = replayStore;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization["Bearer ".Length..].Trim();
        if (token.Length == 0)
            return AuthenticateResult.NoResult();

        // Verifying a token spends it. As @atproto/xrpc-server verifies only for a method that
        // declares an auth verifier, a token is left alone on an endpoint that does not ask for
        // this scheme — anonymous, authorized otherwise, or one that checks the token itself, as
        // a space server's notifyWrite does — including when the authentication middleware runs
        // this as the default scheme on every request.
        var endpoint = Context.GetEndpoint();
        if (endpoint is null || !await RequiresThisSchemeAsync(endpoint))
            return AuthenticateResult.NoResult();

        // A token is valid for the one method it names, so an endpoint that serves none has
        // nothing to hold it to. Checked before the token is verified, so it is not spent here.
        if (endpoint.Metadata.GetMetadata<XrpcMethodMetadata>()?.Nsid is not { } method)
        {
            Logger.LogDebug(
                "Refused service auth on {Path}: the endpoint serves no XRPC method to bind the token to",
                Request.Path);

            return AuthenticateResult.Fail(new ServiceAuthException(
                XrpcErrors.AuthenticationRequired, "This endpoint does not accept service auth."));
        }

        var audiences = Options.Audiences as IReadOnlyCollection<string> ?? [.. Options.Audiences];
        var verifier = new ServiceAuthVerifier(_resolver, _replayStore, Options.CreateVerifierOptions(), TimeProvider);

        VerifiedServiceAuth verified;
        try
        {
            verified = await verifier.VerifyAsync(token, audiences, method, Context.RequestAborted);
        }
        catch (ServiceAuthException ex)
        {
            Logger.LogDebug(ex, "Refused a service auth token for {Method}: {Error}", method, ex.Error);
            return AuthenticateResult.Fail(ex);
        }

        List<Claim> claims =
        [
            new(AtProtoServiceAuthDefaults.DidClaimType, verified.Issuer.Value),
            new(AtProtoServiceAuthDefaults.AudienceClaimType, verified.Audience),
        ];

        if (verified.Method is { } scoped)
            claims.Add(new Claim(AtProtoServiceAuthDefaults.LexiconMethodClaimType, scoped.Value));

        var identity = new ClaimsIdentity(
            claims, Scheme.Name, nameType: AtProtoServiceAuthDefaults.DidClaimType, roleType: null);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>
    /// Whether authorizing <paramref name="endpoint"/> authenticates with this scheme: the policy
    /// the authorization middleware will evaluate names it, or names no scheme while this is the
    /// default one.
    /// </summary>
    private async Task<bool> RequiresThisSchemeAsync(Endpoint endpoint)
    {
        // An anonymous endpoint still has its policy's schemes authenticated, to populate the
        // user, so it is excluded here rather than left to the policy.
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return false;

        if (Context.RequestServices.GetService<IAuthorizationPolicyProvider>() is not { } policies)
            return false;

        var policy = await AuthorizationPolicy.CombineAsync(
            policies,
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());

        if (policy is null)
            return false;

        if (policy.AuthenticationSchemes.Count > 0)
            return policy.AuthenticationSchemes.Contains(Scheme.Name, StringComparer.Ordinal);

        var schemes = Context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync();
        return string.Equals(defaultScheme?.Name, Scheme.Name, StringComparison.Ordinal);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Only a refusal of the token itself is described to the caller; anything else that
        // failed authentication is this service's own business.
        var result = await HandleAuthenticateOnceSafeAsync();
        var (error, message) = result.Failure is ServiceAuthException refusal
            ? (refusal.Error, refusal.ErrorMessage ?? refusal.Error)
            : (XrpcErrors.AuthenticationRequired, "Authentication Required");

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append(HeaderNames.WWWAuthenticate, "Bearer");
        await Response.WriteAsJsonAsync(
            new XrpcErrorBody { Error = error, Message = message },
            XrpcJson<XrpcErrorBody>.TypeInfo,
            cancellationToken: Context.RequestAborted);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(
            new XrpcErrorBody { Error = XrpcErrors.Forbidden, Message = "Forbidden" },
            XrpcJson<XrpcErrorBody>.TypeInfo,
            cancellationToken: Context.RequestAborted);
    }
}

/// <summary>
/// Requires the caller to authenticate with a service auth token, bound to the XRPC method the
/// endpoint serves.
/// </summary>
/// <remarks>
/// Put it on an XRPC handler class — <c>MapXrpcEndpoints()</c> carries a handler's attributes
/// over as endpoint metadata — or apply the same to a whole group with
/// <see cref="AtProtoServiceAuthExtensions.RequireServiceAuth{TBuilder}"/>. The caller's DID is
/// then <c>context.User.Identity.Name</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireServiceAuthAttribute : AuthorizeAttribute
{
    /// <summary>Requires service auth under the default scheme name.</summary>
    public RequireServiceAuthAttribute() : this(AtProtoServiceAuthDefaults.AuthenticationScheme)
    {
    }

    /// <summary>Requires service auth under the named scheme.</summary>
    /// <param name="authenticationScheme">The scheme <c>AddAtProtoServiceAuth()</c> registered.</param>
    public RequireServiceAuthAttribute(string authenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        AuthenticationSchemes = authenticationScheme;
    }
}

/// <summary>
/// Registers and applies service auth: the scheme that authenticates calls from other AT Protocol
/// services and apps by their service auth tokens.
/// </summary>
public static class AtProtoServiceAuthExtensions
{
    /// <summary>
    /// Adds the service auth authentication scheme under
    /// <see cref="AtProtoServiceAuthDefaults.AuthenticationScheme"/>.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configure">
    /// Configures the scheme; <see cref="AtProtoServiceAuthOptions.Audiences"/> is required.
    /// </param>
    /// <returns>The authentication builder for chaining.</returns>
    /// <remarks>
    /// <para>A request is authenticated when its <c>Authorization: Bearer</c> token passes
    /// <see cref="ServiceAuthVerifier"/> with the <c>lxm</c> bound to the NSID of the XRPC endpoint
    /// it reached, and the principal carries the <c>did</c>, <c>aud</c> and <c>lxm</c> claims
    /// (<see cref="AtProtoServiceAuthDefaults"/>), with the DID as its name. A refusal is answered
    /// with <c>401</c> and the XRPC error envelope.</para>
    /// <para>A token is verified, and so spent, only on an endpoint whose authorization asks for
    /// this scheme: <see cref="RequireServiceAuthAttribute"/>, <see cref="RequireServiceAuth{TBuilder}"/>,
    /// or any policy naming the scheme — or naming none, while this is the default scheme, which
    /// ASP.NET Core makes it when it is the only one registered. Everywhere else, an
    /// <c>[AllowAnonymous]</c> endpoint included, the token is left untouched: an endpoint that
    /// checks it itself, like a space server's <c>notifyWrite</c>, still finds it unspent. An
    /// endpoint that asks for the scheme but serves no XRPC method fails authentication, since
    /// there is nothing to hold its token to.</para>
    /// <para>This also registers the identity resolvers (<c>AddAtProtoIdentity()</c>), whose cache
    /// the issuer's keys are read through, and an in-process <see cref="IJtiReplayStore"/> unless
    /// one is registered already: replace it with a shared store when more than one instance
    /// serves this DID.</para>
    /// <para>Authentication needs the endpoint, so <c>UseAuthentication()</c> must run after
    /// routing, as it does by default in a <c>WebApplication</c>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAuthentication()
    ///     .AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:feed.example.com#bsky_fg"));
    /// builder.Services.AddAuthorization();
    ///
    /// app.MapXrpcEndpoints().RequireServiceAuth();
    /// </code>
    /// </example>
    public static AuthenticationBuilder AddAtProtoServiceAuth(
        this AuthenticationBuilder builder, Action<AtProtoServiceAuthOptions>? configure = null) =>
        builder.AddAtProtoServiceAuth(AtProtoServiceAuthDefaults.AuthenticationScheme, configure);

    /// <summary>
    /// Adds the service auth authentication scheme under a name of your choosing, for a service
    /// answering to several identities with different settings.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="authenticationScheme">The scheme name.</param>
    /// <param name="configure">
    /// Configures the scheme; <see cref="AtProtoServiceAuthOptions.Audiences"/> is required.
    /// </param>
    /// <returns>The authentication builder for chaining.</returns>
    /// <remarks>
    /// The options are validated when the host starts, so a missing audience fails the start
    /// rather than the first request.
    /// </remarks>
    public static AuthenticationBuilder AddAtProtoServiceAuth(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        Action<AtProtoServiceAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);

        builder.Services.AddAtProtoIdentity();
        builder.Services.TryAddSingleton<IJtiReplayStore, InMemoryJtiReplayStore>();
        builder.Services.AddOptions<AtProtoServiceAuthOptions>(authenticationScheme).ValidateOnStart();

        return builder.AddScheme<AtProtoServiceAuthOptions, AtProtoServiceAuthHandler>(authenticationScheme, configure);
    }

    /// <summary>
    /// Requires service auth on the endpoints <paramref name="builder"/> configures: typically the
    /// group <c>MapXrpcEndpoints()</c> returns.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoints.</param>
    /// <param name="authenticationScheme">The scheme <c>AddAtProtoServiceAuth()</c> registered.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// A handler marked <c>[AllowAnonymous]</c> stays open, and so do the space server's endpoints,
    /// which authenticate their callers themselves. Requires <c>AddAuthorization()</c> and
    /// <c>UseAuthorization()</c>, as any authorization does.
    /// </remarks>
    public static TBuilder RequireServiceAuth<TBuilder>(
        this TBuilder builder, string authenticationScheme = AtProtoServiceAuthDefaults.AuthenticationScheme)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RequireAuthorization(new RequireServiceAuthAttribute(authenticationScheme));
    }
}
