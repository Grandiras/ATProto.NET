using System.ComponentModel.DataAnnotations;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// Entity representing a stored AT Protocol OAuth token in the database.
/// The token payload is encrypted at rest using ASP.NET Core Data Protection.
/// </summary>
public sealed class AtProtoTokenEntity
{
    /// <summary>
    /// The user's DID (decentralized identifier). Primary key.
    /// </summary>
    [Key]
    [MaxLength(2048)]
    public required string Did { get; set; }

    /// <summary>
    /// The encrypted, serialized token data (JSON protected by Data Protection).
    /// </summary>
    public required string EncryptedTokenData { get; set; }

    /// <summary>
    /// When this token was last updated (UTC).
    /// Used for cleanup of expired tokens.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
