# Lexicon Code Generator

ATProto.NET includes `atproto-lexgen`, a bidirectional `dotnet tool` for working with AT Protocol Lexicon schemas.

## Installation

```bash
dotnet tool install -g ATProtoNet.LexiconGenerator
```

## Commands

| Command | Description |
|---------|-------------|
| `atproto-lexgen csharp` | Generate C# classes from Lexicon JSON schemas |
| `atproto-lexgen lexicon` | Generate Lexicon JSON from .NET assemblies |
| `atproto-lexgen diff` | Compare Lexicon schemas and detect breaking changes |
| `atproto-lexgen migrate` | Scaffold or apply record migrations |
| `atproto-lexgen publish` | Publish schemas with version tracking |

## Generate C# from Lexicons

Generate typed C# record classes from Lexicon JSON schema files:

```bash
atproto-lexgen csharp --input ./lexicons --output ./Generated --namespace MyApp.Lexicon
```

This generates:
- `sealed class` types with `required`/`init` properties
- `[JsonPropertyName]` attributes for JSON serialization
- `$type` expression-body properties
- Support for all Lexicon types: record, object, string enum, token, ref, union, array, blob

### Example Input (Lexicon JSON)

```json
{
  "lexicon": 1,
  "id": "com.example.todo.item",
  "defs": {
    "main": {
      "type": "record",
      "key": "tid",
      "record": {
        "type": "object",
        "required": ["title"],
        "properties": {
          "title": { "type": "string", "maxLength": 256 },
          "completed": { "type": "boolean" },
          "priority": { "type": "integer", "minimum": 0, "maximum": 5 },
          "dueDate": { "type": "string", "format": "datetime" },
          "tags": { "type": "array", "items": { "type": "string" } }
        }
      }
    }
  }
}
```

### Example Output (C#)

```csharp
using System.Text.Json.Serialization;

namespace MyApp.Lexicon.Com.Example.Todo;

public sealed class Item
{
    [JsonPropertyName("$type")]
    public string Type => "com.example.todo.item";

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("completed")]
    public bool? Completed { get; init; }

    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; init; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }
}
```

## Generate Lexicons from C# Assemblies

Reverse-generate Lexicon JSON schemas from compiled .NET types:

```bash
atproto-lexgen lexicon --assembly ./bin/Debug/net10.0/MyApp.dll --output ./lexicons
```

## Compare Schemas (Diff)

Compare a baseline schema against the current version to detect breaking changes:

```bash
atproto-lexgen diff --baseline ./lexicons-v1 --current ./lexicons-v2
```

The diff detects:
- Added/removed properties
- Type changes
- Required status changes
- Constraint tightening (breaking changes per AT Protocol evolution rules)

### CI Integration

Use `--strict` mode for CI pipelines — exits with code 1 on breaking changes:

```bash
atproto-lexgen diff --baseline ./baseline --current ./current --strict
```

## Schema Migrations

### Scaffold a Migration

Generate a migration file from the diff between two schema versions:

```bash
atproto-lexgen migrate scaffold --baseline ./v1 --current ./v2 --output ./migrations/001.json
```

### Migration File Format

```json
{
  "version": 1,
  "lexiconId": "com.example.todo.item",
  "operations": [
    { "type": "addProperty", "name": "tags", "default": [] },
    { "type": "removeProperty", "name": "oldField" },
    { "type": "renameProperty", "from": "dueDate", "to": "deadline" }
  ]
}
```

### Apply Migrations

Apply a migration file to transform JSON records:

```bash
atproto-lexgen migrate apply --migration ./migrations/001.json --input ./records
```

### Programmatic Migrations

Use the migration API in code:

```csharp
using ATProtoNet.Lexicon;

// Fluent migration builder
var migration = new MigrationBuilder()
    .AddProperty("tags", defaultValue: new string[] { })
    .RemoveProperty("oldField")
    .RenameProperty("dueDate", "deadline")
    .Build();

// Apply to a JSON record
var migrated = migration.Apply(originalRecord);
```

### Migration Runner

```csharp
var runner = new LexiconMigrationRunner();

// Register migrations in order
runner.AddMigration(new DelegateMigration(1, record =>
{
    record["tags"] = new JsonArray();
    return record;
}));

// Run all migrations
var result = runner.Run(record, fromRevision: 0);
```

## Publishing Schemas

Publish schemas to a directory with version tracking:

```bash
# Publish with automatic revision bump for non-breaking changes
atproto-lexgen publish --schema ./lexicons --directory ./published

# Force publish even with breaking changes
atproto-lexgen publish --schema ./lexicons --directory ./published --force

# Publish without bumping the revision
atproto-lexgen publish --schema ./lexicons --directory ./published --no-bump
```

The publisher:
- Validates against the baseline diff
- Auto-increments revision numbers for non-breaking changes
- Detects and warns on breaking changes
- Tracks versions in the published directory

## Lexicon Plugin Packages

If you're distributing your Lexicon types as a NuGet package, combine the code generator with the [plugin system](custom-records.md#distributing-lexicons-as-nuget-packages):

1. Generate C# types from your Lexicon schemas
2. Implement `ILexiconPlugin` to register the types
3. Mark the assembly with `[LexiconPlugin]`
4. Package and distribute via NuGet

## Next Steps

- [Custom Lexicon Records](custom-records.md) — Use generated types with RecordCollection\<T\>
- [Low-Level Repo API](low-level-repo.md) — MST, DAG-CBOR, and CAR file support
