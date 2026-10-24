using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

/// <summary>A parsed Lexicon schema document: immutable, typed, and keeping its exact source bytes.</summary>
/// <remarks>See <see href="https://atproto.com/specs/lexicon"/>.</remarks>
sealed partial class LexiconDocument
{
    private LexiconDocument(Nsid id, int? revision, string? description, IReadOnlyDictionary<string, LexUserType> defs, byte[] source, JsonElement raw)
    {
        Id = id;
        Revision = revision;
        Description = description;
        Defs = defs;
        SourceUtf8 = source;
        Raw = raw;
    }

    /// <summary>The Lexicon language version; always 1.</summary>
    public int LexiconVersion => 1;

    /// <summary>The document's NSID.</summary>
    public Nsid Id { get; }

    /// <summary>The schema revision, if declared.</summary>
    public int? Revision { get; }

    /// <summary>The document's <c>description</c>.</summary>
    public string? Description { get; }

    /// <summary>The definitions by name, in declaration order: <c>main</c> and named ones.</summary>
    public IReadOnlyDictionary<string, LexUserType> Defs { get; }

    /// <summary>The exact bytes parsed, for publishing without re-serialising.</summary>
    public ReadOnlyMemory<byte> SourceUtf8 { get; }

    /// <summary>The document's root object.</summary>
    public JsonElement Raw { get; }

    /// <summary>The <c>main</c> definition, if any.</summary>
    public LexUserType? Main => Defs.TryGetValue("main", out var main) ? main : null;

    /// <summary>Parses a Lexicon document.</summary>
    /// <param name="utf8Json">The document as UTF-8 JSON.</param>
    /// <returns>The document.</returns>
    /// <exception cref="FormatException">The document is invalid; the message names the first violation.</exception>
    public static LexiconDocument Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (TryParse(utf8Json, out var document, out var diagnostics)) return document;
        foreach (var diagnostic in diagnostics)
            if (diagnostic.Severity == LexiconSeverity.Error) throw new FormatException(diagnostic.ToString());
        throw new FormatException("The Lexicon document is invalid.");
    }

    /// <summary>Parses a Lexicon document, collecting every violation and warning.</summary>
    /// <param name="utf8Json">The document as UTF-8 JSON.</param>
    /// <param name="document">The document, when it has no errors.</param>
    /// <param name="diagnostics">Every finding, warnings included.</param>
    /// <returns>Whether the document has no errors.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Json, [NotNullWhen(true)] out LexiconDocument? document, out IReadOnlyList<LexiconDiagnostic> diagnostics)
    {
        document = null;
        var parser = new LexiconParser();
        diagnostics = parser.Diagnostics;
        if (utf8Json.Length > LexiconLimits.MaxLexiconDocumentBytes)
        {
            parser.Error(LexiconRules.DocumentShape, $"The document is larger than {LexiconLimits.MaxLexiconDocumentBytes} bytes.", "");
            return false;
        }
        var source = utf8Json.ToArray();
        JsonElement root;
        try
        {
            using var json = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = 256 });
            root = json.RootElement.Clone();
        }
        catch (JsonException e)
        {
            parser.Error(LexiconRules.DocumentShape, "The document is not valid JSON: " + e.Message, "");
            return false;
        }
        var parsed = parser.Document(root);
        if (parsed is null || parser.HasErrors) return false;
        document = new(parsed.Value.Id, parsed.Value.Revision, parsed.Value.Description, parsed.Value.Defs, source, root);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Id.ToString();
}

/// <summary>The fixed limits of Lexicon parsing and validation.</summary>
static partial class LexiconLimits
{
    /// <summary>The largest document parsed, in bytes.</summary>
    public const int MaxLexiconDocumentBytes = 1024 * 1024;

    /// <summary>The most definitions one document may have.</summary>
    public const int MaxDefinitionsPerDocument = 1024;

    /// <summary>The deepest inline nesting of schemas within one definition.</summary>
    public const int MaxSchemaNestingDepth = 32;

    /// <summary>The deepest data validated (the DRISL depth limit).</summary>
    public const int MaxValidationDepth = 64;

    /// <summary>The most violations collected before validation stops.</summary>
    public const int MaxValidationErrors = 100;
}
