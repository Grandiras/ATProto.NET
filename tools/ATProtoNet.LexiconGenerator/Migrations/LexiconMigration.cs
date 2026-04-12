using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATProtoNet.LexiconGenerator.Migrations;

/// <summary>
/// Represents a single schema migration that transforms records from one revision to the next.
/// Migrations are ordered by <see cref="FromRevision"/> → <see cref="ToRevision"/> and can be
/// chained to migrate records across multiple schema versions.
/// </summary>
public interface ILexiconMigration
{
    /// <summary>The NSID of the schema this migration applies to.</summary>
    string Nsid { get; }

    /// <summary>The source revision (1-based). Use 1 for the initial schema version.</summary>
    int FromRevision { get; }

    /// <summary>The target revision after migration.</summary>
    int ToRevision { get; }

    /// <summary>Optional human-readable description of what this migration does.</summary>
    string? Description { get; }

    /// <summary>
    /// Transforms a single record from <see cref="FromRevision"/> to <see cref="ToRevision"/>.
    /// The record is passed as a mutable JSON object. Implementations should modify
    /// the document in-place (add, remove, rename, or transform properties).
    /// </summary>
    /// <param name="record">The JSON record to transform. Modified in-place.</param>
    void Transform(JsonObject record);
}

/// <summary>
/// A concrete migration that uses a delegate for the transform step.
/// </summary>
public sealed class DelegateMigration : ILexiconMigration
{
    private readonly Action<JsonObject> _transform;

    public DelegateMigration(string nsid, int fromRevision, int toRevision, Action<JsonObject> transform, string? description = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(nsid);
        ArgumentNullException.ThrowIfNull(transform);

        if (fromRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(fromRevision), "Revision must be >= 1.");
        if (toRevision <= fromRevision)
            throw new ArgumentOutOfRangeException(nameof(toRevision), "Target revision must be greater than source revision.");

        Nsid = nsid;
        FromRevision = fromRevision;
        ToRevision = toRevision;
        Description = description;
        _transform = transform;
    }

    public string Nsid { get; }
    public int FromRevision { get; }
    public int ToRevision { get; }
    public string? Description { get; }

    public void Transform(JsonObject record) => _transform(record);
}

/// <summary>
/// Builder for constructing migrations fluently.
/// </summary>
public sealed class MigrationBuilder
{
    private readonly string _nsid;
    private readonly int _fromRevision;
    private readonly int _toRevision;
    private readonly List<Action<JsonObject>> _steps = [];
    private string? _description;

    public MigrationBuilder(string nsid, int fromRevision, int toRevision)
    {
        _nsid = nsid;
        _fromRevision = fromRevision;
        _toRevision = toRevision;
    }

    /// <summary>Sets a description for this migration.</summary>
    public MigrationBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Adds a property with a default value.</summary>
    public MigrationBuilder AddProperty(string name, JsonNode? defaultValue)
    {
        _steps.Add(record => record[name] = defaultValue?.DeepClone());
        return this;
    }

    /// <summary>Removes a property.</summary>
    public MigrationBuilder RemoveProperty(string name)
    {
        _steps.Add(record => record.Remove(name));
        return this;
    }

    /// <summary>Renames a property.</summary>
    public MigrationBuilder RenameProperty(string oldName, string newName)
    {
        _steps.Add(record =>
        {
            if (record.TryGetPropertyValue(oldName, out var value))
            {
                record.Remove(oldName);
                record[newName] = value?.DeepClone();
            }
        });
        return this;
    }

    /// <summary>Applies a custom transformation step.</summary>
    public MigrationBuilder Apply(Action<JsonObject> transform)
    {
        _steps.Add(transform);
        return this;
    }

    /// <summary>Builds the migration.</summary>
    public ILexiconMigration Build()
    {
        var steps = _steps.ToList();
        return new DelegateMigration(_nsid, _fromRevision, _toRevision, record =>
        {
            foreach (var step in steps)
                step(record);
        }, _description);
    }
}
