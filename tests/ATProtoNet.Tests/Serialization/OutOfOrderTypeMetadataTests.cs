using System.Text.Json;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// Regression for #50: the Bluesky appview serializes polymorphic objects with "$type"
/// AFTER other properties (observed: embed views come as {"external": …, "$type": …}),
/// and real-world record writers do the same inside record unions. Deserialization must
/// tolerate the discriminator anywhere in the object.
/// </summary>
public class OutOfOrderTypeMetadataTests
{
    // Shape captured from public.api.bsky.app app.bsky.feed.getPosts — note "$type" LAST.
    private const string PostViewJson =
        """
        {
          "uri": "at://did:plc:tkjlutt3jh2aqkkxliitff6k/app.bsky.feed.post/3m3micwvzml2b",
          "cid": "bafyreibg74yyklc26wkjybt2nup2wlvghyfabj7gaft3tpoxocwa5cgd64",
          "author": {"did": "did:plc:tkjlutt3jh2aqkkxliitff6k", "handle": "forum.recipe.exchange"},
          "record": {"text": "shared a recipe"},
          "embed": {
            "external": {
              "uri": "https://recipe.exchange/recipes/01K7Z0S209TEPAGZFSCD4HH8VJ",
              "title": "A soup",
              "description": "Carrot soup"
            },
            "$type": "app.bsky.embed.external#view"
          },
          "replyCount": 1,
          "likeCount": 2,
          "indexedAt": "2026-06-01T00:00:00.000Z"
        }
        """;

    [Fact]
    public void Deserialize_EmbedViewWithTrailingTypeDiscriminator_ResolvesVariant()
    {
        var post = JsonSerializer.Deserialize<PostView>(PostViewJson, AtProtoJsonDefaults.Options);

        Assert.NotNull(post);
        var external = Assert.IsType<ExternalView>(post!.Embed);
        Assert.Equal("https://recipe.exchange/recipes/01K7Z0S209TEPAGZFSCD4HH8VJ", external.External.Uri);
        Assert.Equal(2, post.LikeCount);
    }

    [Fact]
    public void Deserialize_EmbedViewWithLeadingTypeDiscriminator_StillWorks()
    {
        var json = PostViewJson.Replace(
            """
            "external": {
              "uri": "https://recipe.exchange/recipes/01K7Z0S209TEPAGZFSCD4HH8VJ",
              "title": "A soup",
              "description": "Carrot soup"
            },
            "$type": "app.bsky.embed.external#view"
            """,
            """
            "$type": "app.bsky.embed.external#view",
            "external": {
              "uri": "https://recipe.exchange/recipes/01K7Z0S209TEPAGZFSCD4HH8VJ",
              "title": "A soup",
              "description": "Carrot soup"
            }
            """);

        var post = JsonSerializer.Deserialize<PostView>(json, AtProtoJsonDefaults.Options);

        Assert.IsType<ExternalView>(post!.Embed);
    }

    [Fact]
    public void RegistryOptions_AlsoTolerateTrailingDiscriminator()
    {
        var options = LexiconTypeRegistry.Instance.CreateOptions();

        var post = JsonSerializer.Deserialize<PostView>(PostViewJson, options);

        Assert.IsType<ExternalView>(post!.Embed);
    }
}
