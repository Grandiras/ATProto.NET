using System.Net;

namespace ATProtoNet.Net;

/// <summary>Why an untrusted fetch failed.</summary>
public enum UntrustedFetchError
{
    /// <summary>The policy refused the URL or the address it resolved to; nothing was sent.</summary>
    Blocked,

    /// <summary>The fetch ran out of its <see cref="UntrustedFetchPolicy.RequestTimeout"/>.</summary>
    Timeout,

    /// <summary>The body is larger than the request accepts.</summary>
    ResponseTooLarge,

    /// <summary>The host answered with an HTTP status the caller does not accept.</summary>
    HttpStatus,

    /// <summary>The host could not be reached, or broke off.</summary>
    Network,

    /// <summary>The host redirected where the request may not follow.</summary>
    RedirectRefused,

    /// <summary>The answer is not in the expected format.</summary>
    Malformed,
}

/// <summary>An untrusted fetch failed: the inner exception of every component's own exception when the fetch is what failed.</summary>
public sealed class UntrustedFetchException : AtProtoException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">Why the fetch failed.</param>
    /// <param name="url">The URL fetched.</param>
    /// <param name="message">What failed.</param>
    /// <param name="status">The HTTP status, when the host answered.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public UntrustedFetchException(UntrustedFetchError error, Uri url, string message, HttpStatusCode? status = null, Exception? innerException = null)
        : base(message, innerException) => (Error, Url, Status) = (error, url, status);

    /// <summary>Why the fetch failed.</summary>
    public UntrustedFetchError Error { get; }

    /// <summary>The HTTP status, when the host answered.</summary>
    public HttpStatusCode? Status { get; }

    /// <summary>The URL fetched.</summary>
    public Uri Url { get; }
}
