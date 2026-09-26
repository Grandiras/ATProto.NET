using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Com.AtProto.Temp;

/// <summary>
/// Client for com.atproto.temp.* XRPC endpoints: methods upstream marks as temporary, which may
/// change or be replaced — signup helpers, OAuth scope references and account credential
/// revocation.
/// </summary>
/// <remarks>
/// The deprecated <c>fetchLabels</c> (use <see cref="Label.LabelClient"/>) and the entryway-internal
/// <c>addReservedHandle</c> are not covered.
/// </remarks>
public sealed class TempClient
{
    private readonly XrpcClient _xrpc;

    internal TempClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Check whether a handle is available for signup, getting suggestions when it is not.
    /// </summary>
    /// <param name="handle">The handle to check; also the seed for suggestions.</param>
    /// <param name="email">The user's email address, which the server may use for suggestions.</param>
    /// <param name="birthDate">The user's birth date, which the server may use for suggestions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CheckHandleAvailabilityResponse> CheckHandleAvailabilityAsync(
        Handle handle,
        string? email = null,
        AtDatetime? birthDate = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("handle", handle)
            .Add("email", email)
            .Add("birthDate", birthDate?.ToString());

        return _xrpc.QueryAsync<CheckHandleAvailabilityResponse>(
            "com.atproto.temp.checkHandleAvailability", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Check where the signed-in account is in the signup queue, on a server that queues new
    /// accounts before activating them.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CheckSignupQueueResponse> CheckSignupQueueAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<CheckSignupQueueResponse>(
            "com.atproto.temp.checkSignupQueue", cancellationToken: cancellationToken);

    /// <summary>
    /// Expand an OAuth scope reference (<c>ref:…</c>) into the full permission scope it stands for.
    /// </summary>
    /// <remarks>
    /// An authorization server may hand a resource server a short reference in place of a long
    /// scope string; this is how the resource server looks the scope up.
    /// </remarks>
    /// <param name="scope">The scope reference, starting with <c>ref:</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full OAuth permission scope.</returns>
    public async Task<string> DereferenceScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var parameters = new XrpcParams().Add("scope", scope);
        var response = await _xrpc.QueryAsync<DereferenceScopeResponse>(
            "com.atproto.temp.dereferenceScope", parameters, cancellationToken: cancellationToken);
        return response.Scope;
    }

    /// <summary>
    /// Ask the server to text a verification code to a phone number, on a server that verifies
    /// phone numbers at signup. The code is then passed as
    /// <c>verificationCode</c> of <c>com.atproto.server.createAccount</c>.
    /// </summary>
    /// <param name="phoneNumber">The phone number to send the code to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RequestPhoneVerificationAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var request = new RequestPhoneVerificationRequest { PhoneNumber = phoneNumber };
        await _xrpc.ProcedureAsync(
            "com.atproto.temp.requestPhoneVerification", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Revoke an account's sessions, password and app passwords (moderator action). The account
    /// can recover with a password reset.
    /// </summary>
    /// <param name="account">The account's DID or handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RevokeAccountCredentialsAsync(AtIdentifier account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var request = new RevokeAccountCredentialsRequest { Account = account };
        await _xrpc.ProcedureAsync(
            "com.atproto.temp.revokeAccountCredentials", request, cancellationToken: cancellationToken);
    }
}
