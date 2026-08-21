using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Thrown when a credential presented to a space server does not verify.
/// </summary>
/// <remarks>
/// <para>It is an <see cref="XrpcException"/>, so a handler that lets one escape answers with
/// the named error the Lexicon declares rather than a 500 — <c>InvalidDelegationToken</c>,
/// <c>InvalidClientAttestation</c>, or <c>NotAuthorized</c> — and the routing writes the body.</para>
/// <para>The message describes the failure for the operator's logs, not for the caller. Keep
/// what it says away from anything the caller could use to distinguish "this space does not
/// exist" from "you may not read this space", which the protocol deliberately conflates.</para>
/// </remarks>
public sealed class SpaceVerificationException : XrpcException
{
    /// <summary>
    /// Creates a verification failure.
    /// </summary>
    /// <param name="error">The XRPC error name. See <c>SpaceErrors</c>.</param>
    /// <param name="message">A description of what failed.</param>
    /// <param name="statusCode">The HTTP status. Defaults to 401.</param>
    public SpaceVerificationException(
        string error, string message, int statusCode = StatusCodes.Status401Unauthorized)
        : base(error, message, statusCode)
    {
    }

    /// <summary>
    /// Creates a verification failure with an underlying cause.
    /// </summary>
    /// <param name="error">The XRPC error name. See <c>SpaceErrors</c>.</param>
    /// <param name="message">A description of what failed.</param>
    /// <param name="innerException">The underlying cause.</param>
    /// <param name="statusCode">The HTTP status. Defaults to 401.</param>
    public SpaceVerificationException(
        string error,
        string message,
        Exception innerException,
        int statusCode = StatusCodes.Status401Unauthorized)
        : base(error, message, innerException, statusCode)
    {
    }
}
