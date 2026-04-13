using System.Collections.Concurrent;

namespace ATProtoNet.Pds;

/// <summary>
/// In-memory account store for development and testing.
/// Not suitable for production use.
/// </summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly ConcurrentDictionary<string, PdsAccount> _byDid = new();
    private readonly ConcurrentDictionary<string, string> _handleToDid = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _emailToDid = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task CreateAsync(PdsAccount account, CancellationToken cancellationToken = default)
    {
        if (!_byDid.TryAdd(account.Did, account))
            throw new InvalidOperationException($"Account with DID {account.Did} already exists.");

        _handleToDid[account.Handle] = account.Did;
        if (account.Email is not null)
            _emailToDid[account.Email] = account.Did;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PdsAccount?> GetByDidAsync(string did, CancellationToken cancellationToken = default)
    {
        _byDid.TryGetValue(did, out var account);
        return Task.FromResult(account);
    }

    /// <inheritdoc />
    public Task<PdsAccount?> GetByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (_handleToDid.TryGetValue(handle, out var did))
            return GetByDidAsync(did, cancellationToken);
        return Task.FromResult<PdsAccount?>(null);
    }

    /// <inheritdoc />
    public Task<PdsAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_emailToDid.TryGetValue(email, out var did))
            return GetByDidAsync(did, cancellationToken);
        return Task.FromResult<PdsAccount?>(null);
    }

    /// <inheritdoc />
    public Task UpdateAsync(PdsAccount account, CancellationToken cancellationToken = default)
    {
        _byDid[account.Did] = account;
        _handleToDid[account.Handle] = account.Did;
        if (account.Email is not null)
            _emailToDid[account.Email] = account.Did;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string did, CancellationToken cancellationToken = default)
    {
        if (_byDid.TryRemove(did, out var account))
        {
            _handleToDid.TryRemove(account.Handle, out _);
            if (account.Email is not null)
                _emailToDid.TryRemove(account.Email, out _);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HandleExistsAsync(string handle, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_handleToDid.ContainsKey(handle));
    }
}
