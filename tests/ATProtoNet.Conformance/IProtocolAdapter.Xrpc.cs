using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Issues <paramref name="call"/> through the implementation's own client, whose HTTP stack
    /// is <paramref name="handler"/> (it records the request and serves the scripted response),
    /// and reports what the client returned or raised. The client is anonymous: no session.
    /// </summary>
    Task<XrpcOutcome> InvokeAsync(XrpcFixtureCall call, HttpMessageHandler handler, CancellationToken cancellationToken);
}

/// <summary>One XRPC call as a caller makes it.</summary>
/// <param name="Nsid">The method's NSID.</param>
/// <param name="Params">
/// What the caller passes, by Lexicon name: a query's parameters, or a procedure's input object.
/// Arrays are JSON arrays, identifiers and datetimes strings, a blob input <c>{"$bytes": base64}</c>,
/// and a MIME type for it <c>mimeType</c>. A record read or written by AT URI passes <c>uri</c>.
/// </param>
/// <param name="ServiceUrl">The service's base URL, without a path.</param>
/// <param name="ClientProxy">The <c>atproto-proxy</c> value configured on the whole client, if any.</param>
public sealed record XrpcFixtureCall(string Nsid, JsonObject Params, string ServiceUrl, string? ClientProxy = null);

/// <summary>What a call produced.</summary>
/// <param name="Output">
/// The decoded output re-serialised as Lexicon JSON (the shape the method's Lexicon declares,
/// so a client that unwraps a single-field output wraps it back), or null for a method without
/// JSON output or a failed call.
/// </param>
/// <param name="Binary">The body of a binary-output method, read to the end.</param>
/// <param name="BinaryContentType">The media type the client reported for <paramref name="Binary"/>.</param>
/// <param name="Failure">The XRPC error the call raised, if it did.</param>
public sealed record XrpcOutcome(
    JsonNode? Output = null, byte[]? Binary = null, string? BinaryContentType = null, XrpcFailure? Failure = null);

/// <summary>An XRPC error as the client surfaced it.</summary>
/// <param name="Error">The XRPC error name (from the body, else from the status).</param>
/// <param name="Status">The HTTP status.</param>
/// <param name="Message">The server's message, if the body had one.</param>
public sealed record XrpcFailure(string Error, int Status, string? Message = null);
