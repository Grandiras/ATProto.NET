namespace ATProtoNet.Identity;

/// <summary>Dispatches each DID to the resolver of its method.</summary>
/// <param name="methods">The resolver of each method, by method name (<c>plc</c>, <c>web</c>). The caller owns them.</param>
public sealed class DidResolver(IReadOnlyDictionary<string, IDidResolver> methods) : IDidResolver
{
    private readonly IReadOnlyDictionary<string, IDidResolver> _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    /// <inheritdoc />
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) => For(did).ResolveAsync(did, cancellationToken);

    /// <inheritdoc />
    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default) => For(did).RefreshAsync(did, cancellationToken);

    /// <inheritdoc />
    public Task<DidDocument> FetchFreshAsync(Did did, CancellationToken cancellationToken = default) => For(did).FetchFreshAsync(did, cancellationToken);

    /// <inheritdoc />
    public ValueTask InvalidateAsync(Did did, CancellationToken cancellationToken = default) =>
        _methods.TryGetValue(did?.Method ?? throw new ArgumentNullException(nameof(did)), out var resolver) ? resolver.InvalidateAsync(did, cancellationToken) : ValueTask.CompletedTask;

    private IDidResolver For(Did did)
    {
        ArgumentNullException.ThrowIfNull(did);
        return _methods.TryGetValue(did.Method, out var resolver) ? resolver
            : throw new IdentityResolutionException(IdentityResolutionError.UnsupportedMethod, $"'{did}' uses the unsupported DID method '{did.Method}'.", did);
    }
}
