using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// ASP.NET Core authentication handler that validates AT Protocol JWT access tokens.
/// </summary>
/// <remarks>
/// <para>This handler extracts the Bearer token from the Authorization header
/// and validates it against the configured PDS. It sets up claims including
/// the user's DID, handle, and scope.</para>
/// <para>Register with <c>services.AddAuthentication().AddAtProto();</c></para>
/// </remarks>
public class AtProtoAuthenticationHandler : AuthenticationHandler<AtProtoAuthenticationOptions>
{
    private readonly AtProtoClient _client;

    /// <summary>
    /// Creates a new instance of <see cref="AtProtoAuthenticationHandler"/>.
    /// </summary>
    public AtProtoAuthenticationHandler(
        IOptionsMonitor<AtProtoAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AtProtoClient client)
        : base(options, logger, encoder)
    {
        _client = client;
    }

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        try
        {
            // Validate the token by using it to call getSession
            // This is a simple strategy; production apps may decode JWT locally
            var tempClient = new AtProtoClientBuilder()
                .WithInstanceUrl(Options.PdsUrl ?? "https://bsky.social")
                .WithAutoRefreshSession(false)
                .Build();

            // Manually set the access token for validation
            var session = new Auth.Session
            {
                Did = string.Empty,
                Handle = string.Empty,
                AccessJwt = token,
                RefreshJwt = string.Empty,
            };

            await tempClient.ResumeSessionAsync(session);

            if (!tempClient.IsAuthenticated || tempClient.Session is null)
                return AuthenticateResult.Fail("Invalid AT Protocol token");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, tempClient.Session.Did),
                new("did", tempClient.Session.Did),
                new("handle", tempClient.Session.Handle),
            };

            if (tempClient.Session.Email is not null)
                claims.Add(new Claim(ClaimTypes.Email, tempClient.Session.Email));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AT Protocol token validation failed");
            return AuthenticateResult.Fail("Token validation failed");
        }
    }
}

/// <summary>
/// Options for AT Protocol authentication.
/// </summary>
public class AtProtoAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The PDS URL to validate tokens against.
    /// Default: "https://bsky.social"
    /// </summary>
    public string? PdsUrl { get; set; }
}

/// <summary>
/// Extension methods for configuring AT Protocol authentication.
/// </summary>
public static class AtProtoAuthenticationExtensions
{
    /// <summary>The default authentication scheme name.</summary>
    public const string DefaultScheme = "ATProto";

    /// <summary>
    /// Add AT Protocol authentication to the authentication builder.
    /// </summary>
    public static AuthenticationBuilder AddAtProto(
        this AuthenticationBuilder builder,
        Action<AtProtoAuthenticationOptions>? configure = null)
    {
        return builder.AddAtProto(DefaultScheme, configure);
    }

    /// <summary>
    /// Add AT Protocol authentication with a custom scheme name.
    /// </summary>
    public static AuthenticationBuilder AddAtProto(
        this AuthenticationBuilder builder,
        string scheme,
        Action<AtProtoAuthenticationOptions>? configure = null)
    {
        return builder.AddScheme<AtProtoAuthenticationOptions, AtProtoAuthenticationHandler>(
            scheme, configure);
    }
}
