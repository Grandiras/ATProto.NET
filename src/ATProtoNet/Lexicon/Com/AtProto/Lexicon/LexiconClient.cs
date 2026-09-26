using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Com.AtProto.Lexicon;

/// <summary>
/// Client for com.atproto.lexicon.* XRPC endpoints: Lexicon resolution delegated to a service.
/// </summary>
/// <remarks>
/// As an <see cref="ILexiconResolver"/> it trusts the service's answer, checking only that the
/// schema is the one asked for. To resolve and verify locally, use <see cref="LexiconResolver"/>.
/// The schema records themselves are ordinary records: publish one with
/// <c>client.Repo.PutRecordAsync</c> under <see cref="LexiconSchemaRecord.Collection"/>, keyed by
/// the NSID.
/// </remarks>
public sealed class LexiconClient : ILexiconResolver
{
    private readonly XrpcClient _xrpc;

    internal LexiconClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Asks the service to resolve an NSID to its published schema
    /// (<c>com.atproto.lexicon.resolveLexicon</c>).
    /// </summary>
    /// <param name="nsid">The NSID of the schema.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema record, its AT URI and its CID, as the service reports them.</returns>
    /// <exception cref="XrpcException">
    /// Thrown with <see cref="XrpcErrors.LexiconNotFound"/> when the service resolved no schema.
    /// </exception>
    public Task<ResolvedLexicon> ResolveLexiconAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);

        var parameters = new XrpcParams().Add("nsid", nsid);
        return _xrpc.QueryAsync<ResolvedLexicon>(
            "com.atproto.lexicon.resolveLexicon", parameters, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="ResolveLexiconAsync"/>, reporting <see cref="XrpcErrors.LexiconNotFound"/> as
    /// <see cref="LexiconResolutionErrorKind.NotFound"/>, any other failure as
    /// <see cref="LexiconResolutionErrorKind.ResolutionFailed"/>, and a schema that is not the one
    /// asked for as <see cref="LexiconResolutionErrorKind.InvalidRecord"/>.
    /// </remarks>
    async Task<ResolvedLexicon> ILexiconResolver.ResolveAsync(Nsid nsid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nsid);

        ResolvedLexicon resolved;
        try
        {
            resolved = await ResolveLexiconAsync(nsid, cancellationToken).ConfigureAwait(false);
        }
        catch (XrpcException ex) when (ex.Is(XrpcErrors.LexiconNotFound))
        {
            throw new LexiconResolutionException(
                $"The service resolved no schema for {nsid}.", nsid, LexiconResolutionErrorKind.NotFound, ex);
        }
        catch (Exception ex) when (ex is AtProtoException or HttpRequestException)
        {
            throw new LexiconResolutionException(
                $"The service could not resolve {nsid}: {ex.Message}", nsid, LexiconResolutionErrorKind.ResolutionFailed, ex);
        }

        return LexiconResolver.Validate(nsid, resolved.Uri, resolved.Cid, resolved.Schema);
    }
}
