using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.TokenStore;

/// <summary>
/// An <see cref="IAtProtoSessionStore"/> that keeps each account's session in its own file,
/// encrypted with ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>Each session is stored in a file named <c>{did-hash}.dat</c> within the configured
/// directory. The content is encrypted with Data Protection, so tokens (including an OAuth
/// session's DPoP private key) are protected at rest. Files written by the 0.6 token store are
/// read as OAuth sessions.</para>
/// <para>Suitable for single-server deployments. For multi-server or cloud scenarios,
/// implement <see cref="IAtProtoSessionStore"/> with a shared store (e.g., database, Redis).</para>
/// </remarks>
public sealed class FileAtProtoSessionStore : IAtProtoSessionStore
{
    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly ILogger<FileAtProtoSessionStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a new <see cref="FileAtProtoSessionStore"/>.
    /// </summary>
    /// <param name="dataProtectionProvider">Data protection provider for encrypting session files.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Options specifying the storage directory.</param>
    public FileAtProtoSessionStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<FileAtProtoSessionStore> logger,
        FileSessionStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _protector = dataProtectionProvider.CreateProtector("ATProtoNet.TokenStore");
        _directory = options?.Directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATProtoNet", "tokens");

        Directory.CreateDirectory(_directory);
        RestrictToOwner(_directory, UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Narrows a path to owner-only access on Unix. Data Protection already encrypts the
    /// contents, but the file also reveals which accounts this host holds tokens for, and
    /// the default umask would leave it group- and world-readable. No-op on Windows,
    /// where the directory ACL governs.
    /// </summary>
    private void RestrictToOwner(string path, UnixFileMode extra = default)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | extra);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not set restrictive permissions on {Path}", path);
        }
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(AtProtoSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var did = session.Did;
        var filePath = GetFilePath(did);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = AtProtoSessionJson.Serialize(session);
            var encrypted = _protector.Protect(json);

            // Write to a sibling temp file and move it into place, so a crash or a
            // concurrent reader never observes a half-written token file — losing the
            // refresh token that way logs the user out with no way to recover it.
            var tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, encrypted, cancellationToken);
            RestrictToOwner(tempPath);
            File.Move(tempPath, filePath, overwrite: true);

            _logger.LogDebug("Stored the session of {Did}", did);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        var filePath = GetFilePath(did);

        if (!File.Exists(filePath))
            return null;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var encrypted = await File.ReadAllTextAsync(filePath, cancellationToken);
            var json = _protector.Unprotect(encrypted);
            return AtProtoSessionJson.Deserialize(json);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            _logger.LogWarning(ex, "Failed to read the session file of {Did}; removing corrupted file", did);
            TryDeleteFile(filePath);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        var filePath = GetFilePath(did);

        // Under the same lock as StoreAsync: deleting concurrently with a write
        // otherwise leaves the just-written file behind and the logout ineffective.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            TryDeleteFile(filePath);
            _logger.LogDebug("Removed the session of {Did}", did);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetFilePath(Did did)
    {
        // Hash the DID to create a safe filename
        var hash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(did.Value)));

        return Path.Combine(_directory, $"{hash}.dat");
    }

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete token file {Path}", filePath);
        }
    }
}

/// <summary>
/// Configuration options for <see cref="FileAtProtoSessionStore"/>.
/// </summary>
public sealed class FileSessionStoreOptions
{
    /// <summary>
    /// Directory where encrypted session files are stored.
    /// Defaults to <c>{LocalApplicationData}/ATProtoNet/tokens</c>.
    /// </summary>
    public string? Directory { get; set; }
}
