using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.RichText;

namespace ATProtoNet.Tests.RichText;

public class RichTextBuilderTests
{
    private static readonly Handle Alice = Handle.Parse("alice.test");
    private static readonly Did AliceDid = Did.Parse("did:plc:alice");

    [Fact]
    public void Text_BuildsPlainText()
    {
        var (text, facets) = new RichTextBuilder()
            .Text("Hello, world!")
            .Build();

        Assert.Equal("Hello, world!", text);
        Assert.Null(facets);
    }

    [Fact]
    public void Mention_CreatesFacetWithCorrectOffsets()
    {
        var (text, facets) = new RichTextBuilder()
            .Text("Hello ")
            .Mention(Handle.Parse("alice.bsky.social"), Did.Parse("did:plc:abc123"))
            .Build();

        Assert.Equal("Hello @alice.bsky.social", text);
        Assert.NotNull(facets);
        Assert.Single(facets);

        var facet = facets[0];
        Assert.Equal(6, facet.Index!.ByteStart);  // "Hello " = 6 bytes
        Assert.Equal(24, facet.Index.ByteEnd);     // "@alice.bsky.social" = 18 bytes, 6+18=24

        var feature = Assert.Single(facet.Features!);
        var mention = Assert.IsType<MentionFeature>(feature);
        Assert.Equal("did:plc:abc123", mention.Did);
    }

    [Fact]
    public void Link_CreatesFacetWithCorrectOffsets()
    {
        var (text, facets) = new RichTextBuilder()
            .Text("Check out ")
            .Link("my site", "https://example.com")
            .Build();

        Assert.Equal("Check out my site", text);
        Assert.NotNull(facets);
        Assert.Single(facets);

        var facet = facets[0];
        Assert.Equal(10, facet.Index!.ByteStart); // "Check out " = 10 bytes
        Assert.Equal(17, facet.Index.ByteEnd);    // "my site" = 7 bytes, 10+7=17

        var feature = Assert.Single(facet.Features!);
        var link = Assert.IsType<LinkFeature>(feature);
        Assert.Equal("https://example.com", link.Uri);
    }

    [Fact]
    public void Tag_CreatesFacetWithCorrectOffsets()
    {
        var (text, facets) = new RichTextBuilder()
            .Tag("atproto")
            .Build();

        Assert.Equal("#atproto", text);
        Assert.NotNull(facets);
        Assert.Single(facets);

        var facet = facets[0];
        Assert.Equal(0, facet.Index!.ByteStart);
        Assert.Equal(8, facet.Index.ByteEnd);  // "#atproto" = 8 bytes

        var feature = Assert.Single(facet.Features!);
        var tag = Assert.IsType<TagFeature>(feature);
        Assert.Equal("atproto", tag.Tag);
    }

    [Fact]
    public void MultipleFacets_CorrectOffsets()
    {
        var (text, facets) = new RichTextBuilder()
            .Text("Hello ")
            .Mention(Alice, AliceDid)
            .Text(" check ")
            .Link("this", "https://example.com")
            .Text(" ")
            .Tag("dev")
            .Build();

        Assert.Equal("Hello @alice.test check this #dev", text);
        Assert.NotNull(facets);
        Assert.Equal(3, facets.Count);

        // Mention: starts at 6 ("Hello "), "@alice.test" = 11 bytes
        Assert.Equal(6, facets[0].Index!.ByteStart);
        Assert.Equal(17, facets[0].Index.ByteEnd);

        // Link: starts after "Hello @alice.test check " = 24 bytes
        Assert.Equal(24, facets[1].Index!.ByteStart);
        Assert.Equal(28, facets[1].Index.ByteEnd); // "this" = 4 bytes

        // Tag: starts after "Hello @alice.test check this " = 29 bytes
        Assert.Equal(29, facets[2].Index!.ByteStart);
        Assert.Equal(33, facets[2].Index.ByteEnd); // "#dev" = 4 bytes
    }

    [Fact]
    public void NewLine_InsertsNewlineCharacter()
    {
        var (text, facets) = new RichTextBuilder()
            .Text("Line 1")
            .NewLine()
            .Text("Line 2")
            .Build();

        Assert.Equal("Line 1\nLine 2", text);
        Assert.Null(facets);
    }

    [Fact]
    public void Unicode_CalculatesByteOffsetsCorrectly()
    {
        // Emoji and multi-byte characters
        var (text, facets) = new RichTextBuilder()
            .Text("🦋 ")       // U+1F98B = 4 bytes + space = 5 bytes
            .Mention(Alice, AliceDid)
            .Build();

        Assert.Equal("🦋 @alice.test", text);
        Assert.NotNull(facets);

        // 🦋 is 4 UTF-8 bytes + 1 space = 5 bytes start
        Assert.Equal(5, facets[0].Index!.ByteStart);
        Assert.Equal(16, facets[0].Index.ByteEnd); // "@alice.test" = 11 bytes, 5+11=16
    }

    [Fact]
    public void Build_ThenAppend_LeavesTheBuiltFacetsUnchanged()
    {
        var builder = new RichTextBuilder().Mention(Alice, AliceDid);
        var (_, facets) = builder.Build();

        builder.Text(" ").Tag("later");

        Assert.Single(facets!);
        Assert.Equal(2, builder.Build().Facets!.Count);
    }

    [Fact]
    public void EmptyBuilder_BuildsEmptyText()
    {
        var (text, facets) = new RichTextBuilder().Build();

        Assert.Equal(string.Empty, text);
        Assert.Null(facets);
    }
}
