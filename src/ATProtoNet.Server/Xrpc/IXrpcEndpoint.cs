using ATProtoNet.Identity;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// The base of every XRPC endpoint handler: the Lexicon method it serves.
/// </summary>
/// <remarks>
/// A handler implements exactly one of the shapes below, which decides the HTTP method and how
/// the request and response are carried. The NSID is static so the routing can read it at
/// registration without constructing the handler; handlers are constructed per request, from DI.
/// </remarks>
/// <example>
/// <code>
/// public sealed class GetStatusEndpoint : IXrpcQuery&lt;StatusOutput&gt;
/// {
///     public static Nsid Nsid { get; } = Nsid.Parse("com.example.getStatus");
///
///     public Task&lt;StatusOutput&gt; HandleAsync(HttpContext context, CancellationToken ct) =&gt; …;
/// }
/// </code>
/// </example>
public interface IXrpcEndpoint
{
    /// <summary>
    /// The Lexicon NSID this handler serves (e.g., <c>com.example.getStatus</c>). The route is
    /// <c>/xrpc/{nsid}</c>.
    /// </summary>
    static abstract Nsid Nsid { get; }
}

/// <summary>
/// An XRPC query endpoint (HTTP GET at /xrpc/{nsid}).
/// Implement this interface to handle query requests with typed parameters and output.
/// </summary>
/// <remarks>
/// Parameters bind from the query string by each property's type: a collection property takes
/// every value of its key (<c>?uris=a&amp;uris=b</c>, <c>uris[]=a</c>, or a single <c>?uris=a</c>),
/// a scalar property takes exactly one, and an identifier property such as <see cref="Did"/> or
/// <see cref="AtUri"/> is validated by its parser. A value that does not bind answers
/// <c>InvalidRequest</c>.
/// </remarks>
/// <typeparam name="TParams">The query parameters type (bound from the query string).</typeparam>
/// <typeparam name="TOutput">The response type (serialized to JSON).</typeparam>
public interface IXrpcQuery<TParams, TOutput> : IXrpcEndpoint
    where TParams : class
    where TOutput : class
{
    /// <summary>
    /// Handle the query request.
    /// </summary>
    /// <param name="parameters">The bound query parameters.</param>
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
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response object.</returns>
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
/// An XRPC procedure endpoint that takes no input (HTTP POST at /xrpc/{nsid}). Any request body
/// is ignored.
/// </summary>
/// <typeparam name="TOutput">The response type (serialized to JSON).</typeparam>
public interface IXrpcProcedure<TOutput> : IXrpcEndpoint
    where TOutput : class
{
    /// <summary>
    /// Handle the procedure request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response object.</returns>
    Task<TOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken = default);
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
    /// <param name="input">The deserialized request body.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the procedure has run.</returns>
    Task HandleAsync(TInput input, HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// An XRPC procedure endpoint with neither input nor output, such as
/// <c>com.atproto.server.deleteSession</c> (returns HTTP 200 with no body). Any request body is
/// ignored.
/// </summary>
public interface IXrpcProcedureVoid : IXrpcEndpoint
{
    /// <summary>
    /// Handle the procedure request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the procedure has run.</returns>
    Task HandleAsync(HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// The body of a binary XRPC request: the bytes, and what they are.
/// </summary>
/// <param name="Content">
/// The request body, read as it arrives. It is not buffered, and belongs to the request: read it
/// within the handler, and do not dispose it.
/// </param>
/// <param name="ContentType">The MIME type the client declared, e.g. <c>image/png</c>.</param>
/// <param name="ContentLength">The declared body length, when the client sent one.</param>
public sealed record XrpcBlobInput(Stream Content, string ContentType, long? ContentLength);

/// <summary>
/// An XRPC procedure endpoint whose input is bytes rather than JSON (HTTP POST at
/// /xrpc/{nsid}), such as <c>com.atproto.repo.uploadBlob</c>.
/// </summary>
/// <remarks>
/// A request without a <c>Content-Type</c> answers <c>InvalidRequest</c> before the handler
/// runs. The body size is bounded by the server's request-size limit (Kestrel's
/// <c>MaxRequestBodySize</c>, or <c>[RequestSizeLimit]</c> on the handler class); reading past it
/// answers <c>PayloadTooLarge</c>.
/// </remarks>
/// <typeparam name="TOutput">The response type (serialized to JSON).</typeparam>
public interface IXrpcBlobProcedure<TOutput> : IXrpcEndpoint
    where TOutput : class
{
    /// <summary>
    /// Handle the procedure request.
    /// </summary>
    /// <param name="input">The request body and its content type.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response object.</returns>
    Task<TOutput> HandleAsync(XrpcBlobInput input, HttpContext context, CancellationToken cancellationToken = default);
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
/// <see cref="ATProtoNet.Http.XrpcException"/> and the routing writes the usual error body.
/// </remarks>
/// <typeparam name="TParams">The query parameters type (bound from the query string).</typeparam>
public interface IXrpcBlobQuery<TParams> : IXrpcEndpoint
    where TParams : class
{
    /// <summary>
    /// Handle the query request.
    /// </summary>
    /// <param name="parameters">The bound query parameters.</param>
    /// <param name="context">The HTTP context for accessing auth, headers, etc.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response body and its content type.</returns>
    Task<XrpcBlobResult> HandleAsync(TParams parameters, HttpContext context, CancellationToken cancellationToken = default);
}
