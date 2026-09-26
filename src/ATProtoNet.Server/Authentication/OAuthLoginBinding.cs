using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// What a login's callback needs from its start, kept server-side as the pending authorization's
/// <see cref="OAuthAuthorizationOptions.AppState"/>: the origin the browser started on, where to
/// send it afterwards, and a hash of the browser's binding cookie.
/// </summary>
/// <param name="Origin">The origin the login started on.</param>
/// <param name="ReturnUrl">The local URL to return to, already checked with <see cref="OAuthLoginBinding.IsLocalUrl"/>.</param>
/// <param name="BindingHash">The SHA-256 of the binding cookie's value, lower-case hex.</param>
internal sealed record OAuthLoginState(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("returnUrl")] string? ReturnUrl,
    [property: JsonPropertyName("binding")] string BindingHash)
{
    public string Serialize() => JsonSerializer.Serialize(this);

    public static OAuthLoginState? TryParse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<OAuthLoginState>(json) is { Origin.Length: > 0, BindingHash.Length: > 0 } state
                ? state
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Binds a login to the browser that started it, and keeps its return URL local.
/// </summary>
/// <remarks>
/// <para>The OAuth <c>state</c> ties a callback to a pending authorization, not to a browser.
/// Without a binding, anyone could start a login for their own account and send the callback URL
/// to someone else, whose browser would then be signed in as them. So the login endpoint gives
/// the browser a random value in an HttpOnly, SameSite=Lax cookie and keeps only its hash with
/// the pending authorization, and the callback (or, across loopback origins, the relay) accepts
/// the login only from a browser presenting it.</para>
/// <para>Over HTTPS the cookie carries the <c>__Host-</c> prefix, which a sibling subdomain cannot
/// set, so the binding cannot be planted from one.</para>
/// </remarks>
internal static class OAuthLoginBinding
{
    private const string SecureCookieName = "__Host-atproto-oauth-binding";
    private const string PlainCookieName = "atproto-oauth-binding";

    /// <summary>
    /// Gives the browser its binding cookie, reusing the value it already holds so that logins
    /// started in two tabs both complete, and returns the value's hash.
    /// </summary>
    public static string Issue(HttpContext context)
    {
        var name = CookieName(context.Request);
        var value = context.Request.Cookies[name];
        if (value is not { Length: 43 } || !Base64Url.IsValid(value))
            value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        context.Response.Cookies.Append(name, value, Options(context.Request, OAuthClient.AuthorizationLifetime));
        return Hash(value);
    }

    /// <summary>Whether the request carries the binding cookie whose hash is <paramref name="expectedHash"/>.</summary>
    public static bool Verify(HttpContext context, string expectedHash)
    {
        if (context.Request.Cookies[CookieName(context.Request)] is not { Length: > 0 } value)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(expectedHash));
    }

    /// <summary>Removes the binding cookie once a login has completed.</summary>
    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName(context.Request), Options(context.Request, maxAge: null));

    /// <summary>
    /// Whether <paramref name="url"/> is a local URL, with ASP.NET Core's <c>IsLocalUrl</c> rules:
    /// a path starting with a single <c>/</c> (not <c>//</c> or <c>/\</c>, which browsers treat as
    /// another host), and no control characters.
    /// </summary>
    public static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
            return false;

        if (url.Length == 1)
            return true;

        if (url[1] is '/' or '\\')
            return false;

        foreach (var c in url)
        {
            if (char.IsControl(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether an origin is a loopback one: the only kind a login may move between, from the
    /// browser's <c>https://localhost</c> to a loopback client's <c>http://127.0.0.1</c> callback.
    /// </summary>
    public static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    private static string CookieName(HttpRequest request) => request.IsHttps ? SecureCookieName : PlainCookieName;

    private static CookieOptions Options(HttpRequest request, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = maxAge,

        // Signing in cannot work without it, so consent policies must not drop it.
        IsEssential = true,
    };

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
}
