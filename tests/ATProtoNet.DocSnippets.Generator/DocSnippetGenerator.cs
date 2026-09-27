using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ATProtoNet.DocSnippets.Generator;

/// <summary>
/// Turns every <c>```csharp</c> block of the Markdown files passed as <c>AdditionalFiles</c>
/// into code for the compiler, with <c>#line</c> directives that point back at the Markdown,
/// so a sample that no longer matches the API fails the build at its own line.
/// </summary>
/// <remarks>
/// <para>
/// A block is parsed as C# top-level code and split three ways: type declarations go into a
/// namespace shared by every block of the page (so a later block can use a type an earlier one
/// declared), members with an access modifier go into a class of their own, and everything
/// else (statements, local functions) into that class's <c>RunAsync</c> method. The class
/// derives from <c>DocSnippets.SnippetContext</c>, which supplies the variables samples take
/// for granted, such as <c>client</c> and <c>builder</c>.
/// </para>
/// <para>
/// A <c>using</c> directive in any block applies to every block of the page, as a reader
/// carries it from one block to the next. Words after the language on the opening fence
/// change the rest: <c>partial</c> leaves the block out (a fragment), as does <c>before</c> (the
/// migration guide's code for an API that no longer exists), and <c>continued</c> puts it in the
/// same method as the block before it, so it sees that block's variables.
/// </para>
/// <para>
/// A block that uses a variable of the code around it, such as the <c>session</c> a callback
/// received, declares it in an HTML comment on the line before the fence, which Markdown does
/// not render: <c>&lt;!-- snippet: OAuthSession session; string code, state; --&gt;</c>. The
/// declarations become fields of the block's class, so the block type-checks against them.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class DocSnippetGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pages = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => (file.Path, Text: file.GetText(ct)?.ToString() ?? string.Empty));

        context.RegisterSourceOutput(pages, static (output, page) =>
        {
            foreach (var (hintName, source) in SnippetEmitter.Emit(page.Path, page.Text))
                output.AddSource(hintName, source);
        });
    }
}

internal sealed class Snippet
{
    public Snippet(int firstLine, bool continued, CompilationUnitSyntax root, string? assumed, int assumedLine)
    {
        FirstLine = firstLine;
        Continued = continued;
        Root = root;
        Assumed = assumed;
        AssumedLine = assumedLine;
    }

    /// <summary>The 1-based Markdown line of the block's first line of code.</summary>
    public int FirstLine { get; }

    public bool Continued { get; }

    /// <summary>The block parsed as C# top-level code.</summary>
    public CompilationUnitSyntax Root { get; }

    /// <summary>The declarations of a <c>&lt;!-- snippet: … --&gt;</c> comment before the block.</summary>
    public string? Assumed { get; }

    /// <summary>The 1-based Markdown line of that comment.</summary>
    public int AssumedLine { get; }
}

internal static class SnippetEmitter
{
    private static readonly Regex OpeningFence = new(@"^(?<indent> *)(?<fence>`{3,}|~{3,})(?<info>[^`]*)$");

    private static readonly Regex AssumedVariables = new(@"^\s*<!--\s*snippet:(?<declarations>.*)-->\s*$");

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, kind: SourceCodeKind.Regular);

    private static readonly HashSet<string> CSharpLanguages = new(StringComparer.OrdinalIgnoreCase) { "csharp", "cs", "c#" };

    public static IEnumerable<(string HintName, string Source)> Emit(string path, string markdown)
    {
        var page = PageId(path);
        var units = new List<List<Snippet>>();
        foreach (var snippet in Extract(markdown))
        {
            if (snippet.Continued && units.Count > 0)
                units[units.Count - 1].Add(snippet);
            else
                units.Add([snippet]);
        }

        var file = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var usings = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snippet in units.SelectMany(unit => unit))
        {
            foreach (var directive in snippet.Root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax).OfType<UsingDirectiveSyntax>())
            {
                if (seen.Add(directive.WithoutTrivia().ToString()))
                    Append(usings, file, snippet, directive);
            }
        }

        foreach (var unit in units)
            yield return ($"{page}.L{unit[0].FirstLine:D4}.g.cs", EmitUnit(file, path, page, usings.ToString(), unit));
    }

    private static void Append(StringBuilder target, string file, Snippet snippet, SyntaxNode node)
    {
        var line = snippet.Root.SyntaxTree.GetLineSpan(node.FullSpan).StartLinePosition.Line;
        var text = node.ToFullString();
        target.Append("#line ").Append(snippet.FirstLine + line).Append(" \"").Append(file).Append("\"\n");
        target.Append(text);
        if (!text.EndsWith("\n", StringComparison.Ordinal))
            target.Append('\n');
    }

    internal static List<Snippet> Extract(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var snippets = new List<Snippet>();
        for (var i = 0; i < lines.Length; i++)
        {
            var open = OpeningFence.Match(lines[i]);
            if (!open.Success)
                continue;

            var indent = open.Groups["indent"].Length;
            var fence = open.Groups["fence"].Value;
            var info = open.Groups["info"].Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            var close = i + 1;
            while (close < lines.Length && !IsClosingFence(lines[close], fence))
                close++;

            if (info.Length > 0 && CSharpLanguages.Contains(info[0]) && !info.Contains("partial") && !info.Contains("before"))
            {
                var code = new StringBuilder();
                for (var j = i + 1; j < close; j++)
                    code.Append(Unindent(lines[j], indent)).Append('\n');
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(code.ToString(), ParseOptions).GetRoot();
                var assumed = i > 0 ? AssumedVariables.Match(lines[i - 1]) : Match.Empty;
                snippets.Add(new Snippet(
                    i + 2,
                    info.Contains("continued"),
                    root,
                    assumed.Success ? assumed.Groups["declarations"].Value.Trim() : null,
                    i));
            }

            i = close;
        }

        return snippets;
    }

    private static bool IsClosingFence(string line, string fence)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
    }

    private static string Unindent(string line, int indent)
    {
        var strip = 0;
        while (strip < indent && strip < line.Length && line[strip] == ' ')
            strip++;
        return line.Substring(strip);
    }

    private static string PageId(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var id = new StringBuilder("Page_");
        foreach (var c in name)
            id.Append(char.IsLetterOrDigit(c) ? c : '_');
        return id.ToString();
    }

    private static string EmitUnit(string file, string path, string page, string usings, List<Snippet> unit)
    {
        var types = new StringBuilder();
        var members = new StringBuilder();
        var statements = new StringBuilder();

        foreach (var snippet in unit)
        {
            void Append(StringBuilder target, SyntaxNode node) => SnippetEmitter.Append(target, file, snippet, node);

            if (snippet.Assumed is { } assumed)
                members.Append("#line ").Append(snippet.AssumedLine).Append(" \"").Append(file).Append("\"\n").Append(assumed).Append('\n');

            void Classify(SyntaxList<MemberDeclarationSyntax> declarations)
            {
                foreach (var declaration in declarations)
                {
                    switch (declaration)
                    {
                        case BaseNamespaceDeclarationSyntax ns:
                            // The page's namespace replaces the sample's own.
                            Classify(ns.Members);
                            break;
                        case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                            Append(types, declaration);
                            break;
                        case GlobalStatementSyntax or IncompleteMemberSyntax:
                            Append(statements, declaration);
                            break;
                        case MethodDeclarationSyntax method when !HasAccessModifier(method.Modifiers):
                            // A method with no access modifier reads as a local function.
                            Append(statements, declaration);
                            break;
                        default:
                            Append(members, declaration);
                            break;
                    }
                }
            }

            Classify(snippet.Root.Members);
        }

        var name = $"Snippet_L{unit[0].FirstLine}";
        return $$"""
            // <auto-generated/>
            // Generated from {{path}} by ATProtoNet.DocSnippets.Generator.
            #nullable enable annotations
            #pragma warning disable CS0105, CS0108, CS0162, CS0168, CS0169, CS0219, CS0414, CS0649, CS1998, CS4014, CS8321
            {{usings}}#line default
            namespace DocSnippets.{{page}}
            {
            {{types}}#line default
                internal sealed class {{name}} : global::DocSnippets.SnippetContext
                {
            {{members}}#line default
                    public async global::System.Threading.Tasks.Task RunAsync()
                    {
            {{statements}}#line default
                    }
                }
            }

            """;
    }

    private static bool HasAccessModifier(SyntaxTokenList modifiers) =>
        modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)
            || m.IsKind(SyntaxKind.PrivateKeyword)
            || m.IsKind(SyntaxKind.ProtectedKeyword)
            || m.IsKind(SyntaxKind.InternalKeyword)
            || m.IsKind(SyntaxKind.OverrideKeyword));
}
