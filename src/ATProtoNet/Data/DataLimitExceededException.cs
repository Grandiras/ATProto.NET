namespace ATProtoNet.Data;

/// <summary>Untrusted input exceeded a named size, count or depth limit while it was read.</summary>
/// <remarks>It is a <see cref="FormatException"/>, so callers that treat any malformed input alike need no second catch.</remarks>
public sealed class DataLimitExceededException : FormatException
{
    /// <summary>Creates the exception for <paramref name="limit"/>.</summary>
    /// <param name="limit">The name of the exceeded limit, such as <c>MaxDepth</c> or <c>MaxBlockBytes</c>.</param>
    /// <param name="message">What was exceeded.</param>
    public DataLimitExceededException(string limit, string message) : base(message) => Limit = limit;

    /// <summary>The name of the exceeded limit: the option or constant that sets it.</summary>
    public string Limit { get; }
}
