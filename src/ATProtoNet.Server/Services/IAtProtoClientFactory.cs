using System.Security.Claims;

namespace ATProtoNet.Server.Services;

/// <summary>
/// Factory for creating authenticated <see cref="AtProtoClient"/> instances
/// for the current user. Uses stored OAuth tokens from <see cref="ATProtoNet.Auth.OAuth.IAtProtoTokenStore"/>
/// to create clients configured with the user's PDS URL and DPoP-bound tokens.
/// </summary>
/// <remarks>
/// <para>Each call to <see cref="CreateClientForUserAsync"/> returns a new disposable client.
/// The caller is responsible for disposing it after use.</para>
/// <para>Register with <c>services.AddAtProtoServer()</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
/// {
///     await using var client = await factory.CreateClientForUserAsync(user);
///     if (client is null) return Results.Unauthorized();
///     var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
///     return Results.Ok(profile);
/// });
/// </code>
/// </example>
public interface IAtProtoClientFactory
{
    /// <summary>
    /// Creates an authenticated <see cref="AtProtoClient"/> for the specified user.
    /// The client is configured with the user's PDS URL and DPoP-bound OAuth tokens.
    /// </summary>
    /// <param name="user">
    /// The claims principal from the current request. Must contain a <c>did</c> or
    /// <see cref="ClaimTypes.NameIdentifier"/> claim.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An authenticated <see cref="AtProtoClient"/>, or <c>null</c> if the user
    /// has no stored tokens (e.g., not logged in via OAuth, or tokens were removed).
    /// </returns>
    /// <remarks>
    /// <para>The returned client is disposable. Use <c>await using</c> for proper cleanup:</para>
    /// <code>
    /// await using var client = await factory.CreateClientForUserAsync(user);
    /// </code>
    /// </remarks>
    Task<AtProtoClient?> CreateClientForUserAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
