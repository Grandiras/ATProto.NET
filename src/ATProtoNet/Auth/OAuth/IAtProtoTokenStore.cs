namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Stores and retrieves OAuth token data for multiple users, keyed by DID.
/// Used to persist DPoP-bound tokens server-side so that backend code can create
/// authenticated <see cref="AtProtoClient"/> instances on behalf of logged-in users.
/// </summary>
/// <remarks>
/// <para>The default <c>InMemoryAtProtoTokenStore</c> (in ATProtoNet.Server) is suitable
/// for development and single-server deployments. For production, implement this interface
/// with a durable store (e.g., encrypted database, Redis with data protection).</para>
/// <para><b>Security:</b> Token data includes DPoP private keys and OAuth tokens.
/// Implementations must protect stored data at rest (encryption) and limit access.</para>
/// </remarks>
public interface IAtProtoTokenStore
{
    /// <summary>
    /// Stores token data for a user, replacing any existing data for the same DID.
    /// </summary>
    /// <param name="did">The user's DID (decentralized identifier).</param>
    /// <param name="data">The token data to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string did, AtProtoTokenData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves stored token data for a user.
    /// </summary>
    /// <param name="did">The user's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token data, or <c>null</c> if no data exists for this DID.</returns>
    Task<AtProtoTokenData?> GetAsync(string did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes stored token data for a user (e.g., on logout).
    /// </summary>
    /// <param name="did">The user's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string did, CancellationToken cancellationToken = default);
}
