namespace ATProtoNet;

/// <summary>The base of every exception the SDK throws for a protocol or remote failure.</summary>
/// <remarks>Invalid arguments stay the BCL's (<see cref="ArgumentException"/>, <see cref="FormatException"/> from parsing), and cancellation is never wrapped.</remarks>
public abstract class AtProtoException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    protected AtProtoException(string message, Exception? innerException = null) : base(message, innerException) { }
}
