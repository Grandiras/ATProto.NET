using ATProtoNet.Identity;

namespace ATProtoNet.Blazor;

/// <summary>
/// What a <c>FeedView</c> shows: the signed-in user's timeline, an account's posts, a feed
/// generator's feed, or a list's feed.
/// </summary>
/// <example>
/// <code>
/// &lt;FeedView FeedSource="FeedSource.Timeline" /&gt;
/// &lt;FeedView FeedSource="@FeedSource.Author(AtIdentifier.Parse(&quot;alice.bsky.social&quot;))" /&gt;
/// &lt;FeedView FeedSource="@FeedSource.Feed(AtUri.Parse(&quot;at://did:plc:abc/app.bsky.feed.generator/whats-hot&quot;))" /&gt;
/// </code>
/// </example>
public abstract record FeedSource
{
    private FeedSource()
    {
    }

    /// <summary>The signed-in user's home timeline (<c>app.bsky.feed.getTimeline</c>).</summary>
    public static FeedSource Timeline { get; } = new TimelineSource();

    /// <summary>An account's posts and reposts (<c>app.bsky.feed.getAuthorFeed</c>).</summary>
    /// <param name="actor">The account, by handle or DID.</param>
    /// <returns>The source.</returns>
    public static FeedSource Author(AtIdentifier actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new AuthorSource(actor);
    }

    /// <summary>A feed generator's feed (<c>app.bsky.feed.getFeed</c>).</summary>
    /// <param name="generator">The AT URI of the <c>app.bsky.feed.generator</c> record.</param>
    /// <returns>The source.</returns>
    public static FeedSource Feed(AtUri generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return new GeneratorSource(generator);
    }

    /// <summary>The posts of a list's members (<c>app.bsky.feed.getListFeed</c>).</summary>
    /// <param name="list">The AT URI of the <c>app.bsky.graph.list</c> record.</param>
    /// <returns>The source.</returns>
    public static FeedSource List(AtUri list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new ListSource(list);
    }

    /// <summary>The signed-in user's home timeline.</summary>
    public sealed record TimelineSource : FeedSource;

    /// <summary>An account's posts and reposts.</summary>
    /// <param name="Actor">The account.</param>
    public sealed record AuthorSource(AtIdentifier Actor) : FeedSource;

    /// <summary>A feed generator's feed.</summary>
    /// <param name="GeneratorUri">The AT URI of the feed generator record.</param>
    public sealed record GeneratorSource(AtUri GeneratorUri) : FeedSource;

    /// <summary>The posts of a list's members.</summary>
    /// <param name="ListUri">The AT URI of the list record.</param>
    public sealed record ListSource(AtUri ListUri) : FeedSource;
}
