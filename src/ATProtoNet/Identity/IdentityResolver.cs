using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>
/// Resolves DIDs and handles to verified identities, checking a handle in both directions.
/// </summary>
/// <remarks>
/// <para>A handle is verified when the DID document claims it (<c>alsoKnownAs</c> lists
/// <c>at://handle</c>) and the handle resolves back to that DID. Either half alone proves
/// nothing: anyone can claim any handle in their own document, and anyone can point their own
/// domain at someone else's DID.</para>
/// <para>For a DID, an unverified claimed handle becomes <see cref="Handle.Invalid"/>, including
/// when the handle's authorities cannot be reached: the DID is authoritative and resolved, so the
/// identity is returned rather than failed.</para>
/// </remarks>
public sealed class IdentityResolver : IIdentityResolver, IDisposable
{
    private readonly IDidResolver _didResolver;
    private readonly IHandleResolver _handleResolver;
    private readonly IDisposable[] _owned;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a resolver over the given DID and handle resolvers, which the caller owns.
    /// </summary>
    /// <param name="didResolver">Resolves DIDs; a <see cref="CachingDidResolver"/> in most applications.</param>
    /// <param name="handleResolver">Resolves handles.</param>
    /// <param name="logger">Optional logger.</param>
    public IdentityResolver(IDidResolver didResolver, IHandleResolver handleResolver, ILogger? logger = null)
        : this(didResolver, handleResolver, logger, [])
    {
    }

    private IdentityResolver(IDidResolver didResolver, IHandleResolver handleResolver, ILogger? logger, IDisposable[] owned)
    {
        ArgumentNullException.ThrowIfNull(didResolver);
        ArgumentNullException.ThrowIfNull(handleResolver);

        _didResolver = didResolver;
        _handleResolver = handleResolver;
        _logger = logger ?? NullLogger.Instance;
        _owned = owned;
    }

    /// <summary>
    /// Creates a resolver with the SDK's defaults: a <see cref="CachingDidResolver"/> over a
    /// <see cref="DidResolver"/>, and a <see cref="HandleResolver"/>, all owned by the result.
    /// </summary>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>The resolver. Dispose it to release its clients.</returns>
    public static IdentityResolver CreateDefault(IdentityResolverOptions? options = null, ILogger? logger = null)
    {
        options ??= new IdentityResolverOptions();

        var didResolver = new CachingDidResolver(options);
        var handleResolver = new HandleResolver(options, logger);
        return new IdentityResolver(didResolver, handleResolver, logger, [didResolver, handleResolver]);
    }

    /// <summary>The DID resolver identities are resolved through.</summary>
    public IDidResolver DidResolver => _didResolver;

    /// <inheritdoc/>
    public async Task<ResolvedIdentity> ResolveAsync(AtIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        if (identifier.IsDid)
            return await ResolveDidAsync(identifier.Did, cancellationToken).ConfigureAwait(false);

        var handle = identifier.Handle;
        Did? did;
        try
        {
            did = await _handleResolver.ResolveAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (DidResolutionException or OperationCanceledException))
        {
            throw new DidResolutionException(
                $"Handle '{handle}' could not be resolved: {ex.Message}", DidResolutionErrorKind.HandleNotFound, innerException: ex);
        }

        if (did is null)
            throw new DidResolutionException($"Handle '{handle}' does not resolve to a DID.", DidResolutionErrorKind.HandleNotFound);

        var document = await _didResolver.ResolveAsync(did, cancellationToken).ConfigureAwait(false);
        var verified = document.GetHandle() == handle;

        if (!verified)
        {
            _logger.LogInformation(
                "Handle {Handle} resolves to {Did}, whose document does not claim it; treating it as invalid.",
                handle, did);
        }

        return new ResolvedIdentity(did, verified ? handle : Handle.Invalid, verified, document.GetPdsEndpoint(), document);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The DID resolver's cached document is dropped first (<see cref="IDidResolver.InvalidateAsync"/>),
    /// so the resolution fetches it again. <see cref="IDidResolver.RefreshAsync"/> is not used: a
    /// caching resolver may answer it with a copy fetched moments ago, which is exactly the window
    /// a check against a freshly moved account must not have.
    /// </remarks>
    public async Task<ResolvedIdentity> ResolveUncachedAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        await _didResolver.InvalidateAsync(did, cancellationToken).ConfigureAwait(false);
        return await ResolveDidAsync(did, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResolvedIdentity> ResolveDidAsync(Did did, CancellationToken cancellationToken)
    {
        var document = await _didResolver.ResolveAsync(did, cancellationToken).ConfigureAwait(false);
        var claimed = document.GetHandle();
        if (claimed is null)
            return new ResolvedIdentity(did, null, false, document.GetPdsEndpoint(), document);

        Did? resolved;
        try
        {
            resolved = await _handleResolver.ResolveAsync(claimed, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Whatever the handle's domain (or a custom handle resolver) does, the DID stands: a
            // handle that cannot be verified is handle.invalid, never a failed resolution.
            _logger.LogInformation(ex, "Could not verify handle {Handle} for {Did}; treating it as invalid.", claimed, did);
            resolved = null;
        }

        var verified = resolved == did;
        if (!verified && resolved is not null)
        {
            _logger.LogInformation(
                "Handle {Handle} claimed by {Did} resolves to {Resolved}; treating it as invalid.", claimed, did, resolved);
        }

        return new ResolvedIdentity(did, verified ? claimed : Handle.Invalid, verified, document.GetPdsEndpoint(), document);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }
}
