using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>Resolves DIDs and handles to identities whose handle is checked in both directions.</summary>
/// <remarks>
/// A handle is verified only when the DID document claims it and the handle resolves back to that DID: anyone can claim any
/// handle in their own document, and anyone can point their domain at someone else's DID. Resolving a DID never fails on its
/// handle: an unverifiable claim is <see cref="Handle.Invalid"/>.
/// </remarks>
public sealed partial class IdentityResolver : IIdentityResolver
{
    internal static readonly ActivitySource Source = new("ATProtoNet.Identity");

    private readonly IDidResolver _dids;
    private readonly IHandleResolver _handles;
    private readonly ILogger _logger;

    /// <summary>Creates a resolver over the given resolvers, which the caller owns.</summary>
    /// <param name="dids">Resolves DIDs; a <see cref="CachingDidResolver"/> in most applications.</param>
    /// <param name="handles">Resolves handles.</param>
    /// <param name="logger">Optional logger.</param>
    public IdentityResolver(IDidResolver dids, IHandleResolver handles, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dids);
        ArgumentNullException.ThrowIfNull(handles);
        (_dids, _handles, _logger) = (dids, handles, logger ?? NullLogger.Instance);
    }

    /// <inheritdoc />
    public async Task<ResolvedIdentity> ResolveAsync(AtIdentifier identifier, IdentityResolveOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var fresh = options?.BypassCache ?? false;
        using var activity = Source.StartActivity("identity resolve");
        if (identifier.Did is { } input)
        {
            var document = await DocumentAsync(input, fresh, cancellationToken).ConfigureAwait(false);
            if (document.GetHandle() is not { } claimed) return new(input, null, false, document);
            var verified = await ResolvesToAsync(claimed, input, fresh, cancellationToken).ConfigureAwait(false);
            return new(input, verified ? claimed : Handle.Invalid, verified, document);
        }

        var handle = identifier.Handle!;
        if (fresh) await _handles.InvalidateAsync(handle, cancellationToken).ConfigureAwait(false);
        var did = await _handles.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            ?? throw new IdentityResolutionException(IdentityResolutionError.HandleNotFound, $"Handle '{handle}' does not resolve to a DID.", handle);
        var resolved = await DocumentAsync(did, fresh, cancellationToken).ConfigureAwait(false);
        var claims = resolved.GetHandle() == handle;
        if (!claims) LogNotClaimed(_logger, handle, did);
        return new(did, claims ? handle : Handle.Invalid, claims, resolved);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(AtIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        if (identifier.Did is { } did) await _dids.InvalidateAsync(did, cancellationToken).ConfigureAwait(false);
        else await _handles.InvalidateAsync(identifier.Handle!, cancellationToken).ConfigureAwait(false);
    }

    private Task<DidDocument> DocumentAsync(Did did, bool fresh, CancellationToken cancellationToken) =>
        fresh ? _dids.FetchFreshAsync(did, cancellationToken) : _dids.ResolveAsync(did, cancellationToken);

    // Whatever the handle's domain (or a custom resolver) does, the DID stands: any failure is an unverified handle.
    private async Task<bool> ResolvesToAsync(Handle claimed, Did did, bool fresh, CancellationToken cancellationToken)
    {
        try
        {
            if (fresh) await _handles.InvalidateAsync(claimed, cancellationToken).ConfigureAwait(false);
            var resolved = await _handles.ResolveAsync(claimed, cancellationToken).ConfigureAwait(false);
            if (resolved != did) LogClaimNotVerified(_logger, null, claimed, did, resolved);
            return resolved == did;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogClaimNotVerified(_logger, ex, claimed, did, null);
            return false;
        }
    }

    [LoggerMessage(1020, LogLevel.Debug, "Handle {Handle} resolves to {Did}, whose document does not claim it; treating it as invalid.")]
    private static partial void LogNotClaimed(ILogger logger, Handle handle, Did did);

    [LoggerMessage(1021, LogLevel.Debug, "Handle {Handle} claimed by {Did} resolves to {Resolved}; treating it as invalid.")]
    private static partial void LogClaimNotVerified(ILogger logger, Exception? exception, Handle handle, Did did, Did? resolved);
}
