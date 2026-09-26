namespace ATProtoNet.Server.Authentication;

/// <summary>
/// The claim types the AT Protocol authentication schemes issue: the OAuth cookie login
/// (<see cref="AtProtoOAuthExtensions.AddAtProtoAuthentication"/>) and service auth
/// (<see cref="AtProtoServiceAuthExtensions.AddAtProtoServiceAuth(Microsoft.AspNetCore.Authentication.AuthenticationBuilder, Action{AtProtoServiceAuthOptions}?)"/>).
/// </summary>
/// <remarks>
/// Both schemes put the account's DID in <see cref="Did"/>, which is what the client factory
/// and the space server read; the cookie login also sets it as
/// <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>.
/// </remarks>
public static class AtProtoClaimTypes
{
    /// <summary>The account's DID: the signed-in user, or a service auth token's issuer (<c>iss</c>).</summary>
    public const string Did = "did";

    /// <summary>
    /// The account's handle when it verifies in both directions, and its DID when it does not
    /// (see <see cref="HandleVerified"/>). Issued by the OAuth login.
    /// </summary>
    public const string Handle = "handle";

    /// <summary><c>true</c> or <c>false</c>: whether <see cref="Handle"/> is a verified handle. Issued by the OAuth login.</summary>
    public const string HandleVerified = "handle_verified";

    /// <summary>The URL of the account's PDS. Issued by the OAuth login.</summary>
    public const string PdsUrl = "pds_url";

    /// <summary>How the user signed in: <c>oauth</c> for the OAuth login.</summary>
    public const string AuthMethod = "auth_method";

    /// <summary>The XRPC method a service auth token was scoped to (<c>lxm</c>), when it named one.</summary>
    public const string LexiconMethod = "lxm";

    /// <summary>The audience a service auth token addressed (<c>aud</c>).</summary>
    public const string Audience = "aud";
}
