using System.ComponentModel.DataAnnotations;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>A space this authority gates, and whether it has been deleted.</summary>
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

    /// <summary>The last space revision (a TID) assigned in the space, or <see langword="null"/> before the first.</summary>
    /// <remarks>
    /// The sequencer: a new revision is claimed by an update conditioned on this value, so concurrent
    /// writers of one space are serialized and revisions commit in increasing order.
    /// </remarks>
    [MaxLength(64)]
    public string? LastSpaceRev { get; set; }
}

/// <summary>One account's entry in a space's writer set, as last reported to the authority.</summary>
/// <remarks>
/// The writer set is the sync boundary, not an access-control list. Each entry carries the
/// repo revision and commit hash from the last <c>notifyWrite</c> and the space revision it was
/// sequenced at, which is what lets a syncer resume from a checkpoint and re-sync only the repos
/// that advanced.
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
    public required string RepoRev { get; set; }

    /// <summary>The space revision (a TID) this entry was last sequenced at.</summary>
    /// <remarks>Ordered and compared as a string, so the column needs an ordinal (binary) collation; SQLite's default is one.</remarks>
    [MaxLength(64)]
    public required string SpaceRev { get; set; }

    /// <summary>The repo's commit hash as last reported.</summary>
    public required byte[] Hash { get; set; }
}

/// <summary>A service registered to receive a space's write notifications.</summary>
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

    /// <summary>When the registration lapses. Stored as Unix milliseconds, and so read back as UTC.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A space as <c>com.atproto.simplespace</c> stores it.</summary>
public sealed class SimpleSpaceEntity
{
    /// <summary>The space URI. Primary key.</summary>
    [Key]
    [MaxLength(512)]
    public required string Space { get; set; }

    /// <summary>The DID of the account that created the space, and the only one that may administer it.</summary>
    [MaxLength(512)]
    public required string Owner { get; set; }

    /// <summary>The read policy, as the JSON of its Lexicon union variant (carrying its <c>$type</c>).</summary>
    /// <remarks>
    /// Stored as the wire form rather than as columns so a policy variant added to the union
    /// later needs no schema change — the discriminator is what a
    /// <see cref="ATProtoNet.Lexicon.Com.AtProto.SimpleSpace.SimpleSpaceUserPolicy"/> is
    /// identified by everywhere else too.
    /// </remarks>
    public required string ReadPolicy { get; set; }

    /// <summary>The write policy, as the JSON of its Lexicon union variant.</summary>
    public required string WritePolicy { get; set; }

    /// <summary>The app access policy, as the JSON of its Lexicon union variant.</summary>
    public required string AppAccess { get; set; }

    /// <summary>Whether the space has been deleted.</summary>
    public bool Deleted { get; set; }
}

/// <summary>One DID on a space's member list, and its access.</summary>
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

    /// <summary>Whether the member may read under a member-list read policy.</summary>
    public bool Read { get; set; }

    /// <summary>Whether the member's writes are tracked under a member-list write policy.</summary>
    public bool Write { get; set; }
}
