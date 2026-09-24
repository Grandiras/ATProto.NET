using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// <c>app.bsky.embed.gallery</c> (upstream since 2026-06-03). Before it was registered, one gallery
/// post failed the whole timeline, feed or thread page it appeared in.
/// </summary>
public class GalleryEmbedTests
{
    // The appview's output for the upstream test fixtures "embeds gallery" and "embeds gallery
    // with record" (packages/bsky/tests/views/__snapshots__/posts.test.ts.snap), with the
    // snapshot placeholders replaced by concrete identifiers, as a timeline page.
    private const string TimelineJson =
        """
        {"cursor":"1717400000000::bafyreib","feed":[
        {"post":{"author":{"associated":{"activitySubscription":{"allowSubscriptions":"followers"}},"avatar":"https://cdn.bsky.app/img/avatar/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreibvdx7k5ep3gr2lkf2r6dvd3hlrlv2ndz6ewqzmuo7igw3x5gmgue","createdAt":"2023-05-01T00:00:00.000Z","did":"did:plc:ragtjsm2j2vknwkz3zp4oxrd","displayName":"ali","handle":"alice.test","labels":[]},
         "bookmarkCount":0,"cid":"bafyreigdwdwrrkpjhp5vwmrl5gvm4wasvk7lkeo3m4v37qi5wicrp2aoky",
         "embed":{"$type":"app.bsky.embed.gallery#view","items":[
           {"$type":"app.bsky.embed.gallery#viewImage","alt":"landscape","aspectRatio":{"height":3,"width":4},"fullsize":"https://cdn.bsky.app/img/feed_fullsize/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe@jpeg","thumbnail":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe@jpeg"},
           {"$type":"app.bsky.embed.gallery#viewImage","alt":"portrait","aspectRatio":{"height":4,"width":3},"fullsize":"https://cdn.bsky.app/img/feed_fullsize/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreibvdx7k5ep3gr2lkf2r6dvd3hlrlv2ndz6ewqzmuo7igw3x5gmgue@jpeg","thumbnail":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreibvdx7k5ep3gr2lkf2r6dvd3hlrlv2ndz6ewqzmuo7igw3x5gmgue@jpeg"}]},
         "indexedAt":"2026-06-03T00:00:00.000Z","labels":[],"likeCount":0,"quoteCount":0,
         "record":{"$type":"app.bsky.feed.post","createdAt":"2026-06-03T00:00:00.000Z","embed":{"$type":"app.bsky.embed.gallery","items":[
           {"$type":"app.bsky.embed.gallery#image","alt":"landscape","aspectRatio":{"height":3,"width":4},"image":{"$type":"blob","mimeType":"image/jpeg","ref":{"$link":"bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe"},"size":4114}},
           {"$type":"app.bsky.embed.gallery#image","alt":"portrait","aspectRatio":{"height":4,"width":3},"image":{"$type":"blob","mimeType":"image/jpeg","ref":{"$link":"bafkreibvdx7k5ep3gr2lkf2r6dvd3hlrlv2ndz6ewqzmuo7igw3x5gmgue"},"size":3976}}]},"text":"gallery"},
         "replyCount":0,"repostCount":0,"uri":"at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/3lq5a2kxzqc2a"}},
        {"post":{"author":{"did":"did:plc:ragtjsm2j2vknwkz3zp4oxrd","displayName":"ali","handle":"alice.test","labels":[]},
         "bookmarkCount":0,"cid":"bafyreibwrrc2zwzyp2hjl5wbytwamrmhjd3zklqcbnvh4rmnvlghubkgba",
         "embed":{"$type":"app.bsky.embed.recordWithMedia#view",
           "media":{"$type":"app.bsky.embed.gallery#view","items":[{"$type":"app.bsky.embed.gallery#viewImage","alt":"landscape","aspectRatio":{"height":3,"width":4},"fullsize":"https://cdn.bsky.app/img/feed_fullsize/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe@jpeg","thumbnail":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:ragtjsm2j2vknwkz3zp4oxrd/bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe@jpeg"}]},
           "record":{"record":{"$type":"app.bsky.embed.record#viewRecord","author":{"did":"did:plc:ragtjsm2j2vknwkz3zp4oxrd","handle":"alice.test"},"cid":"bafyreif3yx3xdk3ffbynxt5tzf2vzyh6pxqvnmgg6yl2bqsfyt3ogxhbpy","embeds":[],"indexedAt":"2026-06-02T00:00:00.000Z","labels":[],"likeCount":0,"quoteCount":1,"replyCount":0,"repostCount":0,"uri":"at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/3lq59zzzzzc2a","value":{"$type":"app.bsky.feed.post","createdAt":"2026-06-02T00:00:00.000Z","text":"embedded"}}}},
         "indexedAt":"2026-06-03T00:00:00.000Z","labels":[],"likeCount":0,"quoteCount":0,
         "record":{"$type":"app.bsky.feed.post","createdAt":"2026-06-03T00:00:00.000Z","embed":{"$type":"app.bsky.embed.recordWithMedia",
           "media":{"$type":"app.bsky.embed.gallery","items":[{"$type":"app.bsky.embed.gallery#image","alt":"landscape","aspectRatio":{"height":3,"width":4},"image":{"$type":"blob","mimeType":"image/jpeg","ref":{"$link":"bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe"},"size":4114}}]},
           "record":{"record":{"cid":"bafyreif3yx3xdk3ffbynxt5tzf2vzyh6pxqvnmgg6yl2bqsfyt3ogxhbpy","uri":"at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/3lq59zzzzzc2a"}}},"text":"gallery + record"},
         "replyCount":0,"repostCount":0,"uri":"at://did:plc:ragtjsm2j2vknwkz3zp4oxrd/app.bsky.feed.post/3lq5a2kxzqc2b"}}]}
        """;

    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    [Fact]
    public void Deserialize_TimelinePageWithGalleryPosts_Succeeds()
    {
        var page = JsonSerializer.Deserialize<FeedResponse>(TimelineJson, Options)!;

        Assert.Equal(2, page.Feed.Count);

        var gallery = Assert.IsType<GalleryView>(page.Feed[0].Post.Embed);
        Assert.Collection(gallery.Items,
            item =>
            {
                var image = Assert.IsType<GalleryViewImage>(item);
                Assert.Equal("landscape", image.Alt);
                Assert.Equal((4, 3), (image.AspectRatio.Width, image.AspectRatio.Height));
                Assert.EndsWith("@jpeg", image.Thumbnail);
            },
            item => Assert.Equal("portrait", Assert.IsType<GalleryViewImage>(item).Alt));

        var withMedia = Assert.IsType<RecordWithMediaView>(page.Feed[1].Post.Embed);
        Assert.Single(Assert.IsType<GalleryView>(withMedia.Media).Items);
    }

    [Fact]
    public void Deserialize_GalleryPostRecord_ReadsItems()
    {
        var page = JsonSerializer.Deserialize<FeedResponse>(TimelineJson, Options)!;

        var record = page.Feed[0].Post.Record.Deserialize<PostRecord>(Options)!;

        var gallery = Assert.IsType<GalleryEmbed>(record.Embed);
        var first = Assert.IsType<GalleryImage>(gallery.Items[0]);
        Assert.Equal("landscape", first.Alt);
        Assert.Equal("image/jpeg", first.Image.MimeType);
        Assert.Equal(4114, first.Image.Size);

        var quoting = page.Feed[1].Post.Record.Deserialize<PostRecord>(Options)!;
        var withMedia = Assert.IsType<RecordWithMediaEmbed>(quoting.Embed);
        Assert.IsType<GalleryEmbed>(withMedia.Media);
    }

    [Fact]
    public void Serialize_GalleryPostRecord_RoundTripsTheRecordAsWritten()
    {
        var page = JsonSerializer.Deserialize<FeedResponse>(TimelineJson, Options)!;
        var original = page.Feed[0].Post.Record;

        var record = original.Deserialize<PostRecord>(Options)!;
        var written = JsonSerializer.SerializeToElement(record, Options);

        Assert.True(JsonElement.DeepEquals(original, written),
            $"expected {original.GetRawText()}\nactual   {written.GetRawText()}");
    }

    [Fact]
    public void Serialize_NewGalleryEmbed_WritesLexiconShape()
    {
        var embed = new GalleryEmbed
        {
            Items =
            [
                new GalleryImage
                {
                    Image = new() { Ref = new() { Link = Cid.Parse("bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe") }, MimeType = "image/png", Size = 10 },
                    Alt = "a chart",
                    AspectRatio = new() { Width = 16, Height = 9 },
                },
            ],
        };

        var json = JsonSerializer.Serialize<EmbedBase>(embed, Options);

        Assert.Equal(
            """{"$type":"app.bsky.embed.gallery","items":[{"$type":"app.bsky.embed.gallery#image","image":{"$type":"blob","ref":{"$link":"bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe"},"mimeType":"image/png","size":10},"alt":"a chart","aspectRatio":{"width":16,"height":9}}]}""",
            json);
    }
}
