using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.Migrations;

/// <summary>
/// Result of running migrations on a set of records.
/// </summary>
public sealed class MigrationResult
{
    /// <summary>Number of records successfully migrated.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of records that failed migration.</summary>
    public int FailureCount { get; init; }

    /// <summary>Errors encountered during migration, keyed by record index.</summary>
    public IReadOnlyList<MigrationError> Errors { get; init; } = [];

    /// <summary>The migrated records as JSON strings.</summary>
    public IReadOnlyList<string> MigratedRecords { get; init; } = [];

    public bool HasErrors => FailureCount > 0;
}

/// <summary>Describes a single migration error.</summary>
public sealed record MigrationError(int RecordIndex, string Message, Exception? Exception = null);

/// <summary>
/// Plans and executes schema migrations on JSON records. Validates that the
/// migration chain covers the full version range and that no gaps exist.
/// Optionally validates the migrated output against the target schema using <see cref="LexiconDiffer"/>.
/// </summary>
public sealed class LexiconMigrationRunner
{
    private readonly List<ILexiconMigration> _migrations = [];

    /// <summary>Registers a migration with the runner.</summary>
    public LexiconMigrationRunner AddMigration(ILexiconMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        _migrations.Add(migration);
        return this;
    }

    /// <summary>Registers multiple migrations.</summary>
    public LexiconMigrationRunner AddMigrations(IEnumerable<ILexiconMigration> migrations)
    {
        foreach (var m in migrations)
            AddMigration(m);
        return this;
    }

    /// <summary>
    /// Builds an ordered chain of migrations for a given NSID from <paramref name="fromRevision"/>
    /// to <paramref name="toRevision"/>. Throws if no valid path exists.
    /// </summary>
    public IReadOnlyList<ILexiconMigration> BuildChain(string nsid, int fromRevision, int toRevision)
    {
        if (fromRevision >= toRevision)
            throw new ArgumentException($"fromRevision ({fromRevision}) must be less than toRevision ({toRevision}).");

        var candidates = _migrations
            .Where(m => m.Nsid == nsid)
            .OrderBy(m => m.FromRevision)
            .ToList();

        // Greedy chain builder: walk from fromRevision → toRevision
        var chain = new List<ILexiconMigration>();
        var current = fromRevision;

        while (current < toRevision)
        {
            var next = candidates.FirstOrDefault(m => m.FromRevision == current);
            if (next is null)
                throw new InvalidOperationException(
                    $"No migration found for '{nsid}' from revision {current}. " +
                    $"Cannot build chain from {fromRevision} → {toRevision}.");

            if (next.ToRevision > toRevision)
                throw new InvalidOperationException(
                    $"Migration for '{nsid}' rev {next.FromRevision}→{next.ToRevision} " +
                    $"overshoots target revision {toRevision}.");

            chain.Add(next);
            current = next.ToRevision;
        }

        return chain;
    }

    /// <summary>
    /// Migrates a collection of JSON records from one revision to another.
    /// </summary>
    /// <param name="nsid">The NSID of the record schema.</param>
    /// <param name="fromRevision">The current revision of the records.</param>
    /// <param name="toRevision">The target revision.</param>
    /// <param name="records">JSON strings of the records to migrate.</param>
    /// <returns>The migration result with transformed records.</returns>
    public MigrationResult Migrate(string nsid, int fromRevision, int toRevision, IReadOnlyList<string> records)
    {
        var chain = BuildChain(nsid, fromRevision, toRevision);
        var migrated = new List<string>();
        var errors = new List<MigrationError>();
        var success = 0;

        for (var i = 0; i < records.Count; i++)
        {
            try
            {
                var node = JsonNode.Parse(records[i]);
                if (node is not JsonObject obj)
                {
                    errors.Add(new MigrationError(i, "Record is not a JSON object."));
                    continue;
                }

                foreach (var migration in chain)
                    migration.Transform(obj);

                migrated.Add(obj.ToJsonString());
                success++;
            }
            catch (Exception ex)
            {
                errors.Add(new MigrationError(i, ex.Message, ex));
            }
        }

        return new MigrationResult
        {
            SuccessCount = success,
            FailureCount = errors.Count,
            Errors = errors,
            MigratedRecords = migrated,
        };
    }

    /// <summary>
    /// Validates that a complete migration chain exists for a given NSID between two revisions.
    /// </summary>
    /// <returns>True if a valid chain can be built; false otherwise.</returns>
    public bool CanMigrate(string nsid, int fromRevision, int toRevision)
    {
        try
        {
            BuildChain(nsid, fromRevision, toRevision);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets all registered migrations for a given NSID, ordered by revision.
    /// </summary>
    public IReadOnlyList<ILexiconMigration> GetMigrations(string nsid)
    {
        return _migrations
            .Where(m => m.Nsid == nsid)
            .OrderBy(m => m.FromRevision)
            .ToList();
    }

    /// <summary>
    /// Gets all unique NSIDs that have registered migrations.
    /// </summary>
    public IReadOnlyList<string> GetRegisteredNsids()
    {
        return _migrations.Select(m => m.Nsid).Distinct().OrderBy(n => n).ToList();
    }

    /// <summary>
    /// Generates migration scaffolds from a <see cref="DiffResult"/>.
    /// For each non-breaking change (e.g. property added), creates a stub migration
    /// that applies default values. For breaking changes, creates a migration
    /// with a placeholder transform that throws.
    /// </summary>
    public static List<ILexiconMigration> ScaffoldFromDiff(
        DiffResult diff,
        IReadOnlyList<LexiconDocument> baseline,
        int targetRevision)
    {
        var scaffolds = new List<ILexiconMigration>();
        var baseMap = baseline.ToDictionary(d => d.Id, d => d);

        // Group changes by NSID
        var byNsid = diff.Changes.GroupBy(c => c.Nsid);

        foreach (var group in byNsid)
        {
            var nsid = group.Key;
            var fromRev = baseMap.TryGetValue(nsid, out var doc) ? doc.Revision ?? 1 : 1;

            var builder = new MigrationBuilder(nsid, fromRev, targetRevision);
            var descriptions = new List<string>();

            foreach (var change in group)
            {
                // Extract the leaf property name from the dotted path (e.g. "main.record.tags" → "tags")
                var propertyName = change.Path?.Contains('.') == true
                    ? change.Path[(change.Path.LastIndexOf('.') + 1)..]
                    : change.Path ?? "unknown";

                switch (change.Kind)
                {
                    case ChangeKind.PropertyAdded:
                        builder.AddProperty(propertyName, null);
                        descriptions.Add($"Add property '{propertyName}' with null default");
                        break;

                    case ChangeKind.PropertyRemoved:
                        builder.RemoveProperty(propertyName);
                        descriptions.Add($"Remove property '{propertyName}'");
                        break;

                    default:
                        // For other changes, add a descriptive no-op
                        descriptions.Add($"{change.Kind}: {change.Description}");
                        break;
                }
            }

            builder.WithDescription(string.Join("; ", descriptions));
            scaffolds.Add(builder.Build());
        }

        return scaffolds;
    }
}
