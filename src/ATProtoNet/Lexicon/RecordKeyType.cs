using System;
using System.Diagnostics.CodeAnalysis;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

/// <summary>The record key type of a <c>record</c> Lexicon: <c>tid</c>, <c>nsid</c>, <c>any</c> or <c>literal:&lt;value&gt;</c>.</summary>
/// <remarks>See <see href="https://atproto.com/specs/record-key#record-key-type"/>.</remarks>
sealed partial record RecordKeyType
{
    private RecordKeyType(string text, string? literal)
    {
        Text = text;
        Literal = literal;
    }

    /// <summary>Keys are TIDs.</summary>
    public static RecordKeyType Tid { get; } = new("tid", null);

    /// <summary>Keys are NSIDs.</summary>
    public static RecordKeyType Nsid { get; } = new("nsid", null);

    /// <summary>Any valid record key.</summary>
    public static RecordKeyType Any { get; } = new("any", null);

    /// <summary>The Lexicon text: <c>tid</c>, <c>nsid</c>, <c>any</c> or <c>literal:&lt;value&gt;</c>.</summary>
    public string Text { get; }

    /// <summary>The only allowed key of a <c>literal:</c> type; <see langword="null"/> otherwise.</summary>
    public string? Literal { get; }

    /// <summary>The type that allows only <paramref name="value"/>, such as <c>self</c>.</summary>
    /// <param name="value">The key.</param>
    /// <returns>The type.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public static RecordKeyType ForLiteral(string value) =>
        string.IsNullOrEmpty(value) ? throw new ArgumentException("A literal record key is not empty.", nameof(value)) : new("literal:" + value, value);

    /// <summary>Parses the Lexicon text of a key type.</summary>
    /// <param name="text">The text.</param>
    /// <param name="result">The type.</param>
    /// <returns>Whether <paramref name="text"/> is a key type.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out RecordKeyType? result)
    {
        result = text switch
        {
            "tid" => Tid,
            "nsid" => Nsid,
            "any" => Any,
            _ when text is not null && text.StartsWith("literal:", StringComparison.Ordinal) && text.Length > 8 => new(text, text.Substring(8)),
            _ => null,
        };
        return result is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}
