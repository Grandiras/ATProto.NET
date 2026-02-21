using System.Collections.Concurrent;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Server.TokenStore;

/// <summary>
/// In-memory implementation of <see cref="IAtProtoTokenStore"/>.
/// Suitable for development and single-server deployments.
/// </summary>
/// <remarks>
/// <para>Token data is stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and will be lost when the application restarts. For production use, implement
/// <see cref="IAtProtoTokenStore"/> with a durable, encrypted store.</para>
/// </remarks>
public sealed class InMemoryAtProtoTokenStore : IAtProtoTokenStore
{
    private readonly ConcurrentDictionary<string, AtProtoTokenData> _tokens = new();

    /// <inheritdoc/>
    public Task StoreAsync(string did, AtProtoTokenData data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentNullException.ThrowIfNull(data);

        _tokens[did] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<AtProtoTokenData?> GetAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        _tokens.TryGetValue(did, out var data);
        return Task.FromResult(data);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        _tokens.TryRemove(did, out _);
        return Task.CompletedTask;
    }
}
