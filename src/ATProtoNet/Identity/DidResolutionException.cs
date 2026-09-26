namespace ATProtoNet.Identity;

/// <summary>Why an identity could not be resolved.</summary>
public enum DidResolutionErrorKind
{
    /// <summary>
    /// The identifier is not one AT Protocol resolves: a path-based <c>did:web</c>, a port on a
    /// host other than <c>localhost</c>, an IP address where a hostname belongs.
    /// </summary>
    InvalidDid,

    /// <summary>The DID method is neither <c>did:plc</c> nor <c>did:web</c>.</summary>
    UnsupportedMethod,

    /// <summary>
    /// The identity fetch policy refused the request: plain HTTP, <c>localhost</c>, or a host that
    /// resolves to a loopback, private, link-local or CGNAT address. See
    /// <see cref="IdentityResolverOptions.AllowPrivateNetworks"/>.
    /// </summary>
    Blocked,

    /// <summary>No DID document exists (HTTP 404).</summary>
    NotFound,

    /// <summary>The DID existed but has been deactivated: a tombstoned <c>did:plc</c> (HTTP 410).</summary>
    Deactivated,

    /// <summary>The host answered with an unexpected HTTP status.</summary>
    HttpError,

    /// <summary>The host could not be reached.</summary>
    NetworkError,

    /// <summary>The host did not answer within the resolution timeout.</summary>
    Timeout,

    /// <summary>The response was larger than the resolver accepts.</summary>
    ResponseTooLarge,

    /// <summary>
    /// The response was not a usable DID document: malformed JSON, or an <c>id</c> other than the
    /// DID that was asked for.
    /// </summary>
    InvalidDocument,

    /// <summary>Neither DNS nor HTTPS resolved the handle to a DID.</summary>
    HandleNotFound,

    /// <summary>
    /// The handle's DNS and HTTPS answers name different DIDs, or its DNS answer names more than
    /// one. Resolution fails closed rather than picking one.
    /// </summary>
    HandleConflict,

    /// <summary>The PLC directory rejected a submitted operation.</summary>
    OperationRejected,
}

/// <summary>
/// Thrown when a DID, a handle or a PLC directory request cannot be resolved.
/// </summary>
/// <remarks>
/// Every resolver in <see cref="ATProtoNet.Identity"/> reports failure with this one type,
/// including a network failure or a timeout, so a caller that turns an unresolvable identity
/// into its own error (a refused token, a skipped commit) has one thing to catch.
/// </remarks>
public sealed class DidResolutionException : AtProtoException
{
    /// <summary>Creates an exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="kind">Why resolution failed.</param>
    /// <param name="did">The DID being resolved, when there is one.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public DidResolutionException(
        string message, DidResolutionErrorKind kind, Did? did = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Did = did;
    }

    /// <summary>Why resolution failed.</summary>
    public DidResolutionErrorKind Kind { get; }

    /// <summary>The DID being resolved, when there is one.</summary>
    public Did? Did { get; }
}
