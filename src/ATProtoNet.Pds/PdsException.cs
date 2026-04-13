namespace ATProtoNet.Pds;

/// <summary>
/// Exception thrown by PDS operations with an AT Protocol error code.
/// </summary>
public sealed class PdsException : Exception
{
    /// <summary>
    /// The AT Protocol error code (e.g. "InvalidRequest", "AuthenticationRequired").
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Creates a new PDS exception.
    /// </summary>
    public PdsException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
