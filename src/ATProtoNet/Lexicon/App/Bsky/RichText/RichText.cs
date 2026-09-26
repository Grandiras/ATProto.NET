namespace ATProtoNet.Lexicon.App.Bsky.RichText;

/// <summary>
/// Post text together with its facets: the mentions, links and hashtags, located by UTF-8 byte
/// offsets into <see cref="Text"/>.
/// </summary>
/// <remarks>
/// A plain <see cref="string"/> converts to rich text without facets, so
/// <c>client.Bsky.PostAsync("hello")</c> works as is. <see cref="RichTextBuilder.Build"/> produces
/// one with the offsets computed. <c>with { Text = … }</c> keeps the facets as they are, so change
/// both together.
/// </remarks>
/// <param name="Text">The text.</param>
/// <param name="Facets">The facets; empty when the text has none.</param>
public sealed record RichText(string Text, IReadOnlyList<Facet> Facets)
{
    /// <summary>The text.</summary>
    public string Text { get; init; } = Text ?? throw new ArgumentNullException(nameof(Text));

    /// <summary>The facets; empty when the text has none.</summary>
    public IReadOnlyList<Facet> Facets { get; init; } = Facets ?? throw new ArgumentNullException(nameof(Facets));

    /// <summary>Creates rich text without facets.</summary>
    /// <param name="text">The text.</param>
    public RichText(string text)
        : this(text, [])
    {
    }

    /// <summary>Converts plain text to rich text without facets.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator RichText(string text) => new(text);

    /// <summary>The text, without its facets.</summary>
    public override string ToString() => Text;
}
