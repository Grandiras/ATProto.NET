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
