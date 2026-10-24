using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ATProtoNet.Lexicon.CodeGen;

/// <summary>The Lexicon C# emitter (LEX-25): the same inputs and options always produce the same files.</summary>
public static class LexiconGenerator
{
    /// <summary>Generates C# for a set of Lexicon documents.</summary>
    /// <param name="sources">The documents.</param>
    /// <param name="config">The options.</param>
    /// <param name="external">Definitions and formats from referenced assemblies, if any.</param>
    /// <param name="units">The units to write files for; all when <see langword="null"/>. Every unit is still planned, so references and the inventory cover all.</param>
    /// <returns>The files, sorted by path, or none when there is an error; and every finding.</returns>
    public static GenerationResult Generate(IReadOnlyList<LexiconSource> sources, LexgenConfig config, IExternalLexiconTypes? external = null, IReadOnlyCollection<string>? units = null)
    {
        if (sources is null) throw new ArgumentNullException(nameof(sources));
        if (config is null) throw new ArgumentNullException(nameof(config));
        var diagnostics = new List<GenerationDiagnostic>();
        var documents = new List<LexiconDocument>();
        var files = new Dictionary<string, LexiconSource>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            LexiconDocument.TryParse(source.Utf8Json, out var document, out var findings);
            diagnostics.AddRange(findings.Select(d => From(d, source.Path)));
            if (document is null) continue;
            if (files.ContainsKey(document.Id.ToString()))
                diagnostics.Add(new("ATLEX007", GenerationSeverity.Error, $"{document.Id} is defined twice (also in {files[document.Id.ToString()].Path}).", source.Path, ""));
            else
            {
                files[document.Id.ToString()] = source;
                documents.Add(document);
            }
        }
        if (Failed(diagnostics)) return new([], diagnostics, "");

        var catalog = LexiconCatalog.Create(documents, external is null ? null : new ExternalAdapter(external));
        diagnostics.AddRange(catalog.Diagnostics.Concat(LexiconLint.Run(catalog).Where(d => d.Severity == LexiconSeverity.Error))
            .Select(d => From(d, d.DocumentId is { } id && files.TryGetValue(id, out var source) ? source.Path : null)));
        if (Failed(diagnostics)) return new([], diagnostics, "");

        var plan = new Planner(catalog, config, external);
        plan.Plan();
        var output = new List<GeneratedFile>();
        if (!Failed(plan.Diagnostics))
        {
            var emitter = new Emitter(plan);
            foreach (var unit in plan.Units.Where(u => units is null || units.Contains(u.Name)))
            {
                foreach (var document in unit.Documents)
                    output.Add(new($"{unit.Name}/{document.Id}.g.cs", emitter.Document(document, unit.Decls.Where(d => d.Document == document), files[document.Id.ToString()].Pin)));
                output.Add(new($"{unit.Name}/{unit.Name}Namespaces.g.cs", emitter.Namespaces(unit)));
                output.Add(new($"{unit.Name}/{unit.Name}Lexicons.g.cs", emitter.Lexicons(unit)));
                output.Add(new($"{unit.Name}/{unit.Name}LexiconsJsonContext.g.cs", emitter.Context(unit)));
            }
        }
        foreach (var unused in config.Renames.Keys.Where(k => !plan.UsedRenames.Contains(k)))
            plan.Diagnostics.Add(new("ATLEX008", GenerationSeverity.Warning, $"The \"renames\" entry \"{unused}\" matches no definition or property.", null, unused));
        foreach (var unused in config.FieldTypes.Keys.Where(k => !plan.UsedFieldTypes.Contains(k)))
            plan.Diagnostics.Add(new("ATLEX008", GenerationSeverity.Warning, $"The \"fieldTypes\" entry \"{unused}\" matches no field.", null, unused));
        foreach (var unused in config.UnionExtensions.Keys.Where(k => !plan.UsedUnionExtensions.Contains(k) && !plan.UsedUnionExtensions.Contains(k + "#main")))
            plan.Diagnostics.Add(new("ATLEX008", GenerationSeverity.Warning, $"The \"unionExtensions\" entry \"{unused}\" matches no object or record definition.", null, unused));
        diagnostics.AddRange(plan.Diagnostics.Select(d => d.File is { } id && files.TryGetValue(id, out var source) ? d with { File = source.Path } : d));
        return new(Failed(diagnostics) ? [] : [.. output.OrderBy(f => f.Path, StringComparer.Ordinal)], diagnostics, plan.Inventory());
    }

    private static bool Failed(IEnumerable<GenerationDiagnostic> diagnostics) => diagnostics.Any(d => d.Severity == GenerationSeverity.Error);

    private static GenerationDiagnostic From(LexiconDiagnostic diagnostic, string? file) =>
        new(diagnostic.RuleId, diagnostic.Severity == LexiconSeverity.Error ? GenerationSeverity.Error : GenerationSeverity.Warning, diagnostic.Message, file, diagnostic.Pointer);

    private sealed class ExternalAdapter(IExternalLexiconTypes external) : IExternalLexicons
    {
        public bool TryResolve(LexRefTarget target, [NotNullWhen(true)] out ExternalDefinition? definition)
        {
            definition = external.TryResolve(target.ToString(), out var typeName, out var kind)
                ? new(target, typeName, kind switch
                {
                    ExternalTypeKind.Object => ExternalDefinitionKind.Object,
                    ExternalTypeKind.Record => ExternalDefinitionKind.Record,
                    ExternalTypeKind.Token => ExternalDefinitionKind.Token,
                    ExternalTypeKind.KnownValues => ExternalDefinitionKind.KnownValues,
                    ExternalTypeKind.Union => ExternalDefinitionKind.Union,
                    _ => ExternalDefinitionKind.Other,
                })
                : null;
            return definition is not null;
        }
    }
}
