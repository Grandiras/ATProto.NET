using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace ATProtoNet.Lexicon.Cli;

public sealed partial class LexgenApp
{
    private Command InstallCommand()
    {
        var patterns = new Argument<string[]>("nsids") { Description = "NSIDs, or namespace patterns such as app.bsky.*.", Arity = ArgumentArity.OneOrMore };
        var source = new Option<string?>("--source") { Description = "The manifest source to install from (default: the source whose namespaces cover them, or the only one)." };
        var manifest = Manifest();
        var references = new Option<string[]>("--reference") { Description = "An assembly whose [LexiconDefinition] types provide NSIDs that are then not installed (repeatable)." };
        var command = new Command("install", "Adds Lexicons and the Lexicons they reference to lexicons/ and lexicons.json (TOO-14).") { patterns, source, manifest, references };
        command.SetAction((result, cancellationToken) => Install(
            result.GetValue(patterns)!, result.GetValue(source), result.GetValue(manifest)!, result.GetValue(references) ?? [], cancellationToken));
        return command;
    }

    private Command UpdateCommand()
    {
        var patterns = new Argument<string[]>("nsids") { Description = "Update only the sources of these NSIDs or patterns (default: every source).", Arity = ArgumentArity.ZeroOrMore };
        var commit = new Option<string?>("--commit") { Description = "Move the git source being updated to this commit." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print what would change; write nothing." };
        var manifest = Manifest();
        var command = new Command("update", "Re-resolves the manifest's sources and rewrites what changed (TOO-18).") { patterns, commit, dryRun, manifest };
        command.SetAction((result, cancellationToken) => Update(
            result.GetValue(patterns) ?? [], result.GetValue(commit), result.GetValue(dryRun), result.GetValue(manifest)!, cancellationToken));
        return command;
    }

    // TOO-14, TOO-16, TOO-17.
    private async Task<int> Install(string[] patterns, string? sourceName, string manifestPath, string[] references, CancellationToken cancellationToken)
    {
        CheckPatterns(patterns);
        var manifest = ReadManifest(manifestPath);
        var external = ExternalTypes.Read(references, Env.Files);
        sourceName ??= DefaultSource(manifest, patterns);
        var tree = await FetchAsync(manifest, manifestPath, sourceName, null, cancellationToken);
        var wanted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            var matches = tree.Files.Keys.Where(nsid => Matches(pattern, nsid)).ToList();
            if (matches.Count == 0) throw new LexgenException(ExitCodes.Input, $"{pattern}: source \"{sourceName}\" has no such Lexicon.");
            wanted.UnionWith(matches);
        }
        AddReferences(tree, wanted, manifest, external);
        var changes = Apply(manifest, manifestPath, tree, wanted, [], dryRun: false);
        foreach (var pattern in patterns.Where(p => p.EndsWith(".*", StringComparison.Ordinal)))
            AddNamespace(tree.Source, pattern[..^2]);
        return await Finish(manifest, manifestPath, changes, dryRun: false);
    }

    // TOO-18: every selected source is re-read whole (at --commit for git), so its files and its provenance agree.
    private async Task<int> Update(string[] patterns, string? commit, bool dryRun, string manifestPath, CancellationToken cancellationToken)
    {
        CheckPatterns(patterns);
        if (commit is not null && !CommitSha().IsMatch(commit)) throw new LexgenException(ExitCodes.Usage, $"--commit: \"{commit}\" is not a commit SHA.");
        var manifest = ReadManifest(manifestPath);
        List<string> names = patterns.Length == 0 ? [.. manifest.Sources.Select(s => s.Key)] : SourcesOf(manifest, patterns);
        if (patterns.Length == 0)
            foreach (var name in names.Where(n => (string?)manifest.Sources[n]?["type"] == "network").ToList())
            {
                await Env.Error.WriteLineAsync($"{name}: network sources are updated from phase 8 on; skipped.");
                names.Remove(name);
            }
        if (commit is not null && names.Count(n => (string?)manifest.Sources[n]?["type"] == "git") != 1)
            throw new LexgenException(ExitCodes.Usage, "--commit moves one git source; name its Lexicons so that exactly one is selected.");

        var changes = new List<Change>();
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            var tree = await FetchAsync(manifest, manifestPath, name, commit, cancellationToken);
            var vendored = manifest.Lexicons.Where(e => manifest.SourceOf(e.Key) == name).Select(e => e.Key).ToList();
            var wanted = new SortedSet<string>(vendored.Where(tree.Files.ContainsKey), StringComparer.Ordinal);
            wanted.UnionWith(tree.Files.Keys.Where(nsid => OwnerOf(manifest, nsid) == name && manifest.SourceOf(nsid) is null));
            AddReferences(tree, wanted, manifest, null);
            changes.AddRange(Apply(manifest, manifestPath, tree, wanted, [.. vendored.Where(n => !tree.Files.ContainsKey(n))], dryRun));
        }
        return await Finish(manifest, manifestPath, changes, dryRun);
    }

    private sealed record Change(string Nsid, string Kind);

    // Writes the wanted files and manifest entries that differ, removes the removed ones, and records provenance when anything changed.
    private List<Change> Apply(LexiconManifest manifest, string manifestPath, SourceTree tree, SortedSet<string> wanted, IReadOnlyList<string> removed, bool dryRun)
    {
        var directory = LexiconsDirectory(manifestPath, null);
        var changes = new List<Change>();
        foreach (var nsid in wanted)
        {
            var path = LexiconManifest.PathOf(directory, nsid);
            var bytes = tree.Files[nsid];
            var cid = Parse(tree, nsid).ComputeRecordCid().ToString();
            var before = Env.Files.FileExists(path) ? Env.Files.ReadAllBytes(path) : null;
            if (before is not null && before.AsSpan().SequenceEqual(bytes) && manifest.SourceOf(nsid) == tree.Name && manifest.CidOf(nsid) == cid) continue;
            changes.Add(new(nsid, manifest.CidOf(nsid) is null || before is null ? "added" : Classify(before, bytes)));
            if (dryRun) continue;
            Env.Files.WriteAllBytes(path, bytes);
            manifest.Set(nsid, tree.Name, cid);
        }
        foreach (var nsid in removed)
        {
            changes.Add(new(nsid, "removed"));
            if (dryRun) continue;
            var path = LexiconManifest.PathOf(directory, nsid);
            if (Env.Files.FileExists(path)) Env.Files.Delete(path);
            manifest.Remove(nsid);
        }
        if (changes.Count == 0 || dryRun) return changes;
        if (tree.Commit is not null) tree.Source["commit"] = tree.Commit;
        tree.Source["fetched"] = Env.Time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return changes;
    }

    // TOO-18: a changed schema is compatible or breaking by the versioning rules (LEX-14–LEX-16); one that no longer parses is breaking.
    private static string Classify(byte[] before, byte[] after)
    {
        if (!LexiconDocument.TryParse(before, out var old, out _) || !LexiconDocument.TryParse(after, out var current, out _)) return "breaking";
        var report = LexiconCompatibility.Compare(LexiconCatalog.Create([old]), LexiconCatalog.Create([current]));
        return report.HasBreakingChanges ? "breaking" : "compatible";
    }

    private async Task<int> Finish(LexiconManifest manifest, string manifestPath, List<Change> changes, bool dryRun)
    {
        foreach (var change in changes.OrderBy(c => c.Nsid, StringComparer.Ordinal))
            await Env.Out.WriteLineAsync($"{change.Kind} {change.Nsid}");
        var summary = string.Join(", ", changes.GroupBy(c => c.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}"));
        await Env.Error.WriteLineAsync((changes.Count == 0 ? "Nothing changed" : summary) + (dryRun ? " (dry run; nothing written)." : "."));
        var bytes = manifest.ToUtf8();
        if (!dryRun && !Env.Files.ReadAllBytes(manifestPath).AsSpan().SequenceEqual(bytes)) Env.Files.WriteAllBytes(manifestPath, bytes);
        return ExitCodes.Success;
    }

    // TOO-17: what the wanted Lexicons reference, from the same source, unless it is vendored already or provided by a reference.
    private void AddReferences(SourceTree tree, SortedSet<string> wanted, LexiconManifest manifest, ExternalTypes? external)
    {
        var pending = new Queue<string>(wanted);
        var missing = new List<string>();
        while (pending.TryDequeue(out var nsid))
            foreach (var reference in ReferencedNsids(Parse(tree, nsid)))
            {
                if (wanted.Contains(reference) || manifest.SourceOf(reference) is not null) continue;
                if (external?.Provides(reference) == true || manifest.Provided.Any(p => Matches(p, reference))) continue;
                if (!tree.Files.ContainsKey(reference))
                {
                    missing.Add($"{nsid} references {reference}");
                    continue;
                }
                wanted.Add(reference);
                pending.Enqueue(reference);
            }
        if (missing.Count > 0)
            throw new LexgenException(ExitCodes.Input, $"{string.Join("; ", missing)}, which source \"{tree.Name}\" does not have; install it from another source, or list it under \"provided\".");
    }

    // The NSIDs a document's refs and unions point outside itself, as the catalog resolves them.
    private static IEnumerable<string> ReferencedNsids(LexiconDocument document)
    {
        var recorder = new ReferenceRecorder();
        LexiconCatalog.Create([document], recorder);
        return recorder.Nsids.Order(StringComparer.Ordinal);
    }

    private static LexiconDocument Parse(SourceTree tree, string nsid)
    {
        var document = Catch($"{tree.Name}:{nsid}", () => LexiconDocument.Parse(tree.Files[nsid]));
        return document.Id.ToString() == nsid ? document : throw new LexgenException(ExitCodes.Input, $"{tree.Name}:{nsid}: the file's id is {document.Id}.");
    }

    private static string DefaultSource(LexiconManifest manifest, string[] patterns)
    {
        var owners = patterns.Select(p => OwnerOf(manifest, p.EndsWith(".*", StringComparison.Ordinal) ? p[..^2] : p)).Distinct().ToList();
        if (owners is [{ } owner]) return owner;
        return manifest.Sources.Count == 1 ? manifest.Sources.Single().Key
            : throw new LexgenException(ExitCodes.Usage, "Name the source to install from with --source.");
    }

    private static List<string> SourcesOf(LexiconManifest manifest, string[] patterns)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            var found = manifest.Lexicons.Where(e => Matches(pattern, e.Key)).Select(e => manifest.SourceOf(e.Key)!)
                .Append(OwnerOf(manifest, pattern.EndsWith(".*", StringComparison.Ordinal) ? pattern[..^2] : pattern)).OfType<string>().ToList();
            if (found.Count == 0) throw new LexgenException(ExitCodes.Input, $"{pattern}: no source in the manifest has such Lexicons.");
            names.UnionWith(found);
        }
        return [.. names];
    }

    // An installed namespace pattern joins the source's namespaces, so update brings its new Lexicons too.
    private static void AddNamespace(JsonObject source, string prefix)
    {
        if (NamespacesOf(source).Any(ns => Covers(ns, prefix))) return;
        var namespaces = source["namespaces"] as JsonArray ?? [];
        var sorted = namespaces.Select(n => (string?)n).OfType<string>().Append(prefix.Replace('.', '/')).Order(StringComparer.Ordinal).ToList();
        source["namespaces"] = new JsonArray([.. sorted.Select(n => (JsonNode)n)]);
    }

    private sealed class ReferenceRecorder : IExternalLexicons
    {
        public SortedSet<string> Nsids { get; } = new(StringComparer.Ordinal);

        public bool TryResolve(LexRefTarget target, [NotNullWhen(true)] out ExternalDefinition? definition)
        {
            Nsids.Add(target.Nsid.ToString());
            definition = new(target, target.ToString(), ExternalDefinitionKind.Other);
            return true;
        }
    }
}
