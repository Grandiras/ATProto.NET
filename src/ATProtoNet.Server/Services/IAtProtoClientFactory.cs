using System.Security.Claims;

namespace ATProtoNet.Server.Services;

/// <summary>
/// Factory for creating authenticated <see cref="AtProtoClient"/> instances
/// for the current user. Uses the sessions in <see cref="ATProtoNet.Auth.IAtProtoSessionStore"/>
/// to create clients configured with the user's PDS URL and tokens.
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
    /// The client is configured with the user's PDS URL and stored session, and refreshes it
    /// on demand.
    /// </summary>
    /// <param name="user">
    /// The claims principal from the current request. The user is the one the OAuth login signed
    /// in: an authenticated identity the login issued (authentication type <c>ATProto</c>), or one
    /// carrying an <c>auth_method</c> claim of <c>oauth</c>, with a <c>did</c> or
    /// <see cref="ClaimTypes.NameIdentifier"/> claim. Identities of other schemes, service auth
    /// among them, are not considered, so a service auth token naming a DID never reaches that
    /// account's stored session.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An authenticated <see cref="AtProtoClient"/>, or <c>null</c> if the principal carries no
    /// OAuth login user, or the user has no stored session (signed out, say).
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
