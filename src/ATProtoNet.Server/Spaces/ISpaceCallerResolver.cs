using System.Security.Claims;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>Identifies the account an authenticated request is acting for.</summary>
/// <remarks>
/// The <c>com.atproto.simplespace</c> procedures are administered by a space's owner over that
/// account's ordinary OAuth session, not with a space credential — creating a space is what
/// happens <em>before</em> any credential for it can exist. That session is authenticated by
/// whatever scheme the host application already uses, so which claim carries the DID is the
/// application's business rather than this SDK's.
/// </remarks>
public interface ISpaceCallerResolver
{
    /// <summary>Returns the DID of the authenticated account, or <see langword="null"/> when the request carries no session.</summary>
    /// <param name="context">The HTTP context.</param>
    Did? GetCallerDid(HttpContext context);
}

/// <summary>The default <see cref="ISpaceCallerResolver"/>: reads the DID from the request's <see cref="ClaimsPrincipal"/>.</summary>
/// <remarks>
/// <para>It looks for a <c>did</c> claim first — <see cref="Authentication.AtProtoClaimTypes.Did"/>,
/// which the OAuth login and service auth issue — and falls back to
/// <see cref="ClaimTypes.NameIdentifier"/>. Either way the value must parse as a DID; a handle
/// is rejected, because a handle can be reassigned and would silently transfer ownership of a
/// space.</para>
/// <para>Unlike the OAuth login's client factory, which takes only the identity the login issued
/// because it then acts through that account's stored session, it accepts the claim from any
/// authentication scheme: nothing here uses a stored session or credential for the account, so a
/// verified caller can only ever act as itself.</para>
/// </remarks>
public sealed class ClaimsSpaceCallerResolver : ISpaceCallerResolver
{
    /// <inheritdoc/>
    public Did? GetCallerDid(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var value = user.FindFirstValue(Authentication.AtProtoClaimTypes.Did) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Did.TryParse(value, out var did) ? did : null;
    }
}

// Helpers shared by the endpoints that authenticate a caller rather than a credential.
internal static class SpaceCallerResolverExtensions
{
    // Returns the caller's DID, or throws an authentication failure.
    public static Did RequireCallerDid(this ISpaceCallerResolver resolver, HttpContext context) =>
        resolver.GetCallerDid(context)
        ?? throw new SpaceVerificationException(
            SpaceErrors.NotAuthorized, "This method requires an authenticated AT Protocol session.");
}
