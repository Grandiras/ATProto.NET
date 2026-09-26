using ATProtoNet.Identity;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// A completed OAuth callback: whom it signed in, and where the browser goes next.
/// </summary>
/// <remarks>
/// Only <see cref="AtProtoOAuthService"/> creates one, so <see cref="RedirectUrl"/> is always a
/// destination the service chose: never a URL taken from the request.
/// </remarks>
public sealed class AtProtoOAuthCallbackResult
{
    private AtProtoOAuthCallbackResult(Did did, string redirectUrl, bool isRelay)
    {
        Did = did;
        RedirectUrl = redirectUrl;
        IsRelay = isRelay;
    }

    /// <summary>The account that signed in.</summary>
    public Did Did { get; }

    /// <summary>
    /// Where to redirect the browser: the local return URL the login named, or
    /// <see cref="AtProtoOAuthServerOptions.DefaultReturnUrl"/>; or, for a relayed login, the
    /// relay endpoint on the loopback origin the login started on.
    /// </summary>
    public string RedirectUrl { get; }

    /// <summary>
    /// Whether the callback arrived on another loopback origin than the login started on, so the
    /// cookie is issued by the relay endpoint on the login's origin rather than by this response.
    /// </summary>
    public bool IsRelay { get; }

    /// <summary>A login completed on this origin, returning to <paramref name="returnUrl"/>.</summary>
    internal static AtProtoOAuthCallbackResult SignedIn(Did did, string returnUrl) => new(did, returnUrl, isRelay: false);

    /// <summary>A login to finish on its own loopback origin, through <paramref name="relayUrl"/>.</summary>
    internal static AtProtoOAuthCallbackResult Relayed(Did did, string relayUrl) => new(did, relayUrl, isRelay: true);
}
