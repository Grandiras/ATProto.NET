using System.Text;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.RichText;

/// <summary>
/// Utility for building rich text with facets from a fluent API.
/// Handles UTF-8 byte offset computation automatically.
/// </summary>
public sealed class RichTextBuilder
{
    private readonly StringBuilder _text = new();
    private readonly List<Facet> _facets = [];
    private int _byteOffset;

    /// <summary>
    /// Append plain text.
    /// </summary>
    public RichTextBuilder Text(string text)
    {
        _text.Append(text);
        _byteOffset += Encoding.UTF8.GetByteCount(text);
        return this;
    }

    /// <summary>
    /// Append a mention. Display text is "@handle".
    /// </summary>
    /// <param name="handle">The handle to display (without @).</param>
    /// <param name="did">The DID of the mentioned user.</param>
    public RichTextBuilder Mention(string handle, string did)
    {
        var displayText = $"@{handle}";
        var byteCount = Encoding.UTF8.GetByteCount(displayText);

        _facets.Add(new Facet
        {
            Index = new FacetIndex
            {
                ByteStart = _byteOffset,
                ByteEnd = _byteOffset + byteCount,
            },
            Features = [new MentionFeature { Did = did }],
        });

        _text.Append(displayText);
        _byteOffset += byteCount;
        return this;
    }

    /// <summary>
    /// Append a hyperlink.
    /// </summary>
    /// <param name="displayText">The text displayed for the link.</param>
    /// <param name="uri">The URL to link to.</param>
    public RichTextBuilder Link(string displayText, string uri)
    {
        var byteCount = Encoding.UTF8.GetByteCount(displayText);

        _facets.Add(new Facet
        {
            Index = new FacetIndex
            {
                ByteStart = _byteOffset,
                ByteEnd = _byteOffset + byteCount,
            },
            Features = [new LinkFeature { Uri = uri }],
        });

        _text.Append(displayText);
        _byteOffset += byteCount;
        return this;
    }

    /// <summary>
    /// Append a hashtag. Display text is "#tag".
    /// </summary>
    /// <param name="tag">The tag text (without #).</param>
    public RichTextBuilder Tag(string tag)
    {
        var displayText = $"#{tag}";
        var byteCount = Encoding.UTF8.GetByteCount(displayText);

        _facets.Add(new Facet
        {
            Index = new FacetIndex
            {
                ByteStart = _byteOffset,
                ByteEnd = _byteOffset + byteCount,
            },
            Features = [new TagFeature { Tag = tag }],
        });

        _text.Append(displayText);
        _byteOffset += byteCount;
        return this;
    }

    /// <summary>
    /// Append a newline.
    /// </summary>
    public RichTextBuilder NewLine()
    {
        return Text("\n");
    }

    /// <summary>
    /// Build the rich text result.
    /// </summary>
    public (string Text, List<Facet>? Facets) Build()
    {
        return (_text.ToString(), _facets.Count > 0 ? _facets : null);
    }
}
