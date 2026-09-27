using System.Security.Claims;
using ATProtoNet.Identity;

namespace ATProtoNet.Server.Authentication;

// Finds the user the OAuth login signed in, the only one whose stored session the client factory, the
// Blazor components and sign-out act on.
//
// A principal can carry identities from several schemes, and service auth issues the same did claim
// for whoever holds a token naming that DID. Such a holder must not be able to drive the account's
// stored OAuth session, whose scopes are far wider than the one method a service auth token is good
// for, so only an identity the OAuth login issued counts.
internal static class OAuthUser
{
    // The authentication type of the identities the OAuth login issues.
    internal const string IdentityType = "ATProto";

    // The AtProtoClaimTypes.AuthMethod value of the OAuth login.
    internal const string AuthMethod = "oauth";

    // The DID of the user the OAuth login signed in: from an authenticated identity it issued
    // (authentication type ATProto) or one carrying auth_method oauth, read from its did claim, else its
    // ClaimTypes.NameIdentifier.
    //
    // Returns: The DID, or null when no such identity carries one.
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
