using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Marker interface for all XRPC endpoint handlers.
/// </summary>
public interface IXrpcEndpoint
{
    /// <summary>
    /// The Lexicon NSID this handler serves (e.g., "com.example.myQuery").
    /// </summary>
    string Nsid { get; }
}

/// <summary>
/// An XRPC query endpoint (HTTP GET at /xrpc/{nsid}).
/// Implement this interface to handle query requests with typed parameters and output.
/// </summary>
/// <typeparam name="TParams">The query parameters type (deserialized from query string).</typeparam>
/// <typeparam name="TOutput">The response type (serialized to JSON).</typeparam>
public interface IXrpcQuery<TParams, TOutput> : IXrpcEndpoint
    where TParams : class
    where TOutput : class
{
    /// <summary>
    /// Handle the query request.
    /// </summary>
    /// <param name="parameters">The deserialized query parameters.</param>
    /// <param name="context">The HTTP context for accessing auth, headers, etc.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response object.</returns>
    Task<TOutput> HandleAsync(TParams parameters, HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// An XRPC query endpoint with no parameters.
/// </summary>
/// <typeparam name="TOutput">The response type.</typeparam>
public interface IXrpcQuery<TOutput> : IXrpcEndpoint
    where TOutput : class
{
    /// <summary>
    /// Handle the query request.
    /// </summary>
    Task<TOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// An XRPC procedure endpoint (HTTP POST at /xrpc/{nsid}).
/// Implement this interface to handle procedure requests with typed input and output.
/// </summary>
/// <typeparam name="TInput">The request body type (deserialized from JSON).</typeparam>
/// <typeparam name="TOutput">The response type (serialized to JSON).</typeparam>
public interface IXrpcProcedure<TInput, TOutput> : IXrpcEndpoint
    where TInput : class
    where TOutput : class
{
    /// <summary>
    /// Handle the procedure request.
    /// </summary>
    /// <param name="input">The deserialized request body.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response object.</returns>
    Task<TOutput> HandleAsync(TInput input, HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// An XRPC procedure endpoint with no output (returns HTTP 200 with no body).
/// </summary>
/// <typeparam name="TInput">The request body type.</typeparam>
public interface IXrpcProcedureVoid<TInput> : IXrpcEndpoint
    where TInput : class
{
    /// <summary>
    /// Handle the procedure request.
    /// </summary>
    Task HandleAsync(TInput input, HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// The body of a binary XRPC response: the bytes, and what they are.
/// </summary>
/// <param name="Content">The response body. The routing disposes it after writing.</param>
/// <param name="ContentType">The MIME type, e.g. <c>application/vnd.ipld.car</c>.</param>
/// <param name="ContentLength">The body length when known, so the response can be sized.</param>
public sealed record XrpcBlobResult(Stream Content, string ContentType, long? ContentLength = null);

/// <summary>
/// An XRPC query endpoint that answers with bytes rather than JSON (HTTP GET at /xrpc/{nsid}).
/// </summary>
/// <remarks>
/// Lexicon methods whose output is an <c>encoding</c> other than <c>application/json</c> —
/// <c>getBlob</c>, <c>getRepo</c>, and the CAR-serving sync methods — implement this instead of
/// <see cref="IXrpcQuery{TParams, TOutput}"/>. Errors are still JSON: throw
/// <see cref="XrpcException"/> and the routing writes the usual error body.
/// </remarks>
/// <typeparam name="TParams">The query parameters type (deserialized from query string).</typeparam>
public interface IXrpcBlobQuery<TParams> : IXrpcEndpoint
    where TParams : class
{
    /// <summary>
    /// Handle the query request.
    /// </summary>
    /// <param name="parameters">The deserialized query parameters.</param>
    /// <param name="context">The HTTP context for accessing auth, headers, etc.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response body and its content type.</returns>
    Task<XrpcBlobResult> HandleAsync(TParams parameters, HttpContext context, CancellationToken cancellationToken = default);
}
