namespace ATProtoNet.Identity;

/// <summary>Why an identity did not resolve.</summary>
public enum IdentityResolutionError
{
    /// <summary>The DID's method refuses the identifier; nothing was sent.</summary>
    InvalidDid,

    /// <summary>No resolver handles the DID's method; nothing was sent.</summary>
    UnsupportedMethod,

    /// <summary>The DID has no document (HTTP 404).</summary>
    NotFound,

    /// <summary>The DID is deactivated or tombstoned (HTTP 410).</summary>
    Deactivated,

    /// <summary>The document, or another answer, is malformed or names another DID.</summary>
    InvalidDocument,

    /// <summary>The handle resolves to no DID.</summary>
    HandleNotFound,

    /// <summary>The handle's DNS publishes more than one DID.</summary>
    HandleConflict,

    /// <summary>The host could not be fetched; the inner exception is the <see cref="Net.UntrustedFetchException"/>.</summary>
    Unavailable,
}

/// <summary>A DID or handle did not resolve.</summary>
public sealed class IdentityResolutionException : AtProtoException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">Why resolution failed.</param>
    /// <param name="message">What failed.</param>
    /// <param name="identifier">The identifier being resolved, if known.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public IdentityResolutionException(IdentityResolutionError error, string message, AtIdentifier? identifier = null, Exception? innerException = null)
        : base(message, innerException) => (Error, Identifier) = (error, identifier);

    /// <summary>Why resolution failed.</summary>
    public IdentityResolutionError Error { get; }

    /// <summary>The identifier being resolved, if known.</summary>
    public AtIdentifier? Identifier { get; }
}
