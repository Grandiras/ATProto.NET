using System.Net;

namespace ATProtoNet.Http;

/// <summary>
/// XRPC error names, for matching with <see cref="XrpcException.Is"/>.
/// </summary>
/// <remarks>
/// <para>The first group are the generic names XRPC assigns to HTTP statuses; a service answers
/// with one of them when nothing more specific applies, and the client falls back to them for
/// a response that carried no error envelope. The rest are names that Lexicons of the
/// <c>com.atproto.*</c> namespace declare. Permissioned-space methods have their own set in
/// <see cref="Lexicon.Com.AtProto.Space.SpaceErrors"/>.</para>
/// <para>A Lexicon can declare any name, so this list is not exhaustive: match a custom
/// method's errors with the literal names its Lexicon declares.</para>
/// </remarks>
public static class XrpcErrors
{
    // ─── Generic, by status ────────────────────────────────────────────

    /// <summary>The request was malformed or failed validation (400).</summary>
    public const string InvalidRequest = "InvalidRequest";

    /// <summary>The method requires authentication (401).</summary>
    public const string AuthenticationRequired = "AuthenticationRequired";

    /// <summary>The caller lacks permission for the method (403).</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>The service does not implement XRPC, or not this method (404).</summary>
    public const string XrpcNotSupported = "XRPCNotSupported";

    /// <summary>The request body was too large (413).</summary>
    public const string PayloadTooLarge = "PayloadTooLarge";

    /// <summary>A rate limit was exceeded (429).</summary>
    public const string RateLimitExceeded = "RateLimitExceeded";

    /// <summary>The service failed internally (500).</summary>
    public const string InternalServerError = "InternalServerError";

    /// <summary>The method is known but not implemented (501).</summary>
    public const string MethodNotImplemented = "MethodNotImplemented";

    /// <summary>An upstream service failed (502).</summary>
    public const string UpstreamFailure = "UpstreamFailure";

    /// <summary>The service is overloaded or unavailable (503).</summary>
    public const string NotEnoughResources = "NotEnoughResources";

    /// <summary>An upstream service timed out (504).</summary>
    public const string UpstreamTimeout = "UpstreamTimeout";

    /// <summary>A status outside the ones XRPC names.</summary>
    public const string Unknown = "Unknown";

    // ─── Authentication and session ────────────────────────────────────

    /// <summary>The access token has expired; refreshing the session recovers.</summary>
    public const string ExpiredToken = "ExpiredToken";

    /// <summary>The token is malformed, revoked, or of the wrong kind.</summary>
    public const string InvalidToken = "InvalidToken";

    /// <summary>A method that needs credentials was called without any.</summary>
    public const string AuthMissing = "AuthMissing";

    /// <summary><c>createSession</c>: the account needs a second factor; retry with the emailed token.</summary>
    public const string AuthFactorTokenRequired = "AuthFactorTokenRequired";

    /// <summary>The account has been taken down.</summary>
    public const string AccountTakedown = "AccountTakedown";

    // ─── Identity ──────────────────────────────────────────────────────

    /// <summary>The handle does not resolve to any DID.</summary>
    public const string HandleNotFound = "HandleNotFound";

    /// <summary>The DID has no current DID document.</summary>
    public const string DidNotFound = "DidNotFound";

    /// <summary>The DID existed but has been deactivated.</summary>
    public const string DidDeactivated = "DidDeactivated";

    // ─── Repositories and records ──────────────────────────────────────

    /// <summary><c>getRecord</c>: no record exists at the given key.</summary>
    public const string RecordNotFound = "RecordNotFound";

    /// <summary>A write's <c>swapRecord</c> or <c>swapCommit</c> no longer matches (compare-and-swap failed).</summary>
    public const string InvalidSwap = "InvalidSwap";

    /// <summary>The repository does not exist on this service.</summary>
    public const string RepoNotFound = "RepoNotFound";

    /// <summary>The repository has been taken down.</summary>
    public const string RepoTakendown = "RepoTakendown";

    /// <summary>The repository's account is suspended.</summary>
    public const string RepoSuspended = "RepoSuspended";

    /// <summary>The repository's account is deactivated.</summary>
    public const string RepoDeactivated = "RepoDeactivated";

    /// <summary>The blob does not exist in the repository.</summary>
    public const string BlobNotFound = "BlobNotFound";

    /// <summary>
    /// The generic name XRPC gives an HTTP status, used when a response carried no error body.
    /// </summary>
    internal static string ForStatus(HttpStatusCode status) => (int)status switch
    {
        400 => InvalidRequest,
        401 => AuthenticationRequired,
        403 => Forbidden,
        404 => XrpcNotSupported,
        413 => PayloadTooLarge,
        429 => RateLimitExceeded,
        500 => InternalServerError,
        501 => MethodNotImplemented,
        502 => UpstreamFailure,
        503 => NotEnoughResources,
        504 => UpstreamTimeout,
        _ => Unknown,
    };
}
