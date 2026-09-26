using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Com.AtProto.Identity;

/// <summary>
/// Client for com.atproto.identity.* XRPC endpoints.
/// Handles DID/handle resolution and PLC operations.
/// </summary>
public sealed class IdentityClient
{
    private readonly XrpcClient _xrpc;

    internal IdentityClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Resolve a handle (domain name) to a DID.
    /// </summary>
    /// <param name="handle">The handle to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ResolveHandleResponse> ResolveHandleAsync(
        Handle handle, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("handle", handle);
        return _xrpc.QueryAsync<ResolveHandleResponse>(
            "com.atproto.identity.resolveHandle", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Resolves a DID or a handle to a full identity: the DID document and the bidirectionally
    /// verified handle, as the service resolved them (<c>com.atproto.identity.resolveIdentity</c>).
    /// </summary>
    /// <param name="identifier">The DID or handle to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity. Its handle is <c>handle.invalid</c> when it did not verify.</returns>
    /// <exception cref="XrpcException">
    /// Thrown with <see cref="XrpcErrors.HandleNotFound"/>, <see cref="XrpcErrors.DidNotFound"/> or
    /// <see cref="XrpcErrors.DidDeactivated"/> when the identity does not resolve.
    /// </exception>
    /// <remarks>
    /// This delegates resolution to the service and trusts its answer. To resolve locally, with
    /// the SDK's own checks, use <see cref="IdentityResolver"/>.
    /// </remarks>
    public Task<IdentityInfo> ResolveIdentityAsync(
        AtIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var parameters = new XrpcParams().Add("identifier", identifier);
        return _xrpc.QueryAsync<IdentityInfo>(
            "com.atproto.identity.resolveIdentity", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Resolves a DID to its DID document, without verifying the handle
    /// (<c>com.atproto.identity.resolveDid</c>).
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response carrying the document.</returns>
    /// <exception cref="XrpcException">
    /// Thrown with <see cref="XrpcErrors.DidNotFound"/> or <see cref="XrpcErrors.DidDeactivated"/>
    /// when the DID does not resolve.
    /// </exception>
    public Task<ResolveDidResponse> ResolveDidAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<ResolveDidResponse>(
            "com.atproto.identity.resolveDid", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Asks the service to re-resolve an identity, dropping what it cached
    /// (<c>com.atproto.identity.refreshIdentity</c>). The service may ignore the request or require
    /// authentication, depending on its role and policy.
    /// </summary>
    /// <param name="identifier">The DID or handle to refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity as re-resolved.</returns>
    /// <exception cref="XrpcException">
    /// Thrown with <see cref="XrpcErrors.HandleNotFound"/>, <see cref="XrpcErrors.DidNotFound"/> or
    /// <see cref="XrpcErrors.DidDeactivated"/> when the identity does not resolve.
    /// </exception>
    public Task<IdentityInfo> RefreshIdentityAsync(
        AtIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var request = new RefreshIdentityRequest { Identifier = identifier };
        return _xrpc.ProcedureAsync<IdentityInfo>(
            "com.atproto.identity.refreshIdentity", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update the handle for the currently authenticated account.
    /// </summary>
    /// <param name="handle">The new handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateHandleAsync(
        Handle handle, CancellationToken cancellationToken = default)
    {
        var request = new UpdateHandleRequest { Handle = handle };
        await _xrpc.ProcedureAsync(
            "com.atproto.identity.updateHandle", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get recommended DID credentials for account migration.
    /// </summary>
    public Task<GetRecommendedDidCredentialsResponse> GetRecommendedDidCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<GetRecommendedDidCredentialsResponse>(
            "com.atproto.identity.getRecommendedDidCredentials",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Request an email token for signing a PLC operation.
    /// </summary>
    public async Task RequestPlcOperationSignatureAsync(
        CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "com.atproto.identity.requestPlcOperationSignature",
            new { }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sign a PLC operation with the server's rotation key.
    /// </summary>
    public Task<SignPlcOperationResponse> SignPlcOperationAsync(
        SignPlcOperationRequest request, CancellationToken cancellationToken = default)
    {
        return _xrpc.ProcedureAsync<SignPlcOperationResponse>(
            "com.atproto.identity.signPlcOperation", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Submit a signed PLC operation to the PLC directory.
    /// </summary>
    public async Task SubmitPlcOperationAsync(
        SubmitPlcOperationRequest request, CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "com.atproto.identity.submitPlcOperation", request, cancellationToken: cancellationToken);
    }
}
