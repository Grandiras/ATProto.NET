using System.Text.Json.Serialization;
using ATProtoNet.Identity;

namespace ATProtoNet.Auth;

/// <summary>
/// An authenticated session with an account's PDS: who the account is, where it is hosted, and
/// the credentials that authorize requests to it.
/// </summary>
/// <remarks>
/// <para>A session is an immutable value. <see cref="AtProtoClient"/> installs one with
/// <see cref="AtProtoClient.LoginAsync"/>, <see cref="AtProtoClient.ApplySessionAsync"/> or
/// <see cref="AtProtoClient.ResumeSessionAsync"/>, and replaces it with a new value whenever it
/// refreshes the tokens; read the current one from <see cref="AtProtoClient.Session"/> or
/// <see cref="AtProtoClient.SessionChanged"/>.</para>
/// <para>There are two kinds: <see cref="PasswordSession"/> from a password (or app password)
/// login, and <see cref="OAuthSession"/> from an OAuth authorization. Both serialize with
/// <see cref="System.Text.Json"/> as the base type, a <c>$kind</c> member telling them apart, so a
/// store can persist either one.</para>
/// <para><b>Security:</b> a session carries bearer credentials, and an OAuth session its DPoP
/// private key. Persist it encrypted, and never log it.</para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(PasswordSession), "password")]
[JsonDerivedType(typeof(OAuthSession), "oauth")]
public abstract record AtProtoSession
{
    private protected AtProtoSession()
    {
    }

    /// <summary>The account's DID.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>
    /// The account's handle; <c>handle.invalid</c> when it could not be verified.
    /// </summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>
    /// The service the session's requests go to: the account's PDS, or the entryway it signed in
    /// through when the PDS was not known.
    /// </summary>
    [JsonPropertyName("serviceEndpoint")]
    public required Uri ServiceEndpoint { get; init; }

    /// <summary>
    /// When the access token expires, if known. The client refreshes shortly before this time,
    /// and refreshes after the service rejects the token either way.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The token sent as the request credential.</summary>
    [JsonIgnore]
    internal abstract string AccessCredential { get; }

    /// <summary>Whether the session holds what a refresh needs.</summary>
    [JsonIgnore]
    internal abstract bool CanRefresh { get; }

    /// <summary>The session's kind, DID and handle; never its credentials.</summary>
    public sealed override string ToString() => $"{GetType().Name} {{ Did = {Did}, Handle = {Handle} }}";
}

/// <summary>
/// A session created with a password or app password (<c>com.atproto.server.createSession</c>),
/// authorized with bearer JWTs.
/// </summary>
public sealed record PasswordSession : AtProtoSession
{
    /// <summary>The access JWT sent with each request.</summary>
    [JsonPropertyName("accessJwt")]
    public required string AccessJwt { get; init; }

    /// <summary>
    /// The refresh JWT, which obtains a new access JWT and signs the session out.
    /// </summary>
    [JsonPropertyName("refreshJwt")]
    public required string RefreshJwt { get; init; }

    /// <summary>The account's email address, if the service disclosed it.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>Whether the email address has been confirmed.</summary>
    [JsonPropertyName("emailConfirmed")]
    public bool? EmailConfirmed { get; init; }

    /// <summary>Whether email is enabled as a second authentication factor.</summary>
    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    /// <summary>
    /// Whether the account is active (not deactivated, suspended, or taken down).
    /// </summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>The hosting status of the account, if it is not active.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    internal override string AccessCredential => AccessJwt;

    internal override bool CanRefresh => !string.IsNullOrEmpty(RefreshJwt);
}

/// <summary>
/// A session from an AT Protocol OAuth authorization, authorized with DPoP-bound tokens.
/// </summary>
/// <remarks>
/// It holds the DPoP private key as PKCS#8 bytes rather than a key object, so it owns nothing
/// that needs disposing: <see cref="AtProtoClient"/> builds (and disposes) its own key object
/// from <see cref="DPoPKey"/>.
/// </remarks>
public sealed record OAuthSession : AtProtoSession
{
    /// <summary>The DPoP-bound access token.</summary>
    [JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }

    /// <summary>The refresh token, when the authorization server issued one.</summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; init; }

    /// <summary>
    /// The session's DPoP private key, a P-256 key in PKCS#8 form. Every request and token
    /// refresh is signed with it.
    /// </summary>
    [JsonPropertyName("dpopKey")]
    public required ReadOnlyMemory<byte> DPoPKey { get; init; }

    /// <summary>The authorization server's issuer identifier.</summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary>The authorization server's token endpoint, which refreshes the session.</summary>
    [JsonPropertyName("tokenEndpoint")]
    public required Uri TokenEndpoint { get; init; }

    /// <summary>
    /// The authorization server's revocation endpoint (RFC 7009), if it has one. When
    /// <see langword="null"/>, sign-out looks it up in the server's metadata.
    /// </summary>
    [JsonPropertyName("revocationEndpoint")]
    public Uri? RevocationEndpoint { get; init; }

    /// <summary>The granted scopes, space-separated.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    internal override string AccessCredential => AccessToken;

    internal override bool CanRefresh => !string.IsNullOrEmpty(RefreshToken);
}

/// <summary>
/// What happened to the session of an <see cref="AtProtoClient"/>.
/// </summary>
public enum AtProtoSessionChange
{
    /// <summary>A session was installed: by a login, or by applying or resuming one.</summary>
    Created,

    /// <summary>
    /// The installed session was replaced by a newer version of itself: rotated tokens after a
    /// refresh, or account details updated while resuming.
    /// </summary>
    Refreshed,

    /// <summary>
    /// The session could not be refreshed because the service rejected its refresh token, so
    /// the client dropped it. The user has to sign in again.
    /// </summary>
    Expired,

    /// <summary>The session was signed out.</summary>
    Removed,
}

/// <summary>
/// Describes a change to the session of an <see cref="AtProtoClient"/>.
/// </summary>
public sealed class AtProtoSessionChangedEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="change">What happened.</param>
    /// <param name="session">The session now installed, if any.</param>
    /// <param name="previous">The session installed before the change, if any.</param>
    /// <param name="error">Why the session expired, for <see cref="AtProtoSessionChange.Expired"/>.</param>
    public AtProtoSessionChangedEventArgs(
        AtProtoSessionChange change,
        AtProtoSession? session,
        AtProtoSession? previous,
        Exception? error = null)
    {
        Change = change;
        Session = session;
        Previous = previous;
        Error = error;
    }

    /// <summary>What happened.</summary>
    public AtProtoSessionChange Change { get; }

    /// <summary>
    /// The session now installed; <see langword="null"/> after <see cref="AtProtoSessionChange.Expired"/>
    /// and <see cref="AtProtoSessionChange.Removed"/>.
    /// </summary>
    public AtProtoSession? Session { get; }

    /// <summary>The session installed before the change, if there was one.</summary>
    public AtProtoSession? Previous { get; }

    /// <summary>
    /// For <see cref="AtProtoSessionChange.Expired"/>, the refresh failure that ended the session.
    /// </summary>
    public Exception? Error { get; }
}
