using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Server.Services;

/// <summary>
/// Supplies the <see cref="OAuthClient"/> that <see cref="IAtProtoClientFactory"/>
/// uses to refresh OAuth-bound sessions for per-request clients. The provider is
/// optional: when not registered in DI, factory-built clients will not be able to
/// refresh expired OAuth tokens.
/// </summary>
/// <remarks>
/// The Blazor integration (<c>AtProtoOAuthService</c>) implements this interface and
/// is registered automatically when you call <c>AddAtProtoAuthentication()</c>. If
/// you build your own OAuth integration, register your own implementation so that
/// expired tokens can be rotated transparently.
/// </remarks>
public interface IOAuthClientProvider
{
    /// <summary>
    /// Returns the configured <see cref="OAuthClient"/>, or <c>null</c> if none has
    /// been created yet (e.g. no user has started the OAuth flow in this process).
    /// </summary>
    OAuthClient? TryGetClient();
}
