namespace ATProtoNet.Http;

/// <summary>
/// Per-call settings for one XRPC request.
/// </summary>
/// <remarks>
/// <para>Everything here applies to the one call it is passed to and overrides the client-wide
/// defaults (<see cref="AtProtoClient.SetProxy"/>, <see cref="AtProtoClient.SetLabelers(IEnumerable{string})"/>)
/// for that call only. That makes it the safe way to vary these on a client shared between
/// concurrent callers, such as the singleton dependency injection registers: nothing is
/// written to the client, so no caller sees another's settings.</para>
/// <para>An instance is immutable once built, so one can be kept in a static field and reused.</para>
/// </remarks>
/// <example>
/// <code>
/// var labels = await client.QueryAsync&lt;QueryLabelsResponse&gt;(
///     Nsid.Parse("com.atproto.label.queryLabels"),
///     new XrpcParams().AddAll("uriPatterns", ["at://did:plc:alice/*"]),
///     new XrpcCallOptions { Proxy = "did:plc:labeler#atproto_labeler" });
/// </code>
/// </example>
public sealed record XrpcCallOptions
{
    /// <summary>
    /// The <c>atproto-proxy</c> header for this call: a service DID and endpoint fragment, such
    /// as <see cref="ServiceProxy.BskyChatHeader"/>.
    /// </summary>
    /// <remarks>
    /// Precedence: a value set here is always sent. When it is <see langword="null"/>, the
    /// client-wide default from <see cref="AtProtoClient.SetProxy"/> is sent instead, if there
    /// is one — except on the session calls the SDK makes itself (<c>createSession</c>,
    /// <c>refreshSession</c>, <c>getSession</c>, <c>deleteSession</c>, <c>createAccount</c>),
    /// which address the account's own PDS and never carry the default.
    /// </remarks>
    public string? Proxy { get; init; }

    /// <summary>
    /// The labelers whose labels the service should apply to this call's response, sent as the
    /// <c>atproto-accept-labelers</c> header. Each entry is a labeler DID, optionally followed by
    /// <c>;redact</c>.
    /// </summary>
    /// <remarks>
    /// Precedence as for <see cref="Proxy"/>: a list set here is always used, and an empty one
    /// sends no header even when the client has a default. <see langword="null"/> falls back to
    /// the client-wide default from <see cref="AtProtoClient.SetLabelers(IEnumerable{string})"/>,
    /// except on the SDK's own session calls.
    /// </remarks>
    public IReadOnlyList<string>? AcceptLabelers { get; init; }

    /// <summary>
    /// Additional request headers. They are added after the SDK's own, so they can override
    /// <c>User-Agent</c>, <c>atproto-proxy</c> and <c>atproto-accept-labelers</c>, but not
    /// <c>Authorization</c> or <c>DPoP</c>, which belong to the session and are rejected.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// A deadline for this call, from sending the request to reading the whole response (for a
    /// download, to receiving the response headers). On expiry the call throws
    /// <see cref="TimeoutException"/>. The <see cref="HttpClient.Timeout"/> of the underlying
    /// client still applies to each attempt on its own.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Marks a call addressed to the service itself: the client-wide <c>atproto-proxy</c> and
    /// <c>atproto-accept-labelers</c> defaults are not applied to it, while a per-call
    /// <see cref="Proxy"/> or <see cref="AcceptLabelers"/> still wins when set. The SDK sets it
    /// on the session calls, which must reach the account's own PDS.
    /// </summary>
    internal bool IsDirect { get; init; }
}
