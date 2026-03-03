using System.Text;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.CodeGen;

/// <summary>
/// Compares two sets of Lexicon documents and produces a migration report.
/// Enforces AT Protocol schema evolution rules:
/// <list type="bullet">
///   <item>New fields must be optional</item>
///   <item>Existing fields cannot be removed or renamed</item>
///   <item>Field types cannot change</item>
///   <item>Breaking changes require a new NSID</item>
/// </list>
/// </summary>
public sealed class LexiconDiffer
{
    /// <summary>
    /// Compare two sets of Lexicon documents and return a diff result.
    /// </summary>
    /// <param name="baseline">The previous/existing schemas.</param>
    /// <param name="current">The new/proposed schemas.</param>
    /// <returns>A diff result describing all changes and whether they are breaking.</returns>
    public DiffResult Compare(
        IReadOnlyList<LexiconDocument> baseline,
        IReadOnlyList<LexiconDocument> current)
    {
        var baselineMap = baseline.ToDictionary(d => d.Id, d => d);
        var currentMap = current.ToDictionary(d => d.Id, d => d);
        var changes = new List<SchemaChange>();

        // Detect removed schemas (breaking)
        foreach (var id in baselineMap.Keys)
        {
            if (!currentMap.ContainsKey(id))
            {
                changes.Add(new SchemaChange(
                    id, null, ChangeKind.SchemaRemoved, "Schema removed",
                    IsBreaking: true));
            }
        }

        // Detect added schemas (non-breaking)
        foreach (var id in currentMap.Keys)
        {
            if (!baselineMap.ContainsKey(id))
            {
                changes.Add(new SchemaChange(
                    id, null, ChangeKind.SchemaAdded, "Schema added",
                    IsBreaking: false));
            }
        }

        // Detect changes within shared schemas
        foreach (var (id, baseDoc) in baselineMap)
        {
            if (!currentMap.TryGetValue(id, out var curDoc))
                continue;

            CompareDocument(id, baseDoc, curDoc, changes);
        }

        return new DiffResult(changes);
    }

    private void CompareDocument(
        string nsid,
        LexiconDocument baseline,
        LexiconDocument current,
        List<SchemaChange> changes)
    {
        // Compare definitions
        var baseDefs = baseline.Defs ?? new();
        var curDefs = current.Defs ?? new();

        foreach (var (defName, baseDef) in baseDefs)
        {
            if (!curDefs.TryGetValue(defName, out var curDef))
            {
                changes.Add(new SchemaChange(
                    nsid, defName, ChangeKind.DefinitionRemoved,
                    $"Definition '{defName}' removed",
                    IsBreaking: true));
                continue;
            }

            CompareDefinition(nsid, defName, baseDef, curDef, changes);
        }

        foreach (var defName in curDefs.Keys)
        {
            if (!baseDefs.ContainsKey(defName))
            {
                changes.Add(new SchemaChange(
                    nsid, defName, ChangeKind.DefinitionAdded,
                    $"Definition '{defName}' added",
                    IsBreaking: false));
            }
        }
    }

    private void CompareDefinition(
        string nsid, string defName,
        LexiconSchema baseline, LexiconSchema current,
        List<SchemaChange> changes)
    {
        var path = defName;

        // Type change is always breaking
        if (baseline.Type != current.Type)
        {
            changes.Add(new SchemaChange(
                nsid, path, ChangeKind.TypeChanged,
                $"Type changed from '{baseline.Type}' to '{current.Type}'",
                IsBreaking: true));
            return;
        }

        // For record types, compare the inner record schema
        if (baseline.Type == "record" && baseline.Record is not null && current.Record is not null)
        {
            CompareObjectSchema(nsid, path, baseline.Record, current.Record, changes);
            return;
        }

        // For object types, compare properties
        if (baseline.Type == "object")
        {
            CompareObjectSchema(nsid, path, baseline, current, changes);
            return;
        }

        // For query/procedure, compare parameters and input/output
        if (baseline.Type is "query" or "procedure")
        {
            if (baseline.Parameters is not null && current.Parameters is not null)
                CompareObjectSchema(nsid, $"{path}.parameters", baseline.Parameters, current.Parameters, changes);

            if (baseline.Input?.Schema is not null && current.Input?.Schema is not null)
                CompareObjectSchema(nsid, $"{path}.input", baseline.Input.Schema, current.Input.Schema, changes);

            if (baseline.Output?.Schema is not null && current.Output?.Schema is not null)
                CompareObjectSchema(nsid, $"{path}.output", baseline.Output.Schema, current.Output.Schema, changes);
        }

        // For string types, compare constraints
        if (baseline.Type == "string")
            CompareStringConstraints(nsid, path, baseline, current, changes);
    }

    private void CompareObjectSchema(
        string nsid, string path,
        LexiconSchema baseline, LexiconSchema current,
        List<SchemaChange> changes)
    {
        var baseProps = baseline.Properties ?? new();
        var curProps = current.Properties ?? new();
        var baseRequired = new HashSet<string>(baseline.Required ?? []);
        var curRequired = new HashSet<string>(current.Required ?? []);

        // Removed properties (breaking)
        foreach (var (propName, _) in baseProps)
        {
            if (!curProps.ContainsKey(propName))
            {
                changes.Add(new SchemaChange(
                    nsid, $"{path}.{propName}", ChangeKind.PropertyRemoved,
                    $"Property '{propName}' removed",
                    IsBreaking: true));
            }
        }

        // Added properties
        foreach (var (propName, _) in curProps)
        {
            if (!baseProps.ContainsKey(propName))
            {
                var isRequired = curRequired.Contains(propName);
                changes.Add(new SchemaChange(
                    nsid, $"{path}.{propName}", ChangeKind.PropertyAdded,
                    $"Property '{propName}' added{(isRequired ? " (required — BREAKING)" : " (optional)")}",
                    IsBreaking: isRequired));
            }
        }

        // Changed properties
        foreach (var (propName, baseProp) in baseProps)
        {
            if (!curProps.TryGetValue(propName, out var curProp))
                continue;

            // Type change
            if (baseProp.Type != curProp.Type)
            {
                changes.Add(new SchemaChange(
                    nsid, $"{path}.{propName}", ChangeKind.PropertyTypeChanged,
                    $"Property '{propName}' type changed from '{baseProp.Type}' to '{curProp.Type}'",
                    IsBreaking: true));
            }

            // Required status change
            var wasRequired = baseRequired.Contains(propName);
            var isNowRequired = curRequired.Contains(propName);
            if (!wasRequired && isNowRequired)
            {
                changes.Add(new SchemaChange(
                    nsid, $"{path}.{propName}", ChangeKind.PropertyBecameRequired,
                    $"Property '{propName}' changed from optional to required",
                    IsBreaking: true));
            }
            else if (wasRequired && !isNowRequired)
            {
                changes.Add(new SchemaChange(
                    nsid, $"{path}.{propName}", ChangeKind.PropertyBecameOptional,
                    $"Property '{propName}' changed from required to optional",
                    IsBreaking: false));
            }

            // Recurse into nested objects/arrays
            if (baseProp.Type == "object" && curProp.Type == "object")
                CompareObjectSchema(nsid, $"{path}.{propName}", baseProp, curProp, changes);

            if (baseProp.Type == "array" && curProp.Type == "array" &&
                baseProp.Items is not null && curProp.Items is not null)
            {
                if (baseProp.Items.Type != curProp.Items.Type)
                {
                    changes.Add(new SchemaChange(
                        nsid, $"{path}.{propName}.items", ChangeKind.PropertyTypeChanged,
                        $"Array items type changed from '{baseProp.Items.Type}' to '{curProp.Items.Type}'",
                        IsBreaking: true));
                }
            }
        }
    }

    private void CompareStringConstraints(
        string nsid, string path,
        LexiconSchema baseline, LexiconSchema current,
        List<SchemaChange> changes)
    {
        // Format change
        if (baseline.Format != current.Format)
        {
            changes.Add(new SchemaChange(
                nsid, path, ChangeKind.ConstraintChanged,
                $"String format changed from '{baseline.Format ?? "none"}' to '{current.Format ?? "none"}'",
                IsBreaking: true));
        }

        // MaxLength tightened (breaking)
        if (baseline.MaxLength is not null && current.MaxLength is not null &&
            current.MaxLength < baseline.MaxLength)
        {
            changes.Add(new SchemaChange(
                nsid, path, ChangeKind.ConstraintChanged,
                $"maxLength tightened from {baseline.MaxLength} to {current.MaxLength}",
                IsBreaking: true));
        }

        // MinLength loosened is OK, tightened is breaking
        if (baseline.MinLength is not null && current.MinLength is not null &&
            current.MinLength > baseline.MinLength)
        {
            changes.Add(new SchemaChange(
                nsid, path, ChangeKind.ConstraintChanged,
                $"minLength tightened from {baseline.MinLength} to {current.MinLength}",
                IsBreaking: true));
        }

        // Enum values removed (breaking)
        if (baseline.Enum is not null && current.Enum is not null)
        {
            var removed = baseline.Enum.Except(current.Enum).ToList();
            if (removed.Count > 0)
            {
                changes.Add(new SchemaChange(
                    nsid, path, ChangeKind.ConstraintChanged,
                    $"Enum values removed: {string.Join(", ", removed)}",
                    IsBreaking: true));
            }
        }
    }
}

/// <summary>Categorizes the type of schema change.</summary>
public enum ChangeKind
{
    SchemaAdded,
    SchemaRemoved,
    DefinitionAdded,
    DefinitionRemoved,
    TypeChanged,
    PropertyAdded,
    PropertyRemoved,
    PropertyTypeChanged,
    PropertyBecameRequired,
    PropertyBecameOptional,
    ConstraintChanged,
}

/// <summary>A single schema change with its location, description, and breaking status.</summary>
/// <param name="Nsid">The NSID of the affected schema.</param>
/// <param name="Path">The path within the schema (e.g., "main.record.title"), or null for top-level.</param>
/// <param name="Kind">The type of change.</param>
/// <param name="Description">Human-readable description of the change.</param>
/// <param name="IsBreaking">Whether this change violates AT Protocol schema evolution rules.</param>
public sealed record SchemaChange(
    string Nsid,
    string? Path,
    ChangeKind Kind,
    string Description,
    bool IsBreaking);

/// <summary>The result of comparing two sets of Lexicon documents.</summary>
public sealed class DiffResult
{
    public IReadOnlyList<SchemaChange> Changes { get; }
    public bool HasBreakingChanges => Changes.Any(c => c.IsBreaking);
    public bool HasChanges => Changes.Count > 0;
    public int BreakingCount => Changes.Count(c => c.IsBreaking);
    public int NonBreakingCount => Changes.Count(c => !c.IsBreaking);

    public DiffResult(IReadOnlyList<SchemaChange> changes)
    {
        Changes = changes;
    }

    /// <summary>Formats the diff result as a human-readable report.</summary>
    public string ToReport()
    {
        if (!HasChanges)
            return "No schema changes detected.";

        var sb = new StringBuilder();
        sb.AppendLine($"Schema Diff: {Changes.Count} change(s) ({BreakingCount} breaking, {NonBreakingCount} non-breaking)");
        sb.AppendLine(new string('─', 72));

        var grouped = Changes.GroupBy(c => c.Nsid).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine();
            sb.AppendLine($"  {group.Key}");

            foreach (var change in group.OrderBy(c => c.Path ?? ""))
            {
                var marker = change.IsBreaking ? "BREAK" : "  OK ";
                var path = change.Path is not null ? $" [{change.Path}]" : "";
                sb.AppendLine($"    [{marker}] {change.Description}{path}");
            }
        }

        sb.AppendLine();
        if (HasBreakingChanges)
        {
            sb.AppendLine("⚠ Breaking changes detected. These violate AT Protocol schema evolution rules.");
            sb.AppendLine("  See: https://atproto.com/specs/lexicon#lexicon-evolution");
        }
        else
        {
            sb.AppendLine("All changes are backwards-compatible.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Suggests updated revision numbers for schemas that have non-breaking changes.
    /// Returns a dictionary of NSID → suggested new revision.
    /// </summary>
    public Dictionary<string, int> SuggestRevisions(IReadOnlyList<LexiconDocument> baseline)
    {
        var result = new Dictionary<string, int>();

        var changedNsids = Changes
            .Where(c => !c.IsBreaking)
            .Select(c => c.Nsid)
            .Distinct();

        var baseMap = baseline.ToDictionary(d => d.Id, d => d);

        foreach (var nsid in changedNsids)
        {
            var currentRevision = baseMap.TryGetValue(nsid, out var doc)
                ? doc.Revision ?? 1
                : 1;
            result[nsid] = currentRevision + 1;
        }

        return result;
    }
}
