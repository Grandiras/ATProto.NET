using System.Security.Claims;
using ATProtoNet.Identity;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// Finds the user the OAuth login signed in, the only one whose stored session the client factory,
/// the Blazor components and sign-out act on.
/// </summary>
/// <remarks>
/// A principal can carry identities from several schemes, and service auth issues the same
/// <c>did</c> claim for whoever holds a token naming that DID. Such a holder must not be able to
/// drive the account's stored OAuth session, whose scopes are far wider than the one method a
/// service auth token is good for, so only an identity the OAuth login issued counts.
/// </remarks>
internal static class OAuthUser
{
    /// <summary>The authentication type of the identities the OAuth login issues.</summary>
    internal const string IdentityType = "ATProto";

    /// <summary>The <see cref="AtProtoClaimTypes.AuthMethod"/> value of the OAuth login.</summary>
    internal const string AuthMethod = "oauth";

    /// <summary>
    /// The DID of the user the OAuth login signed in: from an authenticated identity it issued
    /// (authentication type <c>ATProto</c>) or one carrying <c>auth_method</c> <c>oauth</c>, read
    /// from its <c>did</c> claim, else its <see cref="ClaimTypes.NameIdentifier"/>.
    /// </summary>
    /// <returns>The DID, or <see langword="null"/> when no such identity carries one.</returns>
    internal static Did? DidOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        foreach (var identity in user.Identities)
        {
            if (!identity.IsAuthenticated ||
                (!string.Equals(identity.AuthenticationType, IdentityType, StringComparison.Ordinal) &&
                 !identity.HasClaim(AtProtoClaimTypes.AuthMethod, AuthMethod)))
            {
                continue;
            }

            var claim = identity.FindFirst(AtProtoClaimTypes.Did)?.Value ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Did.TryParse(claim, out var did))
                return did;
        }

        return null;
    }
}
