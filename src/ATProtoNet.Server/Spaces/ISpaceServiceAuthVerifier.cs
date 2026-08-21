using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A verified inter-service authentication token.
/// </summary>
/// <param name="Issuer">The calling service's DID.</param>
/// <param name="Audience">The DID it addressed.</param>
/// <param name="Method">The <c>lxm</c> it was scoped to, when it named one.</param>
public sealed record VerifiedServiceAuth(string Issuer, string Audience, string? Method);

/// <summary>
/// Verifies the service auth tokens on the notification endpoints.
/// </summary>
/// <remarks>
/// Write notifications are not carried by a space credential. The caller is a PDS acting as
/// itself, telling an authority that one of the repos it hosts advanced, so it authenticates
/// with the ordinary AT Protocol
/// <see href="https://atproto.com/specs/xrpc#service-auth">service auth</see> a feed generator or
/// labeler already uses: a short-lived JWT with <c>iss</c>, <c>aud</c>, and <c>lxm</c>, signed
/// with the issuer's <c>#atproto</c> key.
/// </remarks>
public interface ISpaceServiceAuthVerifier
{
    /// <summary>
    /// Verifies the <c>Authorization: Bearer</c> service auth token on a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="expectedAudience">The DID this service answers to.</param>
    /// <param name="expectedMethod">The <c>lxm</c> the token must be scoped to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    Task<VerifiedServiceAuth> VerifyAsync(
        HttpContext context, string expectedAudience, string expectedMethod,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether a service speaks for an account's permissioned repos.
    /// </summary>
    /// <param name="serviceDid">The calling service's DID.</param>
    /// <param name="repoDid">The account whose repo the call concerns.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Without this check any service could advance any account's revision in a space's writer
    /// set, which is what a syncer decides from whether to re-read a repo.
    /// </remarks>
    Task<bool> IsRepoHostAsync(string serviceDid, string repoDid, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISpaceServiceAuthVerifier"/>, resolving keys and endpoints from DID
/// documents.
/// </summary>
public sealed class SpaceServiceAuthVerifier : ISpaceServiceAuthVerifier
{
    private readonly ISpaceDidDocumentResolver _resolver;
    private readonly ISpaceReplayStore _replayStore;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">Resolves the caller's DID document.</param>
    /// <param name="replayStore">Consumes each token's <c>jti</c>.</param>
    /// <param name="options">Server options.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceServiceAuthVerifier(
        ISpaceDidDocumentResolver resolver,
        ISpaceReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(replayStore);

        _resolver = resolver;
        _replayStore = replayStore;
        _options = options ?? new SpaceServerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<VerifiedServiceAuth> VerifyAsync(
        HttpContext context,
        string expectedAudience,
        string expectedMethod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMethod);

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw Invalid("A write notification is authenticated with a Bearer service auth token.");

        var jwt = header["Bearer ".Length..].Trim();
        var parts = jwt.Split('.');
        if (parts.Length != 3)
            throw Invalid("Malformed service auth token: expected three parts.");

        var payload = DecodeJson(parts[1]);

        var issuer = GetString(payload, "iss") ?? throw Invalid("The service auth token is missing its \"iss\".");
        var audience = GetString(payload, "aud") ?? throw Invalid("The service auth token is missing its \"aud\".");

        if (!string.Equals(audience, expectedAudience, StringComparison.Ordinal))
            throw Invalid($"The service auth token is addressed to '{audience}', not to '{expectedAudience}'.");

        var method = GetString(payload, "lxm");
        if (method is not null && !string.Equals(method, expectedMethod, StringComparison.Ordinal))
            throw Invalid($"The service auth token is scoped to '{method}', not to '{expectedMethod}'.");

        if (!payload.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds))
            throw Invalid("The service auth token is missing its \"exp\".");

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
        var now = _timeProvider.GetUtcNow();
        if (expiresAt <= now - _options.ClockSkew)
            throw Invalid("The service auth token is expired.");

        // Service auth is short-lived by convention, but its `exp` is its issuer's own choice
        // and the issuer here is any DID that can sign — the check that it hosts the repo comes
        // later. Without a ceiling, a token dated years ahead is replayable for years and holds
        // its jti in the replay store for just as long.
        if (!_options.IsWithinSingleUseWindow(expiresAt, now))
        {
            throw Invalid(
                $"The service auth token is valid for longer than the {_options.MaxSingleUseTokenLifetime} " +
                "this service accepts.");
        }

        // An `iat` is optional, but one dated in the future is not a fresh token.
        if (payload.TryGetProperty("iat", out var iat) && iat.TryGetInt64(out var iatSeconds) &&
            DateTimeOffset.FromUnixTimeSeconds(iatSeconds) > now + _options.ClockSkew)
        {
            throw Invalid("The service auth token is dated in the future.");
        }

        var signerKey = await _resolver.ResolveAccountKeyAsync(
            issuer, keyId: null, SpaceErrors.NotAuthorized, cancellationToken);

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = DecodeBase64Url(parts[2]);

        bool valid;
        try
        {
            valid = AtProtoCrypto.VerifySignature(signerKey, signingInput, signature);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not verify the service auth signature: {ex.Message}", ex);
        }

        if (!valid)
            throw Invalid("The service auth token's signature does not verify.");

        // A jti is optional in the AT Protocol service auth spec, so its absence is not an error
        // — but when one is present it is spent, so a captured token cannot be re-delivered.
        if (GetString(payload, "jti") is { } tokenId &&
            !await _replayStore.TryConsumeAsync(issuer, tokenId, expiresAt, cancellationToken))
        {
            throw Invalid("The service auth token has already been used.");
        }

        return new VerifiedServiceAuth(issuer, audience, method);
    }

    /// <inheritdoc/>
    public async Task<bool> IsRepoHostAsync(
        string serviceDid, string repoDid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDid);

        // The usual AT Protocol shape: a PDS signs service auth with the account's own key, so
        // the issuer is the account rather than the host.
        if (string.Equals(serviceDid, repoDid, StringComparison.Ordinal))
            return true;

        var repoDocument = await _resolver.ResolveAsync(repoDid, cancellationToken);
        var repoHost = SpaceAuthority.GetHostEndpoint(repoDocument);
        if (repoHost is null)
            return false;

        var serviceDocument = await _resolver.ResolveAsync(serviceDid, cancellationToken);

        // A host may publish several service entries; any of them answering at the same origin
        // as the repo's host is the same service.
        return serviceDocument.Service.Any(s => SameOrigin(s.Endpoint, repoHost));
    }

    private static bool SameOrigin(string? left, string? right) =>
        Uri.TryCreate(left, UriKind.Absolute, out var a) &&
        Uri.TryCreate(right, UriKind.Absolute, out var b) &&
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);

    private static JsonElement DecodeJson(string part)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(DecodeBase64Url(part));
        }
        catch (JsonException ex)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not parse the service auth token: {ex.Message}", ex);
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            var padding = (4 - (padded.Length % 4)) % 4;
            return Convert.FromBase64String(padding == 0 ? padded : padded + new string('=', padding));
        }
        catch (FormatException ex)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not decode the service auth token: {ex.Message}", ex);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
