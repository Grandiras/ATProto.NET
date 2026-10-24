namespace ATProtoNet.Spaces;

/// <summary>Which check of space data failed.</summary>
public enum SpaceVerificationError
{
    /// <summary>The data is malformed: an export, its metadata, a record or a MAC header.</summary>
    Malformed,
    /// <summary>The commit is for another repository or space than the one requested, or of another version.</summary>
    WrongRepository,
    /// <summary>The MAC is missing or does not verify.</summary>
    BadMac,
    /// <summary>The records do not hash to the commit's <c>data</c>: the repository did not arrive whole.</summary>
    DigestMismatch,
    /// <summary>The data exceeds a bound.</summary>
    LimitExceeded,
}

/// <summary>Space data failed verification.</summary>
/// <param name="error">Which check failed.</param>
/// <param name="message">A description of the failure.</param>
/// <param name="innerException">The underlying cause, if any.</param>
public sealed class SpaceVerificationException(SpaceVerificationError error, string message, Exception? innerException = null)
    : AtProtoException(message, innerException)
{
    /// <summary>Which check failed.</summary>
    public SpaceVerificationError Error { get; } = error;
}
