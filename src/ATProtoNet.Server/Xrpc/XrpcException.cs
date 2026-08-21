using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// A named XRPC error, carrying the wire error name and the status code to answer with.
/// </summary>
/// <remarks>
/// <para>XRPC does not signal failure with a bare status code. Every error response carries a
/// body of <c>{"error": "&lt;name&gt;", "message": "&lt;description&gt;"}</c>, and the
/// <em>name</em> is what a client branches on — a Lexicon declares the names a method may
/// answer with, and clients match on them rather than on the status.</para>
/// <para>Throwing this from an endpoint handler is how a handler produces one: the routing in
/// <see cref="XrpcEndpointExtensions"/> catches it and writes that body, leaving the handler to
/// state what went wrong rather than how to serialize it.</para>
/// </remarks>
public class XrpcException : Exception
{
    /// <summary>
    /// Creates a new XRPC error.
    /// </summary>
    /// <param name="error">The wire error name, e.g. <c>RecordNotFound</c>.</param>
    /// <param name="message">A human-readable description. The error name is used when omitted.</param>
    /// <param name="statusCode">The HTTP status code. Defaults to 400.</param>
    public XrpcException(string error, string? message = null, int statusCode = StatusCodes.Status400BadRequest)
        : base(message ?? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Error = error;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Creates a new XRPC error with an underlying cause.
    /// </summary>
    /// <param name="error">The wire error name, e.g. <c>RecordNotFound</c>.</param>
    /// <param name="message">A human-readable description.</param>
    /// <param name="innerException">The underlying cause.</param>
    /// <param name="statusCode">The HTTP status code. Defaults to 400.</param>
    public XrpcException(
        string error,
        string message,
        Exception innerException,
        int statusCode = StatusCodes.Status400BadRequest)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Error = error;
        StatusCode = statusCode;
    }

    /// <summary>The wire error name, matched by clients.</summary>
    public string Error { get; }

    /// <summary>The HTTP status code to answer with.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// Extra headers to write alongside the error, such as the
    /// <c>WWW-Authenticate</c> a DPoP-authenticated endpoint owes a rejected request.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
