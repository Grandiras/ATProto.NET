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
    private readonly IInviteCodeStore _inviteCodes;
    private readonly PdsRepoManager? _repoManager;
    private readonly PdsIdentityService? _identity;

    /// <summary>
    /// Creates a new PDS service without federation support: accounts get locally generated
    /// DIDs and writes produce no signed commits or firehose events.
    /// </summary>
    /// <param name="accounts">Account store.</param>
    /// <param name="repos">Repository store.</param>
    /// <param name="sessions">Session/token service.</param>
    /// <param name="options">PDS configuration.</param>
    /// <param name="inviteCodes">
    /// Invite code store consulted when <see cref="PdsOptions.OpenRegistration"/> is
    /// <c>false</c>. When omitted, an empty <see cref="InMemoryInviteCodeStore"/> is used —
    /// closed registration then rejects every code until one is issued through
    /// <see cref="CreateInviteCodeAsync"/>.
    /// </param>
    /// <remarks>
    /// For hosts that construct the service themselves. <c>AddAtProtoPds()</c> always registers
    /// the federation services and builds the service through the constructor below — see
    /// <c>PdsHostingExtensions.CreatePdsService</c>.
    /// </remarks>
    public PdsService(
        IAccountStore accounts,
        IRepoStore repos,
        PdsSessionService sessions,
        PdsOptions options,
        IInviteCodeStore? inviteCodes = null)
        : this(accounts, repos, sessions, options, repoManager: null, identity: null, inviteCodes)
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
    /// <param name="inviteCodes">
    /// Invite code store consulted when <see cref="PdsOptions.OpenRegistration"/> is
    /// <c>false</c>. When omitted, an empty <see cref="InMemoryInviteCodeStore"/> is used —
    /// closed registration then rejects every code until one is issued through
    /// <see cref="CreateInviteCodeAsync"/>.
    /// </param>
    public PdsService(
        IAccountStore accounts,
        IRepoStore repos,
        PdsSessionService sessions,
        PdsOptions options,
        PdsRepoManager? repoManager,
        PdsIdentityService? identity,
        IInviteCodeStore? inviteCodes = null)
    {
        _accounts = accounts;
        _repos = repos;
        _sessions = sessions;
        _options = options;
        _inviteCodes = inviteCodes ?? new InMemoryInviteCodeStore();
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
    /// <param name="inviteCode">
    /// Invite code. Required when open registration is disabled, in which case it is
    /// validated against — and consumed from — the <see cref="IInviteCodeStore"/>.
    /// Ignored when open registration is enabled.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session result with tokens.</returns>
    /// <exception cref="PdsException">
    /// <c>InvalidInviteCode</c> if the code is missing, unknown, disabled, or exhausted;
    /// <c>HandleNotAvailable</c> if the handle is taken.
    /// </exception>
    public async Task<PdsSessionResult> CreateAccountAsync(
        string handle, string? email, string password,
        string? did = null, string? inviteCode = null,
        CancellationToken cancellationToken = default)
    {
        // Reserve a use of the code up front so two concurrent sign-ups racing on the last
        // use of a code can't both win. The reservation is released again if anything below
        // fails, and only turns into a recorded use once the account actually exists.
        string? claimedCode = null;
        if (!_options.OpenRegistration)
        {
            if (string.IsNullOrWhiteSpace(inviteCode))
                throw new PdsException("InvalidInviteCode", "An invite code is required to create an account.");

            if (!await _inviteCodes.TryClaimAsync(inviteCode, cancellationToken))
                throw new PdsException("InvalidInviteCode", "The invite code is invalid, disabled, or has already been used.");

            claimedCode = inviteCode;
        }

        string accountDid;
        try
        {
            if (await _accounts.HandleExistsAsync(handle, cancellationToken))
                throw new PdsException("HandleNotAvailable", $"The handle '{handle}' is already taken.");

            // Mint the identity. With federation enabled this derives a real did:plc from a signed
            // genesis operation (or a did:web from the handle); otherwise it falls back to a locally
            // generated placeholder that nothing on the network can resolve.
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
        }
        catch
        {
            if (claimedCode is not null)
                await _inviteCodes.ReleaseClaimAsync(claimedCode, CancellationToken.None);
            throw;
        }

        if (claimedCode is not null)
            await _inviteCodes.ConfirmClaimAsync(claimedCode, accountDid, CancellationToken.None);

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
    //  Invite codes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Issue a single invite code.
    /// </summary>
    /// <param name="useCount">How many accounts the code may create. Must be at least 1.</param>
    /// <param name="forAccount">DID the code is issued to, or <c>null</c> for an admin code.</param>
    /// <param name="createdBy">Who issued the code. Defaults to <c>"admin"</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated code.</returns>
    public async Task<string> CreateInviteCodeAsync(
        int useCount = 1, string? forAccount = null, string createdBy = "admin",
        CancellationToken cancellationToken = default)
    {
        if (useCount < 1)
            throw new PdsException("InvalidRequest", "useCount must be at least 1.");

        var code = GenerateInviteCode();
        await _inviteCodes.CreateAsync(
            new PdsInviteCode
            {
                Code = code,
                AvailableUses = useCount,
                ForAccount = forAccount,
                CreatedBy = createdBy,
            },
            cancellationToken);

        return code;
    }

    /// <summary>
    /// Issue several invite codes at once, optionally one batch per account.
    /// </summary>
    /// <param name="codeCount">How many codes to issue per account. Must be at least 1.</param>
    /// <param name="useCount">How many accounts each code may create. Must be at least 1.</param>
    /// <param name="forAccounts">
    /// DIDs to issue codes to. When <c>null</c> or empty, one batch of admin codes is issued.
    /// </param>
    /// <param name="createdBy">Who issued the codes. Defaults to <c>"admin"</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <b>Not atomic.</b> Codes are written one at a time through <see cref="IInviteCodeStore"/>,
    /// which has no transaction seam. Arguments are validated before anything is written, so a
    /// bad <paramref name="codeCount"/>/<paramref name="useCount"/> creates nothing — but if the
    /// store fails partway through, the codes already written stay written and this method throws
    /// without reporting which they were. Recover by listing the affected accounts' codes with
    /// <see cref="GetAccountInviteCodesAsync"/> and disabling the unwanted ones with
    /// <see cref="DisableInviteCodesAsync"/> rather than by retrying blindly, which would leave
    /// the accounts that succeeded the first time holding two batches.
    /// </remarks>
    public async Task<IReadOnlyList<PdsAccountInviteCodes>> CreateInviteCodesAsync(
        int codeCount, int useCount, IReadOnlyList<string>? forAccounts = null,
        string createdBy = "admin", CancellationToken cancellationToken = default)
    {
        if (codeCount < 1)
            throw new PdsException("InvalidRequest", "codeCount must be at least 1.");

        // Checked here as well as in CreateInviteCodeAsync: rejecting up front means an invalid
        // useCount cannot leave one account's batch written and the rest not.
        if (useCount < 1)
            throw new PdsException("InvalidRequest", "useCount must be at least 1.");

        // A null entry means "not bound to an account" — i.e. one batch of admin codes.
        IReadOnlyList<string?> accounts = forAccounts is { Count: > 0 } ? [.. forAccounts] : [null];
        var result = new List<PdsAccountInviteCodes>(accounts.Count);

        foreach (var account in accounts)
        {
            var codes = new List<string>(codeCount);
            for (var i = 0; i < codeCount; i++)
                codes.Add(await CreateInviteCodeAsync(useCount, account, createdBy, cancellationToken));

            result.Add(new PdsAccountInviteCodes { Account = account ?? createdBy, Codes = codes });
        }

        return result;
    }

    /// <summary>
    /// List invite codes for the admin <c>com.atproto.admin.getInviteCodes</c> endpoint.
    /// </summary>
    public Task<InviteCodePage> GetInviteCodesAsync(
        InviteCodeQuery query, CancellationToken cancellationToken = default)
        => _inviteCodes.ListAsync(query, cancellationToken);

    /// <summary>
    /// List the invite codes belonging to an account.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="includeUsed">Whether to include codes with no uses left. Default: true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<PdsInviteCode>> GetAccountInviteCodesAsync(
        string did, bool includeUsed = true, CancellationToken cancellationToken = default)
    {
        var page = await _inviteCodes.ListAsync(
            new InviteCodeQuery { ForAccount = did, Limit = int.MaxValue }, cancellationToken);

        return includeUsed
            ? page.Codes
            : [.. page.Codes.Where(c => !c.Disabled && c.RemainingUses > 0)];
    }

    /// <summary>
    /// Disable invite codes by value and/or by owning account.
    /// </summary>
    /// <param name="codes">Codes to disable.</param>
    /// <param name="accounts">DIDs whose codes should all be disabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many codes were disabled.</returns>
    public async Task<int> DisableInviteCodesAsync(
        IEnumerable<string>? codes, IEnumerable<string>? accounts,
        CancellationToken cancellationToken = default)
    {
        var disabled = 0;
        if (codes is not null)
            disabled += await _inviteCodes.DisableAsync(codes, cancellationToken);
        if (accounts is not null)
            disabled += await _inviteCodes.DisableForAccountsAsync(accounts, cancellationToken);

        return disabled;
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

    // Lowercase alphanumerics minus the pairs that are easy to misread aloud or in a
    // sans-serif font (0/o, 1/l, i, u — which also keeps codes free of accidental words).
    private const string InviteCodeAlphabet = "abcdefghjkmnpqrstvwxyz23456789";

    /// <summary>
    /// Generates a code in the reference PDS's shape: the hostname with its dots turned into
    /// dashes, followed by two random five-character groups (e.g. <c>my-pds-example-com-a2b3c-d4e5f</c>).
    /// </summary>
    private string GenerateInviteCode()
    {
        var host = _options.Hostname.Replace('.', '-').ToLowerInvariant();
        return $"{host}-{RandomGroup()}-{RandomGroup()}";

        static string RandomGroup()
        {
            Span<char> chars = stackalloc char[5];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = InviteCodeAlphabet[RandomNumberGenerator.GetInt32(InviteCodeAlphabet.Length)];
            return new string(chars);
        }
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
