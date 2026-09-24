namespace ATProtoNet;

/// <summary>
/// The base type of every exception the SDK raises on its own account.
/// </summary>
/// <remarks>
/// <para>Catching it catches every protocol-level failure: an XRPC error answered by a service
/// (<see cref="Http.XrpcException"/> and its subtypes), a response that does not match its
/// Lexicon (<see cref="Http.XrpcResponseFormatException"/>), and the failures of the OAuth,
/// permissioned-space and Jetstream components.</para>
/// <para>It does not catch what the SDK does not raise itself: argument validation
/// (<see cref="ArgumentException"/>), misuse (<see cref="InvalidOperationException"/>,
/// <see cref="ObjectDisposedException"/>), cancellation, or a transport fault — an
/// <see cref="HttpRequestException"/> when no response arrived at all — which surface
/// unchanged.</para>
/// </remarks>
public abstract class AtProtoException : Exception
{
    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">A description of what went wrong.</param>
    protected AtProtoException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    protected AtProtoException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
