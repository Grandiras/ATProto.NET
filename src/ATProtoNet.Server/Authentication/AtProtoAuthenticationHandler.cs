using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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

        // Local pre-validation: reject malformed JWTs, alg=none, and tokens that
        // are expired or not-yet-valid before paying for a network round-trip to
        // the PDS. Cryptographic signature verification still happens at the PDS
        // (it owns the signing key), but we refuse to forward obviously bad tokens.
        if (!TryPreValidateJwt(token, out var failureReason))
        {
            Logger.LogDebug("JWT pre-validation failed: {Reason}", failureReason);
            return AuthenticateResult.Fail(failureReason);
        }

        try
        {
            // Signature/identity validation is delegated to the PDS via getSession.
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

    /// <summary>
    /// Cheap local pre-checks on a JWT structure: three base64url-encoded segments,
    /// `alg` is not <c>none</c>, and the `exp`/`nbf` window (if present) covers now.
    /// Does NOT verify the signature — that's owned by the PDS in this delegated flow.
    /// </summary>
    private static bool TryPreValidateJwt(string token, out string failureReason)
    {
        failureReason = "";
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            failureReason = "Token is not a JWT (expected 3 segments).";
            return false;
        }

        try
        {
            using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));

            // RFC 7515 §4.1.1 makes `alg` REQUIRED. A header without `alg` is
            // malformed and an `alg=none` token must be refused before forwarding.
            if (!header.RootElement.TryGetProperty("alg", out var alg))
            {
                failureReason = "Token header is missing required 'alg' claim.";
                return false;
            }

            var algValue = alg.GetString();
            if (string.IsNullOrEmpty(algValue))
            {
                failureReason = "Token header has empty 'alg' claim.";
                return false;
            }

            // Allowlist asymmetric algorithms only. atproto JWTs are signed by the
            // user's PDS using ECDSA / EdDSA / RSA keys distributed via DID
            // documents; symmetric (HS*) algorithms have no place here and rejecting
            // alg=none alone leaves HS256 forgeries reaching the PDS for every
            // unauthenticated request, defeating the pre-validator's purpose.
            if (!IsAllowedJwtAlgorithm(algValue))
            {
                failureReason = $"Token uses unacceptable algorithm '{algValue}'.";
                return false;
            }

            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (payload.RootElement.TryGetProperty("exp", out var exp) &&
                TryGetUnixTime(exp, out var expValue) && now > expValue)
            {
                failureReason = "Token is expired.";
                return false;
            }

            if (payload.RootElement.TryGetProperty("nbf", out var nbf) &&
                TryGetUnixTime(nbf, out var nbfValue) && now + 60 < nbfValue)
            {
                failureReason = "Token is not yet valid.";
                return false;
            }
        }
        catch (Exception ex)
        {
            failureReason = $"Token segments are not valid base64url JSON ({ex.GetType().Name}).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Allowlist of JWT signing algorithms acceptable on an atproto access JWT.
    /// Asymmetric only (ES*, EdDSA, RS*, PS*); HS* are excluded because a
    /// resource server validating an attacker-forged HMAC is meaningless when
    /// the signer is meant to be the user's PDS keypair.
    /// </summary>
    private static bool IsAllowedJwtAlgorithm(string alg) => alg switch
    {
        "ES256" or "ES256K" or "ES384" or "ES512" => true,
        "EdDSA" => true,
        "RS256" or "RS384" or "RS512" => true,
        "PS256" or "PS384" or "PS512" => true,
        _ => false,
    };

    /// <summary>
    /// Reads a NumericDate-style JWT claim, accepting both JSON numbers and
    /// stringified numbers (some IdPs emit `"exp":"1700000000"`).
    /// </summary>
    private static bool TryGetUnixTime(JsonElement element, out long value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt64(out value)) return true;
                if (element.TryGetDouble(out var asDouble))
                {
                    value = (long)asDouble;
                    return true;
                }
                break;
            case JsonValueKind.String:
                var asString = element.GetString();
                if (long.TryParse(asString, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                    return true;
                if (double.TryParse(asString, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dblFromString))
                {
                    value = (long)dblFromString;
                    return true;
                }
                break;
        }
        value = 0;
        return false;
    }

    private static byte[] DecodeBase64Url(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
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
