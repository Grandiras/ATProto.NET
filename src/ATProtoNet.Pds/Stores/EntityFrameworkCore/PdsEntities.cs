using System.ComponentModel.DataAnnotations;

namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// Entity representing a PDS account. Mirrors <see cref="PdsAccount"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Handle"/> and <see cref="Email"/> are stored as written. If you
/// encrypt either column at rest with a non-deterministic scheme (an EF Core value
/// converter, Always Encrypted, a provider-level extension), equality lookups can no
/// longer run in SQL — enable
/// <see cref="PdsEfCoreStoreOptions.ClientSideAccountLookup"/> so
/// <see cref="EfCoreAccountStore{TContext}"/> loads and filters in memory instead.</para>
/// </remarks>
public sealed class PdsAccountEntity
{
    /// <summary>The account's DID. Primary key.</summary>
    [Key]
    [MaxLength(2048)]
    public required string Did { get; set; }

    /// <summary>The user's handle (e.g. alice.example.com).</summary>
    [MaxLength(512)]
    public required string Handle { get; set; }

    /// <summary>The user's email address, if any.</summary>
    [MaxLength(512)]
    public string? Email { get; set; }

    /// <summary>Whether the email has been confirmed.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>Hashed password.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>When the account was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the account is currently active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The base64-encoded private signing key for this account's repo.</summary>
    public required string SigningKey { get; set; }

    /// <summary>
    /// The base64-encoded PLC rotation key that controls the identity, or <see langword="null"/>
    /// for accounts whose DID method needs none. Mirrors <see cref="PdsAccount.RotationKey"/>.
    /// </summary>
    public string? RotationKey { get; set; }
}

/// <summary>
/// Entity representing a single record in a repository collection.
/// Mirrors <see cref="RepoRecord"/>; the record value is persisted as JSON text.
/// </summary>
public sealed class PdsRecordEntity
{
    /// <summary>The DID of the repository owner. Part of the composite key.</summary>
    [MaxLength(2048)]
    public required string Did { get; set; }

    /// <summary>The collection NSID. Part of the composite key.</summary>
    [MaxLength(512)]
    public required string Collection { get; set; }

    /// <summary>The record key within the collection. Part of the composite key.</summary>
    [MaxLength(512)]
    public required string Rkey { get; set; }

    /// <summary>The record value, serialized as JSON text.</summary>
    public required string Value { get; set; }

    /// <summary>The CID of this version of the record.</summary>
    [MaxLength(256)]
    public required string Cid { get; set; }

    /// <summary>When the record was created or last written (UTC).</summary>
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Entity holding the bytes of a blob, keyed by its content identifier.
/// </summary>
/// <remarks>
/// Blobs are content-addressed, so identical uploads from different accounts share a
/// single row; <see cref="PdsBlobRefEntity"/> records which accounts reference it.
/// The content row is deleted once its last reference goes away.
/// </remarks>
public sealed class PdsBlobEntity
{
    /// <summary>The CID of the blob. Primary key.</summary>
    [Key]
    [MaxLength(256)]
    public required string Cid { get; set; }

    /// <summary>Size in bytes.</summary>
    public required long Size { get; set; }

    /// <summary>The blob data.</summary>
    public required byte[] Data { get; set; }
}

/// <summary>
/// Entity linking an account to a blob it uploaded, so that content-addressed blob
/// rows can be shared between accounts without leaking one account's blobs to another.
/// </summary>
public sealed class PdsBlobRefEntity
{
    /// <summary>The DID of the repository owner. Part of the composite key.</summary>
    [MaxLength(2048)]
    public required string Did { get; set; }

    /// <summary>The CID of the referenced blob. Part of the composite key.</summary>
    [MaxLength(256)]
    public required string Cid { get; set; }

    /// <summary>MIME type as declared by the uploader.</summary>
    [MaxLength(256)]
    public required string MimeType { get; set; }

    /// <summary>When the blob was uploaded by this account (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Entity holding the signed head of a repository. Mirrors <see cref="RepoCommitState"/>.
/// </summary>
/// <remarks>
/// One row per repository — a head replaces its predecessor rather than accumulating, so
/// this table stays the same size as the account table. Persisting it matters for a
/// federating PDS: a lost head restarts the revision sequence and relays see the
/// repository rewind.
/// </remarks>
public sealed class PdsRepoHeadEntity
{
    /// <summary>The DID of the repository. Primary key.</summary>
    [Key]
    [MaxLength(2048)]
    public required string Did { get; set; }

    /// <summary>The CID of the signed commit block.</summary>
    [MaxLength(256)]
    public required string CommitCid { get; set; }

    /// <summary>The commit revision (a TID).</summary>
    [MaxLength(64)]
    public required string Rev { get; set; }

    /// <summary>The CID of the MST root referenced by the commit's <c>data</c> field.</summary>
    [MaxLength(256)]
    public required string DataCid { get; set; }

    /// <summary>The DAG-CBOR bytes of the signed commit block.</summary>
    public required byte[] CommitBlock { get; set; }

    /// <summary>When this commit was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
