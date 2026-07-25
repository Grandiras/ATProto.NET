namespace ATProtoNet.LexiconGenerator.CodeGen;

/// <summary>
/// Maps well-known AT Protocol definitions onto the C# types that the ATProtoNet SDK
/// already ships, so a third-party Lexicon that references <c>com.atproto.*</c> or
/// <c>app.bsky.*</c> reuses the SDK model instead of generating a dangling type name
/// inside the consumer's own namespace.
/// </summary>
/// <remarks>
/// Keys are normalized refs — always <c>nsid#defName</c>, with <c>main</c> spelled out
/// (see <see cref="TypeMapper.NormalizeRef"/>). Only definitions that genuinely exist in
/// the SDK are listed; anything else under an SDK-owned authority is reported as
/// unresolved rather than guessed at, because a guessed name would not compile.
/// </remarks>
public static class SdkTypeMap
{
    private const string Models = "ATProtoNet.Models";
    private const string Lex = "ATProtoNet.Lexicon";

    private static readonly Dictionary<string, string> s_types = new(StringComparer.Ordinal)
    {
        // ── com.atproto ──────────────────────────────────────────
        ["com.atproto.repo.strongRef#main"] = $"{Models}.StrongRef",
        ["com.atproto.repo.defs#commitMeta"] = $"{Lex}.Com.AtProto.Repo.CommitMeta",
        ["com.atproto.label.defs#label"] = $"{Models}.Label",
        ["com.atproto.label.defs#selfLabels"] = $"{Lex}.App.Bsky.Feed.SelfLabels",
        ["com.atproto.label.defs#selfLabel"] = $"{Lex}.App.Bsky.Feed.SelfLabelValue",

        // ── app.bsky.richtext ────────────────────────────────────
        ["app.bsky.richtext.facet#main"] = $"{Lex}.App.Bsky.RichText.Facet",
        ["app.bsky.richtext.facet#byteSlice"] = $"{Lex}.App.Bsky.RichText.FacetIndex",
        ["app.bsky.richtext.facet#mention"] = $"{Lex}.App.Bsky.RichText.MentionFeature",
        ["app.bsky.richtext.facet#link"] = $"{Lex}.App.Bsky.RichText.LinkFeature",
        ["app.bsky.richtext.facet#tag"] = $"{Lex}.App.Bsky.RichText.TagFeature",

        // ── app.bsky.embed ───────────────────────────────────────
        ["app.bsky.embed.defs#aspectRatio"] = $"{Lex}.App.Bsky.Embed.AspectRatio",
        ["app.bsky.embed.images#main"] = $"{Lex}.App.Bsky.Embed.ImagesEmbed",
        ["app.bsky.embed.images#image"] = $"{Lex}.App.Bsky.Embed.EmbedImage",
        ["app.bsky.embed.images#view"] = $"{Lex}.App.Bsky.Embed.ImagesView",
        ["app.bsky.embed.images#viewImage"] = $"{Lex}.App.Bsky.Embed.ImageViewItem",
        ["app.bsky.embed.external#main"] = $"{Lex}.App.Bsky.Embed.ExternalEmbed",
        ["app.bsky.embed.external#external"] = $"{Lex}.App.Bsky.Embed.ExternalInfo",
        ["app.bsky.embed.external#view"] = $"{Lex}.App.Bsky.Embed.ExternalView",
        ["app.bsky.embed.external#viewExternal"] = $"{Lex}.App.Bsky.Embed.ExternalViewInfo",
        ["app.bsky.embed.record#main"] = $"{Lex}.App.Bsky.Embed.RecordEmbed",
        ["app.bsky.embed.record#view"] = $"{Lex}.App.Bsky.Embed.RecordView",
        ["app.bsky.embed.recordWithMedia#main"] = $"{Lex}.App.Bsky.Embed.RecordWithMediaEmbed",
        ["app.bsky.embed.recordWithMedia#view"] = $"{Lex}.App.Bsky.Embed.RecordWithMediaView",
        ["app.bsky.embed.video#main"] = $"{Lex}.App.Bsky.Embed.VideoEmbed",
        ["app.bsky.embed.video#caption"] = $"{Lex}.App.Bsky.Embed.VideoCaption",
        ["app.bsky.embed.video#view"] = $"{Lex}.App.Bsky.Embed.VideoView",

        // ── app.bsky.actor ───────────────────────────────────────
        ["app.bsky.actor.defs#profileView"] = $"{Lex}.App.Bsky.Actor.ProfileView",
        ["app.bsky.actor.defs#profileViewBasic"] = $"{Lex}.App.Bsky.Actor.ProfileViewBasic",
        ["app.bsky.actor.defs#profileViewDetailed"] = $"{Lex}.App.Bsky.Actor.ProfileViewDetailed",
        ["app.bsky.actor.defs#viewerState"] = $"{Lex}.App.Bsky.Actor.ViewerState",
        ["app.bsky.actor.defs#knownFollowers"] = $"{Lex}.App.Bsky.Actor.KnownFollowers",

        // ── app.bsky.feed ────────────────────────────────────────
        ["app.bsky.feed.defs#postView"] = $"{Lex}.App.Bsky.Feed.PostView",
        ["app.bsky.feed.defs#viewerState"] = $"{Lex}.App.Bsky.Feed.PostViewerState",
        ["app.bsky.feed.defs#feedViewPost"] = $"{Lex}.App.Bsky.Feed.FeedViewPost",
        ["app.bsky.feed.defs#replyRef"] = $"{Lex}.App.Bsky.Feed.FeedReplyRef",
        ["app.bsky.feed.defs#threadViewPost"] = $"{Lex}.App.Bsky.Feed.ThreadViewPost",
        ["app.bsky.feed.defs#notFoundPost"] = $"{Lex}.App.Bsky.Feed.NotFoundPost",
        ["app.bsky.feed.defs#blockedPost"] = $"{Lex}.App.Bsky.Feed.BlockedPost",
        ["app.bsky.feed.defs#generatorView"] = $"{Lex}.App.Bsky.Feed.GeneratorView",
        ["app.bsky.feed.post#replyRef"] = $"{Lex}.App.Bsky.Feed.ReplyRef",

        // ── app.bsky.graph ───────────────────────────────────────
        ["app.bsky.graph.defs#listView"] = $"{Lex}.App.Bsky.Graph.ListView",
        ["app.bsky.graph.defs#listViewBasic"] = $"{Lex}.App.Bsky.Graph.ListViewBasic",
        ["app.bsky.graph.defs#listItemView"] = $"{Lex}.App.Bsky.Graph.ListItemView",
        ["app.bsky.graph.defs#listViewerState"] = $"{Lex}.App.Bsky.Graph.ListViewerState",
    };

    /// <summary>
    /// NSID authorities owned by the SDK. Refs under these prefixes are never generated
    /// into the consumer's namespace.
    /// </summary>
    private static readonly string[] s_sdkAuthorities =
    [
        "com.atproto.",
        "app.bsky.",
        "chat.bsky.",
        "tools.ozone.",
        "site.standard.",
    ];

    /// <summary>Every known ref → SDK type mapping.</summary>
    public static IReadOnlyDictionary<string, string> Entries => s_types;

    /// <summary>
    /// Resolves a normalized ref (<c>nsid#defName</c>) to a fully-qualified SDK type name.
    /// </summary>
    public static bool TryResolve(string normalizedRef, out string csharpType)
        => s_types.TryGetValue(normalizedRef, out csharpType!);

    /// <summary>
    /// True when the NSID belongs to an authority the SDK ships models for.
    /// </summary>
    public static bool IsSdkAuthority(string nsid)
        => s_sdkAuthorities.Any(prefix => nsid.StartsWith(prefix, StringComparison.Ordinal));
}
