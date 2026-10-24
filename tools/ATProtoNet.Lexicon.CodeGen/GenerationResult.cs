using System.Collections.Generic;

namespace ATProtoNet.Lexicon.CodeGen;

/// <summary>How serious a <see cref="GenerationDiagnostic"/> is.</summary>
public enum GenerationSeverity
{
    /// <summary>Worth knowing; not a problem.</summary>
    Info,

    /// <summary>The output works but should change, usually through a <c>renames</c> entry.</summary>
    Warning,

    /// <summary>Nothing is generated.</summary>
    Error,
}

/// <summary>A finding of the generator: <c>ATLEX0xx</c> rules, plus the Lexicon rules (<c>LXL0xx</c>) of its input.</summary>
/// <param name="Id">The rule ID.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="File">The Lexicon file, when known.</param>
/// <param name="Pointer">The JSON pointer or definition path within it.</param>
public sealed record GenerationDiagnostic(string Id, GenerationSeverity Severity, string Message, string? File, string? Pointer)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{File}{(string.IsNullOrEmpty(Pointer) ? "" : " " + Pointer)}: {Severity.ToString().ToLowerInvariant()} {Id}: {Message}";
}

/// <summary>A generated C# file.</summary>
/// <param name="Path">The path relative to the output directory, with <c>/</c> separators.</param>
/// <param name="Text">The file's text.</param>
public sealed record GeneratedFile(string Path, string Text);

/// <summary>A Lexicon document to generate from.</summary>
/// <param name="Path">Where it came from, for diagnostics.</param>
/// <param name="Utf8Json">The document's bytes.</param>
/// <param name="Pin">The manifest pin written into the file header (source and CID), if any.</param>
public sealed record LexiconSource(string Path, byte[] Utf8Json, string? Pin = null);

/// <summary>What <see cref="LexiconGenerator.Generate"/> produced.</summary>
/// <param name="Files">The files, sorted by path; empty when there are errors.</param>
/// <param name="Diagnostics">Every finding.</param>
/// <param name="TypeNames">The type-name inventory (LEX-90): one sorted line per definition.</param>
public sealed record GenerationResult(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<GenerationDiagnostic> Diagnostics, string TypeNames);

/// <summary>What kind of definition an external type represents.</summary>
public enum ExternalTypeKind
{
    /// <summary>An object.</summary>
    Object,

    /// <summary>A record.</summary>
    Record,

    /// <summary>A token.</summary>
    Token,

    /// <summary>A string with known values.</summary>
    KnownValues,

    /// <summary>A union.</summary>
    Union,
}

/// <summary>Lexicon definitions provided by referenced assemblies, read from their <c>[LexiconDefinition]</c> attributes (LEX-8).</summary>
public interface IExternalLexiconTypes
{
    /// <summary>Finds the type of a definition.</summary>
    /// <param name="reference">The definition: <c>nsid</c> for <c>main</c>, <c>nsid#name</c> otherwise.</param>
    /// <param name="typeName">The fully qualified type name, without <c>global::</c>.</param>
    /// <param name="kind">What kind of definition it is.</param>
    /// <returns>Whether a referenced assembly has it.</returns>
    bool TryResolve(string reference, out string typeName, out ExternalTypeKind kind);

    /// <summary>The types of string formats beyond the built-in ones (<c>[LexiconFormatType]</c>), such as <c>space-ref</c>.</summary>
    IReadOnlyDictionary<string, string> FormatTypes { get; }
}
