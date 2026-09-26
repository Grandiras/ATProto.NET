using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Unspecced;

/// <summary>
/// Client for app.bsky.unspecced.* XRPC endpoints.
/// </summary>
/// <remarks>
/// These are endpoints the Bluesky app relies on before they are specified. Upstream warns that
/// they may change without notice, so expect this client to follow those changes, breaking ones
/// included.
/// </remarks>
public sealed class UnspeccedClient
{
    private readonly XrpcClient _xrpc;

    internal UnspeccedClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Get a thread around an anchor post as a flat list, the way the Bluesky app shows threads:
    /// the anchor's parents up to the root, then its replies, branching up to a depth.
    /// </summary>
    /// <param name="anchor">The AT-URI of the post to build the thread around; any post of the thread.</param>
    /// <param name="above">Whether to include the anchor's parents (default true).</param>
    /// <param name="below">How many levels of replies to include (0-20, default 6).</param>
    /// <param name="branchingFactor">
    /// Max replies per level below the anchor's direct replies, which are all returned (0-100,
    /// default 10).
    /// </param>
    /// <param name="sort">The reply order (see <see cref="PostThreadSort"/>; default oldest).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetPostThreadV2Response> GetPostThreadV2Async(
        AtUri anchor,
        bool? above = null,
        int? below = null,
        int? branchingFactor = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("anchor", anchor)
            .Add("above", above)
            .Add("below", below)
            .Add("branchingFactor", branchingFactor)
            .Add("sort", sort);

        return _xrpc.QueryAsync<GetPostThreadV2Response>(
            "app.bsky.unspecced.getPostThreadV2", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the replies to an anchor post that <see cref="GetPostThreadV2Async"/> leaves out, such
    /// as those the threadgate hides. Call it when that response's
    /// <see cref="GetPostThreadV2Response.HasOtherReplies"/> is set.
    /// </summary>
    /// <param name="anchor">The AT-URI of the anchor post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetPostThreadOtherV2Response> GetPostThreadOtherV2Async(
        AtUri anchor, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("anchor", anchor);
        return _xrpc.QueryAsync<GetPostThreadOtherV2Response>(
            "app.bsky.unspecced.getPostThreadOtherV2", parameters, cancellationToken: cancellationToken);
    }
}
