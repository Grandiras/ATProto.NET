using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Pds;

/// <summary>
/// Core PDS service that implements account management, session handling,
/// and record CRUD operations. This is the main entry point for PDS business logic.
/// </summary>
public sealed class PdsService
{
    private readonly IAccountStore _accounts;
    private readonly IRepoStore _repos;
    private readonly PdsSessionService _sessions;
    private readonly PdsOptions _options;
    private readonly PdsRepoManager? _repoManager;
    private readonly PdsIdentityService? _identity;

    /// <summary>
    /// Creates a new PDS service without federation support: accounts get locally generated
    /// DIDs and writes produce no signed commits or firehose events.
    /// </summary>
    /// <remarks>
    /// For hosts that construct the service themselves. <c>AddAtProtoPds()</c> always registers
    /// the federation services and builds the service through the constructor below — see
    /// <c>PdsHostingExtensions.CreatePdsService</c>.
    /// </remarks>
    public PdsService(
        IAccountStore accounts,
        IRepoStore repos,
        PdsSessionService sessions,
        PdsOptions options)
        : this(accounts, repos, sessions, options, repoManager: null, identity: null)
    {
    }

    /// <summary>
    /// Creates a new PDS service that maintains a federating repository.
    /// </summary>
    /// <param name="accounts">The account store.</param>
    /// <param name="repos">The record and blob store.</param>
    /// <param name="sessions">The session/token service.</param>
    /// <param name="options">PDS configuration.</param>
    /// <param name="repoManager">
    /// Signs a commit and publishes a firehose event after every repository write. When
    /// <c>null</c>, the PDS keeps serving repo CRUD but nothing on the network can follow it.
    /// A host wired through <c>AddAtProtoPds()</c> always gets one; passing <c>null</c> is for
    /// hosts that construct the service themselves. Note that a non-null manager is not itself a
    /// guarantee of federation: if the configured <see cref="IRepoStore"/> cannot enumerate a
    /// repository, <see cref="PdsRepoManager.CommitAsync"/> degrades to a no-op so that writes
    /// keep succeeding.
    /// </param>
    /// <param name="identity">
    /// Mints real DIDs for new accounts. When <c>null</c>, a locally derived placeholder DID is
    /// generated instead.
    /// </param>
    public PdsService(
        IAccountStore accounts,
        IRepoStore repos,
        PdsSessionService sessions,
        PdsOptions options,
        PdsRepoManager? repoManager,
        PdsIdentityService? identity)
    {
        _accounts = accounts;
        _repos = repos;
        _sessions = sessions;
        _options = options;
        _repoManager = repoManager;
        _identity = identity;
    }

    /// <summary>
    /// The repository manager backing this PDS, or <c>null</c> when federation is not enabled.
    /// </summary>
    public PdsRepoManager? RepoManager => _repoManager;

    /// <summary>
    /// The identity service backing this PDS, or <c>null</c> when federation is not enabled.
    /// </summary>
    public PdsIdentityService? Identity => _identity;

    // ──────────────────────────────────────────────────────────
    //  Account management
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new account on this PDS.
    /// </summary>
    /// <param name="handle">Desired handle.</param>
    /// <param name="email">Email address.</param>
    /// <param name="password">Password (will be hashed).</param>
    /// <param name="did">Optional DID. If null, a placeholder DID is generated.</param>
    /// <param name="inviteCode">Invite code (required if open registration is disabled).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session result with tokens.</returns>
    public async Task<PdsSessionResult> CreateAccountAsync(
        string handle, string? email, string password,
        string? did = null, string? inviteCode = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.OpenRegistration && string.IsNullOrEmpty(inviteCode))
            throw new PdsException("InvalidInviteCode", "An invite code is required to create an account.");

        if (await _accounts.HandleExistsAsync(handle, cancellationToken))
            throw new PdsException("HandleNotAvailable", $"The handle '{handle}' is already taken.");

        // Mint the identity. With federation enabled this derives a real did:plc from a signed
        // genesis operation (or a did:web from the handle); otherwise it falls back to a locally
        // generated placeholder that nothing on the network can resolve.
        string accountDid;
        string signingKey;
        string? rotationKey = null;

        if (_identity is not null)
        {
            var identity = await _identity.CreateIdentityAsync(handle, did, cancellationToken);
            accountDid = identity.Did;
            signingKey = identity.SigningKey;
            rotationKey = identity.RotationKey;
        }
        else
        {
            accountDid = did ?? $"did:plc:{GenerateRandomTid()}";

            using var ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            signingKey = Convert.ToBase64String(ecKey.ExportECPrivateKey());
        }

        var passwordHash = HashPassword(password);

        var account = new PdsAccount
        {
            Did = accountDid,
            Handle = handle,
            Email = email,
            PasswordHash = passwordHash,
            SigningKey = signingKey,
            RotationKey = rotationKey,
        };

        await _accounts.CreateAsync(account, cancellationToken);

        if (_repoManager is not null)
        {
            // Announce the account and give it an empty signed commit, so a relay crawling this
            // PDS finds a valid (if empty) repository rather than a DID with no head.
            _repoManager.PublishAccount(accountDid, active: true);
            _repoManager.PublishIdentity(accountDid, handle);
            await _repoManager.EnsureRepoAsync(accountDid, cancellationToken);
        }

        var accessJwt = _sessions.IssueAccessToken(accountDid, handle);
        var refreshJwt = _sessions.IssueRefreshToken(accountDid);

        return new PdsSessionResult
        {
            Did = accountDid,
            Handle = handle,
            AccessJwt = accessJwt,
            RefreshJwt = refreshJwt,
        };
    }

    /// <summary>
    /// Create a session (log in) with identifier and password.
    /// </summary>
    /// <param name="identifier">Handle, email, or DID.</param>
    /// <param name="password">Password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PdsSessionResult> CreateSessionAsync(
        string identifier, string password,
        CancellationToken cancellationToken = default)
    {
        var account = await ResolveAccountAsync(identifier, cancellationToken)
            ?? throw new PdsException("AuthenticationRequired", "Invalid identifier or password.");

        if (!VerifyPassword(password, account.PasswordHash))
            throw new PdsException("AuthenticationRequired", "Invalid identifier or password.");

        if (!account.IsActive)
            throw new PdsException("AccountTakedown", "Account is not active.");

        var accessJwt = _sessions.IssueAccessToken(account.Did, account.Handle);
        var refreshJwt = _sessions.IssueRefreshToken(account.Did);

        return new PdsSessionResult
        {
            Did = account.Did,
            Handle = account.Handle,
            Email = account.Email,
            EmailConfirmed = account.EmailConfirmed,
            AccessJwt = accessJwt,
            RefreshJwt = refreshJwt,
        };
    }

    /// <summary>
    /// Get the current session information for a DID.
    /// </summary>
    public async Task<PdsSessionInfo> GetSessionAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByDidAsync(did, cancellationToken)
            ?? throw new PdsException("InvalidToken", "Session not found.");

        return new PdsSessionInfo
        {
            Did = account.Did,
            Handle = account.Handle,
            Email = account.Email,
            EmailConfirmed = account.EmailConfirmed,
            Active = account.IsActive,
        };
    }

    /// <summary>
    /// Refresh session tokens.
    /// </summary>
    public async Task<PdsSessionResult> RefreshSessionAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByDidAsync(did, cancellationToken)
            ?? throw new PdsException("ExpiredToken", "Session not found.");

        var accessJwt = _sessions.IssueAccessToken(account.Did, account.Handle);
        var refreshJwt = _sessions.IssueRefreshToken(account.Did);

        return new PdsSessionResult
        {
            Did = account.Did,
            Handle = account.Handle,
            Email = account.Email,
            EmailConfirmed = account.EmailConfirmed,
            AccessJwt = accessJwt,
            RefreshJwt = refreshJwt,
        };
    }

    /// <summary>
    /// Delete an account.
    /// </summary>
    public async Task DeleteAccountAsync(
        string did, string password,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByDidAsync(did, cancellationToken)
            ?? throw new PdsException("InvalidToken", "Account not found.");

        if (!VerifyPassword(password, account.PasswordHash))
            throw new PdsException("AuthenticationRequired", "Invalid password.");

        await _repos.DeleteAllAsync(did, cancellationToken);
        await _accounts.DeleteAsync(did, cancellationToken);

        if (_repoManager is not null)
            await _repoManager.DeleteRepoAsync(did, cancellationToken);
    }

    /// <summary>
    /// Describe this PDS server.
    /// </summary>
    public PdsDescription DescribeServer()
    {
        return new PdsDescription
        {
            InviteCodeRequired = !_options.OpenRegistration,
            AvailableUserDomains = _options.AvailableUserDomains,
            Contact = _options.ContactEmail is not null
                ? new PdsContact { Email = _options.ContactEmail }
                : null,
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Record operations
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a record in a repository.
    /// </summary>
    public async Task<PdsRecordRef> CreateRecordAsync(
        string did, string collection, JsonElement record,
        string? rkey = null, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByDidAsync(did, cancellationToken)
            ?? throw new PdsException("RepoNotFound", "Repository not found.");

        if (!account.IsActive)
            throw new PdsException("RepoDeactivated", "Account is not active.");

        var actualRkey = rkey ?? Tid.NextString();
        var (cid, binaryCid) = ComputeRecordCid(record);

        var repoRecord = new RepoRecord
        {
            Did = did,
            Collection = collection,
            Rkey = actualRkey,
            Value = record,
            Cid = cid,
        };

        await _repos.PutRecordAsync(repoRecord, cancellationToken);
        await CommitAsync(did,
            [PdsRepoOp.Create($"{collection}/{actualRkey}", binaryCid)], cancellationToken);

        return new PdsRecordRef
        {
            Uri = $"at://{did}/{collection}/{actualRkey}",
            Cid = cid,
        };
    }

    /// <summary>
    /// Get a record from a repository.
    /// </summary>
    public async Task<PdsRecordResult?> GetRecordAsync(
        string did, string collection, string rkey,
        CancellationToken cancellationToken = default)
    {
        var record = await _repos.GetRecordAsync(did, collection, rkey, cancellationToken);
        if (record is null) return null;

        return new PdsRecordResult
        {
            Uri = $"at://{did}/{collection}/{rkey}",
            Cid = record.Cid,
            Value = record.Value,
        };
    }

    /// <summary>
    /// Put (upsert) a record.
    /// </summary>
    public async Task<PdsRecordRef> PutRecordAsync(
        string did, string collection, string rkey, JsonElement record,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByDidAsync(did, cancellationToken)
            ?? throw new PdsException("RepoNotFound", "Repository not found.");

        if (!account.IsActive)
            throw new PdsException("RepoDeactivated", "Account is not active.");

        var existing = await _repos.GetRecordAsync(did, collection, rkey, cancellationToken);
        var (cid, binaryCid) = ComputeRecordCid(record);

        var repoRecord = new RepoRecord
        {
            Did = did,
            Collection = collection,
            Rkey = rkey,
            Value = record,
            Cid = cid,
        };

        await _repos.PutRecordAsync(repoRecord, cancellationToken);

        var path = $"{collection}/{rkey}";
        var op = existing is null
            ? PdsRepoOp.Create(path, binaryCid)
            : PdsRepoOp.Update(path, binaryCid, ToBinaryCid(existing.Cid));
        await CommitAsync(did, [op], cancellationToken);

        return new PdsRecordRef
        {
            Uri = $"at://{did}/{collection}/{rkey}",
            Cid = cid,
        };
    }

    /// <summary>
    /// Delete a record.
    /// </summary>
    public async Task DeleteRecordAsync(
        string did, string collection, string rkey,
        CancellationToken cancellationToken = default)
    {
        var existing = await _repos.GetRecordAsync(did, collection, rkey, cancellationToken);
        var deleted = await _repos.DeleteRecordAsync(did, collection, rkey, cancellationToken);

        // Only commit when something actually changed — deleting a record that was never there
        // must not bump the repo's revision, or every no-op delete would look like a new
        // version of the repo to anyone following the firehose.
        if (deleted)
        {
            await CommitAsync(did,
                [PdsRepoOp.Delete($"{collection}/{rkey}", ToBinaryCid(existing?.Cid))],
                cancellationToken);
        }
    }

    /// <summary>
    /// List records in a collection.
    /// </summary>
    public Task<RecordPage> ListRecordsAsync(
        string did, string collection,
        int limit = 50, string? cursor = null, bool reverse = false,
        CancellationToken cancellationToken = default)
    {
        return _repos.ListRecordsAsync(did, collection, limit, cursor, reverse, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Blob operations
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a blob.
    /// </summary>
    public async Task<PdsBlobRef> UploadBlobAsync(
        string did, byte[] data, string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (data.Length > _options.MaxBlobSize)
            throw new PdsException("BlobTooLarge",
                $"Blob size {data.Length} exceeds maximum of {_options.MaxBlobSize} bytes.");

        var cid = ComputeBlobCid(data);

        var blob = new RepoBlob
        {
            Did = did,
            Cid = cid,
            MimeType = mimeType,
            Size = data.Length,
            Data = data,
        };

        await _repos.PutBlobAsync(blob, cancellationToken);

        return new PdsBlobRef
        {
            Cid = cid,
            MimeType = mimeType,
            Size = data.Length,
        };
    }

    /// <summary>
    /// Get a blob.
    /// </summary>
    public Task<RepoBlob?> GetBlobAsync(
        string did, string cid,
        CancellationToken cancellationToken = default)
    {
        return _repos.GetBlobAsync(did, cid, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private async Task<PdsAccount?> ResolveAccountAsync(string identifier, CancellationToken ct)
    {
        if (identifier.StartsWith("did:", StringComparison.Ordinal))
            return await _accounts.GetByDidAsync(identifier, ct);

        if (identifier.Contains('@'))
            return await _accounts.GetByEmailAsync(identifier, ct);

        return await _accounts.GetByHandleAsync(identifier, ct);
    }

    private static string GenerateRandomTid()
    {
        // Generate a random TID-like string (13 chars, base32-sortable)
        Span<byte> bytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant()[..13];
    }

    /// <summary>
    /// Commits the current repository state, when federation is enabled. A record store that
    /// cannot enumerate a repository makes this a no-op rather than an error, so repo CRUD keeps
    /// working on stores written before federation support.
    /// </summary>
    private async Task CommitAsync(string did, IReadOnlyList<PdsRepoOp> ops, CancellationToken cancellationToken)
    {
        if (_repoManager is null) return;
        await _repoManager.CommitAsync(did, ops, cancellationToken);
    }

    /// <summary>
    /// Computes a record's real content address: CIDv1 over its DAG-CBOR encoding, with the
    /// dag-cbor (0x71) codec and a SHA-256 multihash.
    /// </summary>
    private static (string Cid, byte[] BinaryCid) ComputeRecordCid(JsonElement element)
    {
        var (_, binaryCid) = PdsRepoManager.EncodeRecord(element);
        return (CidComputation.EncodeCidToString(binaryCid), binaryCid);
    }

    /// <summary>
    /// Computes a blob's content address: CIDv1 with the raw (0x55) codec, as blobs are opaque
    /// bytes rather than DAG-CBOR.
    /// </summary>
    private static string ComputeBlobCid(byte[] data)
        => CidComputation.ComputeForRaw(data).Value;

    private static byte[]? ToBinaryCid(string? cid)
        => CidComputation.TryDecodeCidString(cid, out var binary) ? binary : null;

    internal static string HashPassword(string password)
    {
        // Use a simple PBKDF2-based hash for password storage
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations: 100_000, HashAlgorithmName.SHA256, outputLength: 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    internal static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations: 100_000, HashAlgorithmName.SHA256, outputLength: 32);

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
