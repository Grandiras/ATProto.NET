namespace ATProtoNet.Tests.Lexicon.App.Bsky;

/// <summary>
/// Appview-shaped JSON for the app.bsky tests: the fields, nesting and <c>$type</c> placement
/// the Bluesky appview produces, with made-up identities.
/// </summary>
internal static class BskyFixtures
{
    public const string AliceDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    public const string BobDid = "did:plc:yk4dd2qkboz2yv6tpubpc6co";
    public const string PostCid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";
    public const string OtherCid = "bafyreihbiojjvlzlrn664jgdgql7cexcd62h5ltxuhjulvutjbmbt253h4";
    public const string PostUri = $"at://{AliceDid}/app.bsky.feed.post/3lwinfmsd2k2a";
    public const string OtherPostUri = $"at://{BobDid}/app.bsky.feed.post/3lwinfmsd2k2b";
    public const string ListUri = $"at://{AliceDid}/app.bsky.graph.list/3lwinfmsd2k2c";
    public const string ListItemUri = $"at://{AliceDid}/app.bsky.graph.listitem/3lwinfmsd2k2d";
    public const string StarterPackUri = $"at://{AliceDid}/app.bsky.graph.starterpack/3lwinfmsd2k2e";

    public static readonly string AliceBasicJson =
        $$$"""{"did":"{{{AliceDid}}}","handle":"alice.test","displayName":"Alice","avatar":"https://cdn.bsky.app/img/avatar/plain/{{{AliceDid}}}/{{{PostCid}}}@jpeg","associated":{"chat":{"allowIncoming":"following"},"activitySubscription":{"allowSubscriptions":"followers"}},"viewer":{"muted":false,"blockedBy":false},"labels":[],"createdAt":"2023-04-12T04:53:57.057Z"}""";

    public static readonly string BobProfileJson =
        $$"""{"did":"{{BobDid}}","handle":"bob.test","displayName":"Bob","description":"hi","indexedAt":"2024-01-01T00:00:00.000Z","viewer":{"muted":false,"blockedBy":false,"following":"at://{{AliceDid}}/app.bsky.graph.follow/3lwinfmsd2k2f"},"labels":[],"createdAt":"2023-05-01T10:00:00.000Z"}""";

    /// <summary>A post view, with <c>$type</c> last the way the appview writes it in unions.</summary>
    public static readonly string PostViewJson =
        $$"""{"uri":"{{PostUri}}","cid":"{{PostCid}}","author":{{AliceBasicJson}},"record":{"$type":"app.bsky.feed.post","createdAt":"2026-09-20T12:00:00.000Z","langs":["en"],"text":"hello world"},"bookmarkCount":3,"replyCount":1,"repostCount":0,"likeCount":7,"quoteCount":0,"indexedAt":"2026-09-20T12:00:01.123Z","viewer":{"bookmarked":true,"threadMuted":false,"embeddingDisabled":false},"labels":[],"$type":"app.bsky.feed.defs#postView"}""";

    public static readonly string ListViewJson =
        $$"""{"uri":"{{ListUri}}","cid":"{{PostCid}}","name":"Friends","purpose":"app.bsky.graph.defs#curatelist","listItemCount":2,"indexedAt":"2026-09-01T00:00:00.000Z","labels":[],"viewer":{"muted":false},"creator":{{BobProfileJson.Replace(BobDid, AliceDid, StringComparison.Ordinal).Replace("bob.test", "alice.test", StringComparison.Ordinal)}}}""";

    public static readonly string ListItemViewJson =
        $$"""{"uri":"{{ListItemUri}}","subject":{{BobProfileJson}}}""";

    public static readonly string StarterPackViewJson =
        $$"""{"uri":"{{StarterPackUri}}","cid":"{{OtherCid}}","record":{"$type":"app.bsky.graph.starterpack","createdAt":"2026-09-01T00:00:00.000Z","list":"{{ListUri}}","name":"Pack"},"creator":{{AliceBasicJson}},"list":{"uri":"{{ListUri}}","cid":"{{PostCid}}","name":"Pack","purpose":"app.bsky.graph.defs#referencelist","listItemCount":2},"joinedWeekCount":0,"joinedAllTimeCount":4,"labels":[],"indexedAt":"2026-09-01T00:00:00.000Z"}""";
}
