using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ATProtoNet.Lexicon.Cli;

public sealed partial class LexgenApp
{
    private Command LintCommand()
    {
        var paths = new Argument<string[]>("paths") { Description = "Lexicon files or directories (default: the manifest's Lexicons).", Arity = ArgumentArity.ZeroOrMore };
        var manifest = Manifest();
        var strict = new Option<bool>("--strict") { Description = "Exit 1 on warnings too." };
        var format = new Option<string>("--format") { Description = "text or json.", DefaultValueFactory = _ => "text" };
        format.AcceptOnlyFromAmong("text", "json");
        var command = new Command("lint", "Checks Lexicons against the Lexicon and permission rules (LEX-10–LEX-13).") { paths, manifest, strict, format };
        command.SetAction((result, cancellationToken) => Lint(result.GetValue(paths) ?? [], result.GetValue(manifest)!, result.GetValue(strict), result.GetValue(format)!));
        return command;
    }

    // TOO-19: parse violations, unresolved references within the set, and the lint rules; errors exit 1, warnings only under --strict.
    private async Task<int> Lint(string[] paths, string manifestPath, bool strict, string format)
    {
        var files = paths.Length > 0 ? ExpandPaths(paths) : ManifestFiles(manifestPath);
        var findings = new List<(string File, LexiconDiagnostic Diagnostic)>();
        var documents = new Dictionary<string, (string File, LexiconDocument Document)>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var bytes = Catch(file, () => Env.Files.ReadAllBytes(file));
            if (!IsJson(bytes)) throw new LexgenException(ExitCodes.Input, $"{file}: not valid JSON.");
            LexiconDocument.TryParse(bytes, out var document, out var diagnostics);
            findings.AddRange(diagnostics.Select(d => (file, d)));
            if (document is not null) documents.TryAdd(document.Id.ToString(), (file, document));
        }
        if (files.Count == 0) throw new LexgenException(ExitCodes.Input, "There are no Lexicons to lint.");
        var catalog = LexiconCatalog.Create(documents.Values.Select(d => d.Document), new OpenWorld(documents.Keys));
        findings.AddRange(catalog.Diagnostics.Concat(LexiconLint.Run(catalog)).Select(d => (documents[d.DocumentId!].File, d)));
        var sorted = findings.OrderBy(f => f.Diagnostic.DocumentId ?? f.File, StringComparer.Ordinal).ThenBy(f => f.Diagnostic.Pointer, StringComparer.Ordinal).ToList();
        var errors = sorted.Count(f => f.Diagnostic.Severity == LexiconSeverity.Error);
        var warnings = sorted.Count - errors;
        if (format == "json")
            await Env.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                diagnostics = sorted.Select(f => new
                {
                    file = f.File,
                    nsid = f.Diagnostic.DocumentId,
                    pointer = f.Diagnostic.Pointer,
                    rule = f.Diagnostic.RuleId,
                    severity = f.Diagnostic.Severity == LexiconSeverity.Error ? "error" : "warning",
                    message = f.Diagnostic.Message,
                }),
                errors,
                warnings,
            }));
        else
        {
            foreach (var (file, diagnostic) in sorted)
                await Env.Out.WriteLineAsync($"{file} {diagnostic.Pointer}: {(diagnostic.Severity == LexiconSeverity.Error ? "error" : "warning")} {diagnostic.RuleId}: {diagnostic.Message}");
            await Env.Error.WriteLineAsync($"{documents.Count} Lexicons: {errors} errors, {warnings} warnings.");
        }
        return errors > 0 || strict && warnings > 0 ? ExitCodes.Findings : ExitCodes.Success;
    }

    private List<string> ExpandPaths(string[] paths) =>
    [
        .. paths.SelectMany(path => Env.Files.DirectoryExists(path) ? Env.Files.EnumerateFiles(path, "*.json").Order(StringComparer.Ordinal)
            : Env.Files.FileExists(path) ? (IEnumerable<string>)[path]
            : throw new LexgenException(ExitCodes.Input, $"{path}: no such file or directory.")),
    ];

    private List<string> ManifestFiles(string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);
        var directory = LexiconsDirectory(manifestPath, null);
        return [.. manifest.Lexicons.Select(e => LexiconManifest.PathOf(directory, e.Key))];
    }

    private static bool IsJson(byte[] bytes)
    {
        try
        {
            using var _ = JsonDocument.Parse(bytes);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Lint checks references within the set; a document outside it may live anywhere, so references to it pass.
    private sealed class OpenWorld(IEnumerable<string> inSet) : IExternalLexicons
    {
        private readonly HashSet<string> _inSet = new(inSet, StringComparer.Ordinal);

        public bool TryResolve(LexRefTarget target, [NotNullWhen(true)] out ExternalDefinition? definition)
        {
            definition = _inSet.Contains(target.Nsid.ToString()) ? null : new(target, target.ToString(), ExternalDefinitionKind.Other);
            return definition is not null;
        }
    }
}
