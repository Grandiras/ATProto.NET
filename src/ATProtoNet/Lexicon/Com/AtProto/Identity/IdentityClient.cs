using ATProtoNet.Http;

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
        string handle, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("handle", handle);
        return _xrpc.QueryAsync<ResolveHandleResponse>(
            "com.atproto.identity.resolveHandle", parameters, cancellationToken);
    }

    /// <summary>
    /// Update the handle for the currently authenticated account.
    /// </summary>
    /// <param name="handle">The new handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateHandleAsync(
        string handle, CancellationToken cancellationToken = default)
    {
        var request = new UpdateHandleRequest { Handle = handle };
        await _xrpc.ProcedureAsync<UpdateHandleRequest>(
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
        await _xrpc.ProcedureAsync<object>(
            "com.atproto.identity.requestPlcOperationSignature",
            new { }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sign a PLC operation with the server's rotation key.
    /// </summary>
    public Task<SignPlcOperationResponse> SignPlcOperationAsync(
        SignPlcOperationRequest request, CancellationToken cancellationToken = default)
    {
        return _xrpc.ProcedureAsync<SignPlcOperationRequest, SignPlcOperationResponse>(
            "com.atproto.identity.signPlcOperation", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Submit a signed PLC operation to the PLC directory.
    /// </summary>
    public async Task SubmitPlcOperationAsync(
        SubmitPlcOperationRequest request, CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync<SubmitPlcOperationRequest>(
            "com.atproto.identity.submitPlcOperation", request, cancellationToken: cancellationToken);
    }
}
