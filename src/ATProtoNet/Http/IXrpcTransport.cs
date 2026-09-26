using System.Runtime.CompilerServices;
using ATProtoNet.Identity;

namespace ATProtoNet.Http;

/// <summary>
/// Sends XRPC calls the way an <see cref="AtProtoClient"/> does: to its service, with its
/// installed session (refreshed when it expires), its rate-limit handling and its error model.
/// </summary>
/// <remarks>
/// <para>This is the seam for Lexicons the SDK does not ship. A package for another namespace
/// builds its sub-client on <see cref="AtProtoClient.Transport"/>, so its calls share the
/// client's service, session and settings, and follow a sign-in, refresh or sign-out without
/// any wiring of their own. <c>docs/custom-xrpc.md</c> shows one end to end.</para>
/// <para>Every call throws <see cref="XrpcException"/> when the service answers with an XRPC
/// error (<see cref="XrpcAuthenticationException"/> and <see cref="XrpcRateLimitException"/> for
/// the credential and rate-limit cases), <see cref="XrpcResponseFormatException"/> when a success
/// body is not the expected type, and <see cref="TimeoutException"/> when
/// <see cref="XrpcCallOptions.Timeout"/> expires. Request and response bodies are JSON, written
/// and read with <see cref="Serialization.AtProtoJsonDefaults.Options"/>; a request body is
/// serialized by its runtime type.</para>
/// <para>Implementations are thread-safe. The interface is public so a sub-client can be tested
/// against a fake; the SDK's own implementation is the only one it uses.</para>
/// </remarks>
public interface IXrpcTransport
{
    /// <summary>Calls an XRPC query (HTTP GET) and reads its JSON output.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TOut> QueryAsync<TOut>(
        Nsid nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls an XRPC query (HTTP GET) whose output is binary, such as a blob or a CAR file.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">
    /// Per-call settings; <see cref="XrpcCallOptions.Timeout"/> covers receiving the response
    /// headers.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response body as a stream, which the caller disposes.</returns>
    Task<XrpcStreamResponse> DownloadAsync(
        Nsid nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Calls an XRPC procedure (HTTP POST) with a JSON input and reads its JSON output.</summary>
    /// <typeparam name="TIn">The input type.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="input">The input; <see langword="null"/> sends no body.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TOut> ProcedureAsync<TIn, TOut>(
        Nsid nsid,
        TIn input,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls an XRPC procedure (HTTP POST) with a JSON input, ignoring any output.
    /// </summary>
    /// <typeparam name="TIn">The input type.</typeparam>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="input">The input; <see langword="null"/> sends no body.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcedureAsync<TIn>(
        Nsid nsid,
        TIn input,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls an XRPC procedure (HTTP POST) that takes no input, ignoring any output.
    /// </summary>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Preferred over <see cref="ProcedureAsync{TIn}"/> when the second argument is an
    /// <see cref="XrpcParams"/>, which is never a procedure's input.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    Task ProcedureAsync(
        Nsid nsid,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls an XRPC procedure (HTTP POST) whose input is binary, such as a blob upload, and
    /// reads its JSON output.
    /// </summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="nsid">The method NSID.</param>
    /// <param name="data">
    /// The body, read from its current position and not disposed. A retry (a DPoP nonce
    /// challenge, a rate limit) rewinds to that position, which needs a seekable stream; a
    /// non-seekable one that would need a retry fails with <see cref="InvalidOperationException"/>.
    /// </param>
    /// <param name="mimeType">The body's MIME type, sent as <c>Content-Type</c>.</param>
    /// <param name="parameters">The query parameters, if the method takes any.</param>
    /// <param name="options">Per-call settings: proxy, labelers, headers, timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TOut> UploadAsync<TOut>(
        Nsid nsid,
        Stream data,
        string mimeType,
        XrpcParams? parameters = null,
        XrpcCallOptions? options = null,
        CancellationToken cancellationToken = default);
}
