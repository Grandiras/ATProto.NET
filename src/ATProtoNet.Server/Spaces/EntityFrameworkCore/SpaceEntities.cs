using System.ComponentModel.DataAnnotations;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// A space this authority gates, and whether it has been deleted.
/// </summary>
/// <remarks>
/// A row here is what makes a space <em>exist</em> as far as the authority endpoints are
/// concerned: <c>listRepos</c>, <c>registerNotify</c>, and <c>notifyWrite</c> all answer
/// <c>SpaceNotFound</c> without one. Deletion is a flag rather than a removal, because a deleted
/// space must keep answering <c>SpaceDeleted</c> — that is how a syncer that missed the
/// notification learns to drop its copy.
/// </remarks>
public sealed class SpaceEntity
{
    /// <summary>The space URI. Primary key.</summary>
    [Key]
    [MaxLength(512)]
    public required string Space { get; set; }

    /// <summary>Whether the space has been deleted.</summary>
    public bool Deleted { get; set; }
}

/// <summary>
/// One account's entry in a space's writer set, as last reported to the authority.
/// </summary>
/// <remarks>
/// The writer set is the sync boundary, not an access-control list. Each entry carries the
/// revision and commit hash from the last <c>notifyWrite</c>, which is what lets a syncer
/// re-sync only the repos that advanced.
/// </remarks>
public sealed class SpaceWriterEntity
{
    /// <summary>The space URI. Part of the composite primary key.</summary>
    [MaxLength(512)]
    public required string Space { get; set; }

    /// <summary>The writer's DID. Part of the composite primary key.</summary>
    [MaxLength(512)]
    public required string Did { get; set; }

    /// <summary>The repo's revision (a TID) as last reported.</summary>
    [MaxLength(64)]
    public required string Rev { get; set; }

    /// <summary>The repo's commit hash as last reported.</summary>
    public required byte[] Hash { get; set; }
}

/// <summary>
/// A service registered to receive a space's write notifications.
/// </summary>
public sealed class SpaceSubscriberEntity
{
    /// <summary>The space URI. Part of the composite primary key.</summary>
    [MaxLength(512)]
    public required string Space { get; set; }

    /// <summary>
    /// The subscriber's service identifier — a DID with an optional fragment. Part of the
    /// composite primary key.
    /// </summary>
    [MaxLength(512)]
    public required string Service { get; set; }

    /// <summary>
    /// When the registration lapses. Stored as Unix milliseconds, and so read back as UTC.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// A space as <c>com.atproto.simplespace</c> stores it.
/// </summary>
public sealed class SimpleSpaceEntity
{
    /// <summary>The space URI. Primary key.</summary>
    [Key]
    [MaxLength(512)]
    public required string Space { get; set; }

    /// <summary>The DID of the account that created the space, and the only one that may administer it.</summary>
    [MaxLength(512)]
    public required string Owner { get; set; }

    /// <summary>
    /// The user policy, as the JSON of its Lexicon union variant (carrying its <c>$type</c>).
    /// </summary>
    /// <remarks>
    /// Stored as the wire form rather than as columns so a policy variant added to the union
    /// later needs no schema change — the discriminator is what a
    /// <see cref="ATProtoNet.Lexicon.Com.AtProto.SimpleSpace.SimpleSpaceUserPolicy"/> is
    /// identified by everywhere else too.
    /// </remarks>
    public required string Policy { get; set; }

    /// <summary>The app access policy, as the JSON of its Lexicon union variant.</summary>
    public required string AppAccess { get; set; }

    /// <summary>Whether the space has been deleted.</summary>
    public bool Deleted { get; set; }
}

/// <summary>
/// One DID on a space's member list.
/// </summary>
/// <remarks>
/// Unlike the writer set, this is never published to the network and cannot be rebuilt from
/// anything on it — which is why an authority that means to survive a restart must keep it here
/// rather than in memory.
/// </remarks>
public sealed class SimpleSpaceMemberEntity
{
    /// <summary>The space URI. Part of the composite primary key.</summary>
    [MaxLength(512)]
    public required string Space { get; set; }

    /// <summary>The member's DID. Part of the composite primary key.</summary>
    [MaxLength(512)]
    public required string Did { get; set; }
}

/// <summary>
/// A single-use token identifier this service has already accepted.
/// </summary>
/// <remarks>
/// The primary key is <c>(Issuer, TokenId, ExpiresAt)</c>, matching how
/// <see cref="ATProtoNet.Server.Spaces.ISpaceReplayStore"/> is keyed: the uniqueness of that key
/// <em>is</em> the replay check, so consuming a token is one insert with no read-modify-write.
/// </remarks>
public sealed class SpaceReplayEntity
{
    /// <summary>The token's <c>iss</c>, which scopes the identifier. Part of the composite primary key.</summary>
    [MaxLength(512)]
    public required string Issuer { get; set; }

    /// <summary>The token's <c>jti</c>. Part of the composite primary key.</summary>
    [MaxLength(255)]
    public required string TokenId { get; set; }

    /// <summary>
    /// The token's expiry as Unix seconds. Part of the composite primary key, and what expired
    /// rows are swept on.
    /// </summary>
    /// <remarks>
    /// Held as a number rather than a timestamp so the key is byte-identical across providers
    /// that store <see cref="DateTimeOffset"/> differently, and so the sweep is a plain integer
    /// comparison.
    /// </remarks>
    public long ExpiresAt { get; set; }
}
