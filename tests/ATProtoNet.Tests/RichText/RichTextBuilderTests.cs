using ATProtoNet.Lexicon.App.Bsky.RichText;

namespace ATProtoNet.Tests.RichText;

public class RichTextBuilderTests
{
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
            .Mention("alice.bsky.social", "did:plc:abc123")
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
            .Mention("alice", "did:plc:alice")
            .Text(" check ")
            .Link("this", "https://example.com")
            .Text(" ")
            .Tag("dev")
            .Build();

        Assert.Equal("Hello @alice check this #dev", text);
        Assert.NotNull(facets);
        Assert.Equal(3, facets.Count);

        // Mention: starts at 6 ("Hello "), "@alice" = 6 bytes
        Assert.Equal(6, facets[0].Index!.ByteStart);
        Assert.Equal(12, facets[0].Index.ByteEnd);

        // Link: starts after "Hello @alice check " = 19 bytes
        Assert.Equal(19, facets[1].Index!.ByteStart);
        Assert.Equal(23, facets[1].Index.ByteEnd); // "this" = 4 bytes

        // Tag: starts after "Hello @alice check this " = 24 bytes
        Assert.Equal(24, facets[2].Index!.ByteStart);
        Assert.Equal(28, facets[2].Index.ByteEnd); // "#dev" = 4 bytes
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
            .Mention("alice", "did:plc:alice")
            .Build();

        Assert.Equal("🦋 @alice", text);
        Assert.NotNull(facets);

        // 🦋 is 4 UTF-8 bytes + 1 space = 5 bytes start
        Assert.Equal(5, facets[0].Index!.ByteStart);
        Assert.Equal(11, facets[0].Index.ByteEnd); // "@alice" = 6 bytes, 5+6=11
    }

    [Fact]
    public void EmptyBuilder_BuildsEmptyText()
    {
        var (text, facets) = new RichTextBuilder().Build();

        Assert.Equal(string.Empty, text);
        Assert.Null(facets);
    }
}
