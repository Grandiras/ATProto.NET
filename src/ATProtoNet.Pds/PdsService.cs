using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Serialization;

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

    /// <summary>
    /// Creates a new PDS service.
    /// </summary>
    public PdsService(
        IAccountStore accounts,
        IRepoStore repos,
        PdsSessionService sessions,
        PdsOptions options)
    {
        _accounts = accounts;
        _repos = repos;
        _sessions = sessions;
        _options = options;
    }

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

        // Generate a DID if not provided
        var accountDid = did ?? $"did:plc:{GenerateRandomTid()}";

        // Generate a signing key for this account's repo
        using var ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecKey.ExportECPrivateKey();
        var signingKey = Convert.ToBase64String(privateKeyBytes);

        var passwordHash = HashPassword(password);

        var account = new PdsAccount
        {
            Did = accountDid,
            Handle = handle,
            Email = email,
            PasswordHash = passwordHash,
            SigningKey = signingKey,
        };

        await _accounts.CreateAsync(account, cancellationToken);

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

        var actualRkey = rkey ?? GenerateRandomTid();
        var cid = ComputeCid(record);

        var repoRecord = new RepoRecord
        {
            Did = did,
            Collection = collection,
            Rkey = actualRkey,
            Value = record,
            Cid = cid,
        };

        await _repos.PutRecordAsync(repoRecord, cancellationToken);

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

        var cid = ComputeCid(record);

        var repoRecord = new RepoRecord
        {
            Did = did,
            Collection = collection,
            Rkey = rkey,
            Value = record,
            Cid = cid,
        };

        await _repos.PutRecordAsync(repoRecord, cancellationToken);

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
        await _repos.DeleteRecordAsync(did, collection, rkey, cancellationToken);
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

    private static string ComputeCid(JsonElement element)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(element, AtProtoJsonDefaults.Options);
        var hash = SHA256.HashData(bytes);
        return "bafyrei" + Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    private static string ComputeBlobCid(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return "bafkrei" + Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

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
