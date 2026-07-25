namespace ATProtoNet.Pds;

/// <summary>
/// Represents an account stored in the PDS.
/// </summary>
public sealed class PdsAccount
{
    /// <summary>The account's DID (e.g. did:plc:abc123).</summary>
    public required string Did { get; init; }

    /// <summary>The user's handle (e.g. alice.example.com).</summary>
    public required string Handle { get; set; }

    /// <summary>The user's email address.</summary>
    public string? Email { get; set; }

    /// <summary>Whether the email has been confirmed.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>Bcrypt-hashed password.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the account is currently active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The signing key for this account's repo (base64-encoded private key).</summary>
    public required string SigningKey { get; init; }

    /// <summary>
    /// The PLC rotation key for this account (base64-encoded private key), or <c>null</c> for
    /// accounts whose DID method needs none (<c>did:web</c>) or that predate federation support.
    /// <para>
    /// This key controls the identity: whoever holds it can move the account to another PDS.
    /// It is deliberately kept distinct from <see cref="SigningKey"/>, which only signs commits.
    /// </para>
    /// </summary>
    public string? RotationKey { get; set; }
}

/// <summary>
/// Persistent store for PDS accounts. Implementations may be backed by
/// a database, file system, or in-memory storage.
/// </summary>
public interface IAccountStore
{
    /// <summary>Create a new account.</summary>
    Task CreateAsync(PdsAccount account, CancellationToken cancellationToken = default);

    /// <summary>Get an account by DID.</summary>
    Task<PdsAccount?> GetByDidAsync(string did, CancellationToken cancellationToken = default);

    /// <summary>Get an account by handle.</summary>
    Task<PdsAccount?> GetByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>Get an account by email.</summary>
    Task<PdsAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Update an existing account.</summary>
    Task UpdateAsync(PdsAccount account, CancellationToken cancellationToken = default);

    /// <summary>Delete an account by DID.</summary>
    Task DeleteAsync(string did, CancellationToken cancellationToken = default);

    /// <summary>Check whether a handle is already taken.</summary>
    Task<bool> HandleExistsAsync(string handle, CancellationToken cancellationToken = default);
}
