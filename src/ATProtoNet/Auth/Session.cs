using System.Text.Json.Serialization;

namespace ATProtoNet.Auth;

/// <summary>
/// Represents an authenticated session with an AT Protocol PDS.
/// </summary>
public sealed class Session
{
    /// <summary>
    /// The DID of the authenticated account.
    /// </summary>
    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;

    /// <summary>
    /// The handle of the authenticated account.
    /// </summary>
    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    /// <summary>
    /// The access JWT token for authentication.
    /// </summary>
    [JsonPropertyName("accessJwt")]
    public string AccessJwt { get; init; } = string.Empty;

    /// <summary>
    /// The refresh JWT token for obtaining new access tokens.
    /// </summary>
    [JsonPropertyName("refreshJwt")]
    public string RefreshJwt { get; init; } = string.Empty;

    /// <summary>
    /// The email associated with the account, if available.
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// Whether the email has been confirmed.
    /// </summary>
    [JsonPropertyName("emailConfirmed")]
    public bool? EmailConfirmed { get; init; }

    /// <summary>
    /// Whether email-based authentication factor is enabled.
    /// </summary>
    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    /// <summary>
    /// DID document associated with the account.
    /// </summary>
    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    /// <summary>
    /// Whether the account is active.
    /// </summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>
    /// Status of the account (e.g., "takendown", "suspended", "deactivated").
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Interface for session persistence storage.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Saves the session data.
    /// </summary>
    Task SaveAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the saved session data, if any.
    /// </summary>
    Task<Session?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the stored session data.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory session store. Sessions are lost when the application exits.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private Session? _session;

    public Task SaveAsync(Session session, CancellationToken cancellationToken = default)
    {
        _session = session;
        return Task.CompletedTask;
    }

    public Task<Session?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_session);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _session = null;
        return Task.CompletedTask;
    }
}
