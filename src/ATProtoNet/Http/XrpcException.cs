using System.Net;

namespace ATProtoNet.Http;

/// <summary>
/// A named XRPC error: the <c>{"error", "message"}</c> body an XRPC service answers a failed
/// call with, together with its HTTP status.
/// </summary>
/// <remarks>
/// <para>XRPC does not signal failure with a bare status code. Every error response carries an
/// error <em>name</em> — a Lexicon declares the names a method may answer with — and that name,
/// not the status, is what a caller branches on: <c>catch (XrpcException ex) when
/// (ex.Is(XrpcErrors.RecordNotFound))</c>. <see cref="XrpcErrors"/> holds the common names.</para>
/// <para>One type serves both sides. The client throws it for every non-success response, with
/// <see cref="Nsid"/>, <see cref="ResponseBody"/> and the response <see cref="Headers"/> filled
/// in; a 429 surfaces as <see cref="XrpcRateLimitException"/> and a rejected credential as
/// <see cref="XrpcAuthenticationException"/>. An XRPC endpoint hosted with
/// <c>ATProtoNet.Server</c> throws it to answer with a named error, and the routing writes the
/// body, the status and any <see cref="Headers"/>.</para>
/// </remarks>
public class XrpcException : AtProtoException
{
    /// <summary>
    /// Creates an XRPC error, as an endpoint handler raises one.
    /// </summary>
    /// <param name="error">The wire error name, e.g. <c>RecordNotFound</c>.</param>
    /// <param name="message">A human-readable description, if any.</param>
    /// <param name="statusCode">The HTTP status. Defaults to 400.</param>
    public XrpcException(string error, string? message = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : this(error, message, statusCode, nsid: null)
    {
    }

    /// <summary>
    /// Creates an XRPC error with an underlying cause.
    /// </summary>
    /// <param name="error">The wire error name, e.g. <c>RecordNotFound</c>.</param>
    /// <param name="message">A human-readable description.</param>
    /// <param name="innerException">The underlying cause.</param>
    /// <param name="statusCode">The HTTP status. Defaults to 400.</param>
    public XrpcException(
        string error,
        string message,
        Exception innerException,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : this(error, message, statusCode, nsid: null, innerException)
    {
    }

    /// <summary>
    /// Creates an XRPC error for a call to a named method, as the client raises one.
    /// </summary>
    /// <param name="error">The wire error name, e.g. <c>RecordNotFound</c>.</param>
    /// <param name="message">A human-readable description, if any.</param>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="nsid">The method that failed, if known.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public XrpcException(
        string error,
        string? message,
        HttpStatusCode statusCode,
        string? nsid,
        Exception? innerException = null)
        : base(FormatMessage(error, message, statusCode, nsid), innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Error = error;
        ErrorMessage = message;
        StatusCode = statusCode;
        Nsid = nsid;
    }

    /// <summary>
    /// The NSID of the method that failed. Set on the client side; <see langword="null"/> for an
    /// error an endpoint handler raises.
    /// </summary>
    public string? Nsid { get; }

    /// <summary>
    /// The XRPC error name. When a response carried no error envelope — a proxy's HTML error
    /// page, say — this is the generic name for its status, such as
    /// <see cref="XrpcErrors.InternalServerError"/> for a 500.
    /// </summary>
    public string Error { get; }

    /// <summary>The human-readable description from the error body, if it carried one.</summary>
    public string? ErrorMessage { get; }

    /// <summary>The HTTP status the error was, or is to be, answered with.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body, when the client read one.</summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    /// On the client, the headers of the failed response — <c>WWW-Authenticate</c>,
    /// <c>Retry-After</c>, <c>RateLimit-*</c>. On the server, extra headers to write with the
    /// error, such as the <c>WWW-Authenticate</c> a DPoP-authenticated endpoint owes a rejected
    /// request.
    /// </summary>
    public IDictionary<string, string> Headers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this is the error named <paramref name="error"/>.</summary>
    /// <param name="error">An XRPC error name, typically one of <see cref="XrpcErrors"/>.</param>
    /// <returns><see langword="true"/> when <see cref="Error"/> equals it (case-sensitively, as on the wire).</returns>
    public bool Is(string error) => string.Equals(Error, error, StringComparison.Ordinal);

    private static string FormatMessage(string error, string? message, HttpStatusCode statusCode, string? nsid) =>
        $"{nsid ?? "XRPC call"} failed with {(int)statusCode} {error}" +
        (string.IsNullOrEmpty(message) ? "." : $": {message}");
}

/// <summary>
/// The service refused a call because the client exceeded a rate limit (HTTP 429), and waiting
/// it out was not possible within <see cref="XrpcRateLimitOptions"/>.
/// </summary>
/// <remarks>
/// The client retries a 429 on its own, up to <see cref="XrpcRateLimitOptions.MaxRetries"/>
/// times, as long as the wait the service asks for fits in
/// <see cref="XrpcRateLimitOptions.MaxDelay"/>. This is thrown when it does not — a daily
/// window, typically — or when the retries run out; <see cref="RetryAfter"/> says how long the
/// service wants the caller to wait.
/// </remarks>
public sealed class XrpcRateLimitException : XrpcException
{
    /// <summary>
    /// Creates a rate-limit error.
    /// </summary>
    /// <param name="error">The wire error name, usually <see cref="XrpcErrors.RateLimitExceeded"/>.</param>
    /// <param name="message">A human-readable description, if any.</param>
    /// <param name="nsid">The method that was refused, if known.</param>
    /// <param name="retryAfter">How long the service asked the client to wait, if it said.</param>
    /// <param name="rateLimit">The rate-limit headers of the response, if it sent any.</param>
    public XrpcRateLimitException(
        string error,
        string? message,
        string? nsid,
        TimeSpan? retryAfter,
        RateLimitInfo? rateLimit)
        : base(error, message, HttpStatusCode.TooManyRequests, nsid)
    {
        RetryAfter = retryAfter;
        RateLimit = rateLimit;
    }

    /// <summary>
    /// How long the service asked the client to wait, from <c>Retry-After</c> or, failing that,
    /// <c>RateLimit-Reset</c>. <see langword="null"/> when the response carried neither.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The <c>RateLimit-*</c> headers of the response, if it sent any.</summary>
    public RateLimitInfo? RateLimit { get; }
}

/// <summary>
/// The service rejected the call's credentials: an HTTP 401, or one of the token errors a PDS
/// answers with a 400 (<see cref="XrpcErrors.ExpiredToken"/>,
/// <see cref="XrpcErrors.InvalidToken"/>).
/// </summary>
/// <remarks>
/// An expired access token is the common case, and refreshing the session recovers it. A DPoP
/// nonce challenge (<c>use_dpop_nonce</c>) never surfaces here: the client answers it by
/// retrying once with the nonce the service supplied.
/// </remarks>
public sealed class XrpcAuthenticationException : XrpcException
{
    /// <summary>
    /// Creates an authentication error.
    /// </summary>
    /// <param name="error">The wire error name.</param>
    /// <param name="message">A human-readable description, if any.</param>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="nsid">The method that was refused, if known.</param>
    public XrpcAuthenticationException(string error, string? message, HttpStatusCode statusCode, string? nsid)
        : base(error, message, statusCode, nsid)
    {
    }
}

/// <summary>
/// A service answered an XRPC call with a success status but a body that does not deserialize
/// into the type the method's Lexicon describes.
/// </summary>
/// <remarks>
/// This is a contract violation by the service (or a model the SDK has wrong), not an XRPC
/// error, so it carries no error name. The <see cref="Exception.InnerException"/> is the
/// <see cref="System.Text.Json.JsonException"/> that located the problem.
/// </remarks>
public sealed class XrpcResponseFormatException : AtProtoException
{
    /// <summary>
    /// Creates a response-format error.
    /// </summary>
    /// <param name="nsid">The method whose response did not match.</param>
    /// <param name="message">A description of the mismatch.</param>
    /// <param name="innerException">The underlying deserialization failure, if any.</param>
    public XrpcResponseFormatException(string nsid, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nsid);
        Nsid = nsid;
    }

    /// <summary>The NSID of the method whose response did not match its Lexicon.</summary>
    public string Nsid { get; }
}
