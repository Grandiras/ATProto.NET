using System.Net;

namespace ATProtoNet.Net;

/// <summary>What one untrusted fetch accepts.</summary>
public sealed record UntrustedRequest
{
    /// <summary>The longest success body accepted, in bytes; a longer one is <see cref="UntrustedFetchError.ResponseTooLarge"/>.</summary>
    public required int MaxBodyBytes { get; init; }

    /// <summary>The <c>Accept</c> header, if any.</summary>
    public string? Accept { get; init; }

    /// <summary>How many same-host <c>https</c> redirects a GET follows; any other redirect is <see cref="UntrustedFetchError.RedirectRefused"/>.</summary>
    public int MaxSameHostRedirects { get; init; }
}

/// <summary>The answer to an untrusted fetch.</summary>
public sealed class UntrustedResponse
{
    internal UntrustedResponse(HttpStatusCode status, ReadOnlyMemory<byte> body, Uri finalUrl) => (Status, Body, FinalUrl) = (status, body, finalUrl);

    /// <summary>The HTTP status.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>Whether the status is 2xx.</summary>
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>The body: up to <see cref="UntrustedRequest.MaxBodyBytes"/> of a success, the first <see cref="UntrustedHttp.MaxErrorBodyBytes"/> of anything else.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>The URL that answered, after any redirects followed.</summary>
    public Uri FinalUrl { get; }

    /// <summary>Throws unless the status is 2xx.</summary>
    /// <returns>This response.</returns>
    /// <exception cref="UntrustedFetchException">The status is not 2xx (<see cref="UntrustedFetchError.HttpStatus"/>).</exception>
    public UntrustedResponse EnsureSuccess() => IsSuccess ? this
        : throw new UntrustedFetchException(UntrustedFetchError.HttpStatus, FinalUrl, $"{FinalUrl.Host} answered HTTP {(int)Status}.", Status);
}
