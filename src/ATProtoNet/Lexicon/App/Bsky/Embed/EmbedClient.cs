using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Embed;

/// <summary>
/// Client for app.bsky.embed.* XRPC endpoints.
/// </summary>
public sealed class EmbedClient
{
    private readonly XrpcClient _xrpc;

    internal EmbedClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Resolve the records behind a web page, such as a <c>site.standard.document</c> and its
    /// publication, into an enhanced external embed.
    /// </summary>
    /// <param name="url">The page's canonical URL, typically the one pasted into the composer.</param>
    /// <param name="uris">The AT-URIs of the records that back the page (at most 4).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The view and the references to put into the post's <see cref="ExternalInfo.AssociatedRefs"/>,
    /// or an empty response when the records did not resolve or do not back the URL.
    /// </returns>
    public Task<GetEmbedExternalViewResponse> GetEmbedExternalViewAsync(
        string url, IEnumerable<AtUri> uris, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uris);

        var parameters = new XrpcParams()
            .Add("url", url)
            .AddAll("uris", uris.Select(uri => uri.Value));

        return _xrpc.QueryAsync<GetEmbedExternalViewResponse>(
            "app.bsky.embed.getEmbedExternalView", parameters, cancellationToken: cancellationToken);
    }
}
