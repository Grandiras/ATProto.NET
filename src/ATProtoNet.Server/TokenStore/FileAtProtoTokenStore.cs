using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.TokenStore;

/// <summary>
/// File-based implementation of <see cref="IAtProtoTokenStore"/> that persists tokens
/// to encrypted JSON files using ASP.NET Core Data Protection.
/// </summary>
/// <remarks>
/// <para>Each user's token data is stored in a separate file named <c>{did-hash}.dat</c>
/// within the configured directory. File content is encrypted with Data Protection,
/// so tokens (including the DPoP private key) are protected at rest.</para>
/// <para>Suitable for single-server deployments. For multi-server or cloud scenarios,
/// implement <see cref="IAtProtoTokenStore"/> with a shared store (e.g., database, Redis).</para>
/// </remarks>
public sealed class FileAtProtoTokenStore : IAtProtoTokenStore
{
    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly ILogger<FileAtProtoTokenStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Creates a new <see cref="FileAtProtoTokenStore"/>.
    /// </summary>
    /// <param name="dataProtectionProvider">Data protection provider for encrypting token files.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Options specifying the storage directory.</param>
    public FileAtProtoTokenStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<FileAtProtoTokenStore> logger,
        FileTokenStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _protector = dataProtectionProvider.CreateProtector("ATProtoNet.TokenStore");
        _directory = options?.Directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATProtoNet", "tokens");

        Directory.CreateDirectory(_directory);

        // Set restrictive permissions on Unix (owner-only: rwx------)
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(_directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set restrictive permissions on token store directory");
            }
        }
    }

    /// <inheritdoc/>
    public async Task StoreAsync(string did, AtProtoTokenData data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentNullException.ThrowIfNull(data);

        var filePath = GetFilePath(did);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            var encrypted = _protector.Protect(json);
            await File.WriteAllTextAsync(filePath, encrypted, cancellationToken);
            _logger.LogDebug("Stored tokens for DID {Did}", did);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<AtProtoTokenData?> GetAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        var filePath = GetFilePath(did);

        if (!File.Exists(filePath))
            return null;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var encrypted = await File.ReadAllTextAsync(filePath, cancellationToken);
            var json = _protector.Unprotect(encrypted);
            return JsonSerializer.Deserialize<AtProtoTokenData>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to read token file for DID {Did}; removing corrupted file", did);
            TryDeleteFile(filePath);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        var filePath = GetFilePath(did);
        TryDeleteFile(filePath);
        _logger.LogDebug("Removed tokens for DID {Did}", did);
        return Task.CompletedTask;
    }

    private string GetFilePath(string did)
    {
        // Hash the DID to create a safe filename
        var hash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(did)));

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
/// Configuration options for <see cref="FileAtProtoTokenStore"/>.
/// </summary>
public sealed class FileTokenStoreOptions
{
    /// <summary>
    /// Directory where encrypted token files are stored.
    /// Defaults to <c>{LocalApplicationData}/ATProtoNet/tokens</c>.
    /// </summary>
    public string? Directory { get; set; }
}
