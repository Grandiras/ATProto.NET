using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Migrations;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator;

/// <summary>
/// CLI entry point for the AT Protocol Lexicon code generator.
/// Supports bidirectional generation: Lexicon JSON ↔ C# classes.
/// </summary>
public static class Program
{
    private const string Version = "1.0.0";

    private static readonly JsonSerializerOptions s_parseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        if (args[0] is "-v" or "--version")
        {
            Console.WriteLine($"atproto-lexgen {Version}");
            return 0;
        }

        return args[0] switch
        {
            "csharp" => await RunCSharpCommand(args[1..]),
            "lexicon" => RunLexiconCommand(args[1..]),
            "diff" => await RunDiffCommand(args[1..]),
            "migrate" => await RunMigrateCommand(args[1..]),
            "publish" => await RunPublishCommand(args[1..]),
            _ => Error($"Unknown command: '{args[0]}'. Run with --help for usage."),
        };
    }

    /// <summary>
    /// Generates C# source files from Lexicon JSON schema files.
    /// </summary>
    private static async Task<int> RunCSharpCommand(string[] args)
    {
        string? inputDir = null;
        string? outputDir = null;
        string namespacePrefix = "ATProtoNet.Lexicon";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    inputDir = args[++i];
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--namespace" or "-n" when i + 1 < args.Length:
                    namespacePrefix = args[++i];
                    break;
                case "--help" or "-h":
                    PrintCSharpHelp();
                    return 0;
                default:
                    return Error($"Unknown option: '{args[i]}'");
            }
        }

        if (inputDir is null)
            return Error("--input is required. Specify the directory containing Lexicon .json files.");
        if (outputDir is null)
            return Error("--output is required. Specify the output directory for generated C# files.");
        if (!Directory.Exists(inputDir))
            return Error($"Input directory not found: {inputDir}");

        // Find all .json files in the input directory (recursively)
        var jsonFiles = Directory.GetFiles(inputDir, "*.json", SearchOption.AllDirectories);
        if (jsonFiles.Length == 0)
            return Error($"No .json files found in: {inputDir}");

        Console.WriteLine($"Found {jsonFiles.Length} Lexicon schema file(s) in: {inputDir}");

        // Parse all documents
        var documents = new List<LexiconDocument>();
        var parseErrors = 0;

        foreach (var file in jsonFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var doc = JsonSerializer.Deserialize<LexiconDocument>(json, s_parseOptions);

                if (doc is null || string.IsNullOrEmpty(doc.Id))
                {
                    Console.Error.WriteLine($"  SKIP  {file} (not a valid Lexicon document)");
                    continue;
                }

                // Verify it's a Lexicon v1 document
                if (doc.Lexicon != 1)
                {
                    Console.Error.WriteLine($"  SKIP  {file} (unsupported lexicon version: {doc.Lexicon})");
                    continue;
                }

                documents.Add(doc);
                Console.WriteLine($"  OK    {doc.Id}");
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"  ERROR {file}: {ex.Message}");
                parseErrors++;
            }
        }

        if (parseErrors > 0)
            Console.Error.WriteLine($"\n{parseErrors} file(s) had parse errors.");

        if (documents.Count == 0)
            return Error("No valid Lexicon documents found.");

        // Generate C# code
        var emitter = new CSharpEmitter(namespacePrefix);
        var files = emitter.EmitAll(documents);

        // Write output files
        Directory.CreateDirectory(outputDir);
        var written = 0;

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(outputDir, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, content);
            Console.WriteLine($"  WRITE {relativePath}");
            written++;
        }

        Console.WriteLine($"\nGenerated {written} C# file(s) in: {outputDir}");
        return 0;
    }

    /// <summary>
    /// Generates Lexicon JSON schema files from a compiled .NET assembly.
    /// </summary>
    private static int RunLexiconCommand(string[] args)
    {
        string? assemblyPath = null;
        string? outputDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assembly" or "-a" when i + 1 < args.Length:
                    assemblyPath = args[++i];
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--help" or "-h":
                    PrintLexiconHelp();
                    return 0;
                default:
                    return Error($"Unknown option: '{args[i]}'");
            }
        }

        if (assemblyPath is null)
            return Error("--assembly is required. Specify the path to a compiled .NET assembly.");
        if (outputDir is null)
            return Error("--output is required. Specify the output directory for Lexicon .json files.");
        if (!File.Exists(assemblyPath))
            return Error($"Assembly not found: {assemblyPath}");

        Console.WriteLine($"Analyzing assembly: {assemblyPath}");

        // Load assembly and generate Lexicon schemas
        var emitter = new LexiconEmitter();

        List<(string Nsid, string JsonContent)> schemas;
        try
        {
            schemas = emitter.EmitFromAssembly(assemblyPath);
        }
        catch (Exception ex)
        {
            return Error($"Failed to load assembly: {ex.Message}");
        }

        if (schemas.Count == 0)
        {
            Console.WriteLine("No AT Protocol types found in the assembly.");
            Console.WriteLine("Types must have a property with [JsonPropertyName(\"$type\")] to be detected.");
            return 0;
        }

        // Write output files
        Directory.CreateDirectory(outputDir);

        foreach (var (nsid, json) in schemas)
        {
            // Convert NSID to file path: app.bsky.feed.post → app/bsky/feed/post.json
            var relativePath = nsid.Replace('.', '/') + ".json";
            var fullPath = Path.Combine(outputDir, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, json);
            Console.WriteLine($"  WRITE {relativePath}");
        }

        Console.WriteLine($"\nGenerated {schemas.Count} Lexicon schema(s) in: {outputDir}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            atproto-lexgen {Version} — AT Protocol Lexicon Code Generator

            Bidirectional code generation between AT Protocol Lexicon JSON schemas and C# classes.

            USAGE:
                atproto-lexgen <command> [options]

            COMMANDS:
                csharp      Generate C# source files from Lexicon JSON schemas
                lexicon     Generate Lexicon JSON schemas from a compiled .NET assembly
                diff        Compare Lexicon schemas and detect breaking changes
                migrate     Apply schema migrations to JSON records
                publish     Publish Lexicon schemas with version tracking and diff validation

            OPTIONS:
                -h, --help      Show help
                -v, --version   Show version

            EXAMPLES:
                # Generate C# from official Bluesky Lexicon schemas
                atproto-lexgen csharp --input ./lexicons --output ./src/Generated

                # Generate Lexicon JSON from your custom C# record types
                atproto-lexgen lexicon --assembly ./bin/Debug/net10.0/MyApp.dll --output ./lexicons

                # Check for breaking changes between schema versions
                atproto-lexgen diff --baseline ./lexicons-v1 --current ./lexicons-v2

            Run 'atproto-lexgen <command> --help' for command-specific options.
            """);
    }

    private static void PrintCSharpHelp()
    {
        Console.WriteLine("""
            atproto-lexgen csharp — Generate C# from Lexicon JSON

            Reads AT Protocol Lexicon .json schema files and generates strongly-typed
            C# classes with [JsonPropertyName] attributes and proper $type discriminators.

            OPTIONS:
                -i, --input <dir>         Directory containing Lexicon .json files (required)
                -o, --output <dir>        Output directory for generated .cs files (required)
                -n, --namespace <prefix>  C# namespace prefix (default: ATProtoNet.Lexicon)

            Generated types follow ATProtoNet SDK conventions:
              - sealed classes with init-only properties
              - required keyword for non-optional fields
              - [JsonPropertyName] on all properties
              - $type expression-body property for record types

            EXAMPLE:
                atproto-lexgen csharp \
                  --input ./lexicons/app/bsky \
                  --output ./src/ATProtoNet/Lexicon \
                  --namespace ATProtoNet.Lexicon
            """);
    }

    private static void PrintLexiconHelp()
    {
        Console.WriteLine("""
            atproto-lexgen lexicon — Generate Lexicon JSON from C# assembly

            Loads a compiled .NET assembly and generates AT Protocol Lexicon JSON
            schema files for all types that have a [JsonPropertyName("$type")] property.

            OPTIONS:
                -a, --assembly <path>  Path to the .NET assembly (.dll) to analyze (required)
                -o, --output <dir>     Output directory for generated .json files (required)

            The tool detects AT Protocol types by looking for properties annotated with
            [JsonPropertyName("$type")] and reads their constant value to determine the NSID.

            EXAMPLE:
                atproto-lexgen lexicon \
                  --assembly ./bin/Debug/net10.0/MyApp.dll \
                  --output ./lexicons
            """);
    }

    /// <summary>
    /// Compares two sets of Lexicon schemas and reports breaking changes.
    /// Supports directory-to-directory or assembly-to-directory comparison.
    /// </summary>
    private static async Task<int> RunDiffCommand(string[] args)
    {
        string? baselineDir = null;
        string? currentDir = null;
        string? currentAssembly = null;
        var strict = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline" or "-b" when i + 1 < args.Length:
                    baselineDir = args[++i];
                    break;
                case "--current" or "-c" when i + 1 < args.Length:
                    currentDir = args[++i];
                    break;
                case "--assembly" or "-a" when i + 1 < args.Length:
                    currentAssembly = args[++i];
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--help" or "-h":
                    PrintDiffHelp();
                    return 0;
                default:
                    return Error($"Unknown option: '{args[i]}'");
            }
        }

        if (baselineDir is null)
            return Error("--baseline is required. Specify the directory containing baseline Lexicon .json files.");
        if (currentDir is null && currentAssembly is null)
            return Error("Either --current (directory) or --assembly (DLL) is required for the new schemas.");
        if (!Directory.Exists(baselineDir))
            return Error($"Baseline directory not found: {baselineDir}");

        // Parse baseline schemas
        var baselineDocs = await ParseDirectory(baselineDir);
        if (baselineDocs.Count == 0)
            return Error($"No valid Lexicon documents found in baseline: {baselineDir}");

        Console.WriteLine($"Baseline: {baselineDocs.Count} schema(s) from {baselineDir}");

        // Parse current schemas (from directory or assembly)
        List<LexiconDocument> currentDocs;

        if (currentAssembly is not null)
        {
            if (!File.Exists(currentAssembly))
                return Error($"Assembly not found: {currentAssembly}");

            Console.WriteLine($"Current:  analyzing assembly {currentAssembly}");

            var emitter = new LexiconEmitter();
            var schemas = emitter.EmitFromAssembly(currentAssembly);
            currentDocs = [];

            foreach (var (_, json) in schemas)
            {
                var doc = JsonSerializer.Deserialize<LexiconDocument>(json, s_parseOptions);
                if (doc is not null && !string.IsNullOrEmpty(doc.Id))
                    currentDocs.Add(doc);
            }
        }
        else
        {
            if (!Directory.Exists(currentDir))
                return Error($"Current directory not found: {currentDir}");

            currentDocs = await ParseDirectory(currentDir!);
        }

        if (currentDocs.Count == 0)
            return Error("No valid Lexicon documents found in current schemas.");

        Console.WriteLine($"Current:  {currentDocs.Count} schema(s)");
        Console.WriteLine();

        // Run diff
        var differ = new LexiconDiffer();
        var result = differ.Compare(baselineDocs, currentDocs);

        Console.WriteLine(result.ToReport());

        // Suggest revision bumps
        if (result.HasChanges && !result.HasBreakingChanges)
        {
            var suggestions = result.SuggestRevisions(baselineDocs);
            if (suggestions.Count > 0)
            {
                Console.WriteLine("Suggested revision bumps:");
                foreach (var (nsid, rev) in suggestions.OrderBy(s => s.Key))
                    Console.WriteLine($"  {nsid}: revision → {rev}");
            }
        }

        // Exit code: 0 = no changes or non-breaking, 1 = breaking (when --strict)
        return strict && result.HasBreakingChanges ? 1 : 0;
    }

    /// <summary>Parses all .json files in a directory into LexiconDocuments.</summary>
    private static async Task<List<LexiconDocument>> ParseDirectory(string dir)
    {
        var documents = new List<LexiconDocument>();
        var jsonFiles = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);

        foreach (var file in jsonFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var doc = JsonSerializer.Deserialize<LexiconDocument>(json, s_parseOptions);

                if (doc is not null && !string.IsNullOrEmpty(doc.Id) && doc.Lexicon == 1)
                    documents.Add(doc);
            }
            catch (JsonException)
            {
                // Skip invalid files silently in diff mode
            }
        }

        return documents;
    }

    private static void PrintDiffHelp()
    {
        Console.WriteLine("""
            atproto-lexgen diff — Compare Lexicon schemas for breaking changes

            Compares a baseline set of Lexicon schemas against current schemas and
            reports any changes. Enforces AT Protocol schema evolution rules.

            OPTIONS:
                -b, --baseline <dir>    Directory of baseline Lexicon .json files (required)
                -c, --current <dir>     Directory of current Lexicon .json files
                -a, --assembly <path>   Or: path to assembly to derive current schemas from
                --strict                Exit with code 1 if breaking changes are detected

            Provide either --current (directory) or --assembly (DLL), not both.

            BREAKING CHANGES (will fail with --strict):
                - Removing a schema or definition
                - Removing a property
                - Changing a property type
                - Making a property required
                - Adding a new required property
                - Tightening string constraints

            NON-BREAKING CHANGES:
                - Adding a new schema or definition
                - Adding an optional property
                - Making a property optional
                - Loosening constraints

            EXAMPLES:
                # Compare two directories of schemas
                atproto-lexgen diff --baseline ./lexicons-v1 --current ./lexicons-v2

                # Compare baseline schemas against a live assembly
                atproto-lexgen diff --baseline ./lexicons --assembly ./bin/MyApp.dll

                # Fail in CI if breaking changes detected
                atproto-lexgen diff --baseline ./lexicons --current ./lexicons-new --strict
            """);
    }

    /// <summary>
    /// Applies schema migrations to JSON records and writes the output.
    /// Reads a migration plan (JSON file mapping NSID revisions to transforms)
    /// and applies them to input records.
    /// </summary>
    private static async Task<int> RunMigrateCommand(string[] args)
    {
        string? inputFile = null;
        string? outputFile = null;
        string? nsid = null;
        int? fromRevision = null;
        int? toRevision = null;
        string? migrationsDir = null;
        string? baselineDir = null;
        string? currentDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    inputFile = args[++i];
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outputFile = args[++i];
                    break;
                case "--nsid" when i + 1 < args.Length:
                    nsid = args[++i];
                    break;
                case "--from" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out var from))
                        return Error("--from must be an integer.");
                    fromRevision = from;
                    break;
                case "--to" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out var to))
                        return Error("--to must be an integer.");
                    toRevision = to;
                    break;
                case "--baseline" or "-b" when i + 1 < args.Length:
                    baselineDir = args[++i];
                    break;
                case "--current" or "-c" when i + 1 < args.Length:
                    currentDir = args[++i];
                    break;
                case "--migrations" or "-m" when i + 1 < args.Length:
                    migrationsDir = args[++i];
                    break;
                case "--help" or "-h":
                    PrintMigrateHelp();
                    return 0;
                default:
                    return Error($"Unknown option: '{args[i]}'");
            }
        }

        // Scaffold mode: generate migration stubs from diff
        if (baselineDir is not null && currentDir is not null)
        {
            if (!Directory.Exists(baselineDir))
                return Error($"Baseline directory not found: {baselineDir}");
            if (!Directory.Exists(currentDir))
                return Error($"Current directory not found: {currentDir}");

            var baselineDocs = await ParseDirectory(baselineDir);
            var currentDocs = await ParseDirectory(currentDir);

            if (baselineDocs.Count == 0)
                return Error("No valid Lexicon documents found in baseline.");
            if (currentDocs.Count == 0)
                return Error("No valid Lexicon documents found in current.");

            var differ = new LexiconDiffer();
            var diff = differ.Compare(baselineDocs, currentDocs);

            if (!diff.HasChanges)
            {
                Console.WriteLine("No changes detected between baseline and current schemas.");
                return 0;
            }

            Console.WriteLine(diff.ToReport());

            var revisions = diff.SuggestRevisions(baselineDocs);
            var targetRev = revisions.Values.DefaultIfEmpty(2).Max();
            var scaffolds = LexiconMigrationRunner.ScaffoldFromDiff(diff, baselineDocs, targetRev);

            Console.WriteLine($"\nScaffolded {scaffolds.Count} migration(s):");
            foreach (var m in scaffolds)
                Console.WriteLine($"  {m.Nsid}: rev {m.FromRevision} → {m.ToRevision} — {m.Description}");

            return 0;
        }

        // Run mode: apply migrations to records
        if (inputFile is null)
            return Error("--input is required. Specify the file containing JSON records.");
        if (nsid is null)
            return Error("--nsid is required. Specify the NSID of the records.");
        if (fromRevision is null)
            return Error("--from is required. Specify the source revision.");
        if (toRevision is null)
            return Error("--to is required. Specify the target revision.");
        if (!File.Exists(inputFile))
            return Error($"Input file not found: {inputFile}");

        // Read records (one JSON object per line, or a JSON array)
        var inputText = await File.ReadAllTextAsync(inputFile);
        var records = ParseRecords(inputText);
        if (records.Count == 0)
            return Error("No valid JSON records found in input file.");

        Console.WriteLine($"Loaded {records.Count} record(s) for migration.");

        // If a migrations directory is provided, load migration scripts
        // Otherwise, the runner has no migrations and will fail to build a chain
        var runner = new LexiconMigrationRunner();

        if (migrationsDir is not null && Directory.Exists(migrationsDir))
        {
            var migrationFiles = Directory.GetFiles(migrationsDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var file in migrationFiles)
            {
                try
                {
                    var migrationJson = await File.ReadAllTextAsync(file);
                    var migration = ParseMigrationFile(migrationJson);
                    if (migration is not null)
                        runner.AddMigration(migration);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  SKIP  {file}: {ex.Message}");
                }
            }
        }

        if (!runner.CanMigrate(nsid, fromRevision.Value, toRevision.Value))
            return Error($"No valid migration chain for '{nsid}' from revision {fromRevision} to {toRevision}.");

        var result = runner.Migrate(nsid, fromRevision.Value, toRevision.Value, records);

        if (result.HasErrors)
        {
            Console.Error.WriteLine($"\n{result.FailureCount} record(s) failed migration:");
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"  Record {error.RecordIndex}: {error.Message}");
        }

        Console.WriteLine($"Migrated {result.SuccessCount}/{records.Count} record(s).");

        // Write output
        if (outputFile is not null)
        {
            var outputJson = "[\n" + string.Join(",\n", result.MigratedRecords) + "\n]";
            await File.WriteAllTextAsync(outputFile, outputJson);
            Console.WriteLine($"Output written to: {outputFile}");
        }
        else
        {
            foreach (var rec in result.MigratedRecords)
                Console.WriteLine(rec);
        }

        return result.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Publishes Lexicon schemas to an output directory with diff validation and revision bumping.
    /// </summary>
    private static async Task<int> RunPublishCommand(string[] args)
    {
        string? inputDir = null;
        string? assemblyPath = null;
        string? outputDir = null;
        string? baselineDir = null;
        var autoBump = true;
        var force = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" or "-i" when i + 1 < args.Length:
                    inputDir = args[++i];
                    break;
                case "--assembly" or "-a" when i + 1 < args.Length:
                    assemblyPath = args[++i];
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--baseline" or "-b" when i + 1 < args.Length:
                    baselineDir = args[++i];
                    break;
                case "--no-bump":
                    autoBump = false;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--help" or "-h":
                    PrintPublishHelp();
                    return 0;
                default:
                    return Error($"Unknown option: '{args[i]}'");
            }
        }

        if (outputDir is null)
            return Error("--output is required. Specify the target directory for published schemas.");
        if (inputDir is null && assemblyPath is null)
            return Error("Either --input (directory) or --assembly (DLL) is required.");

        var publisher = new LexiconPublisher();
        PublishResult result;

        if (assemblyPath is not null)
        {
            if (!File.Exists(assemblyPath))
                return Error($"Assembly not found: {assemblyPath}");

            Console.WriteLine($"Publishing from assembly: {assemblyPath}");
            result = await publisher.PublishFromAssemblyAsync(assemblyPath, outputDir, baselineDir, autoBump, !force);
        }
        else
        {
            if (!Directory.Exists(inputDir))
                return Error($"Input directory not found: {inputDir}");

            var documents = await LexiconPublisher.LoadFromDirectoryAsync(inputDir!);
            if (documents.Count == 0)
                return Error("No valid Lexicon documents found in input directory.");

            Console.WriteLine($"Publishing {documents.Count} schema(s) from: {inputDir}");
            result = await publisher.PublishAsync(documents, outputDir, baselineDir, autoBump, !force);
        }

        if (result.HasErrors)
        {
            Console.Error.WriteLine("Publish failed:");
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"  {error}");
            return 1;
        }

        if (result.Diff is not null)
        {
            Console.WriteLine();
            Console.WriteLine(result.Diff.ToReport());
        }

        if (result.SuggestedRevisions is not null && result.SuggestedRevisions.Count > 0)
        {
            Console.WriteLine("Applied revision bumps:");
            foreach (var (revNsid, rev) in result.SuggestedRevisions.OrderBy(kv => kv.Key))
                Console.WriteLine($"  {revNsid}: revision → {rev}");
        }

        Console.WriteLine($"\nPublished: {result.WrittenNsids.Count} schema(s)");
        if (result.SkippedNsids.Count > 0)
            Console.WriteLine($"Skipped (unchanged): {result.SkippedNsids.Count}");

        foreach (var id in result.WrittenNsids)
            Console.WriteLine($"  WRITE {id}");

        return 0;
    }

    /// <summary>Parses JSON records from text (array or newline-delimited).</summary>
    private static List<string> ParseRecords(string text)
    {
        var trimmed = text.Trim();
        var records = new List<string>();

        if (trimmed.StartsWith('['))
        {
            // JSON array
            try
            {
                var array = JsonNode.Parse(trimmed) as JsonArray;
                if (array is not null)
                {
                    foreach (var item in array)
                    {
                        if (item is not null)
                            records.Add(item.ToJsonString());
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to line-by-line
            }
        }

        if (records.Count > 0)
            return records;

        // Newline-delimited JSON
        foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                // Verify it's valid JSON
                JsonNode.Parse(line);
                records.Add(line);
            }
            catch (JsonException)
            {
                // Skip invalid lines
            }
        }

        return records;
    }

    /// <summary>
    /// Parses a migration definition file. Format:
    /// { "nsid": "...", "fromRevision": 1, "toRevision": 2, "operations": [...] }
    /// Operations: { "op": "addProperty", "name": "...", "default": ... }
    ///             { "op": "removeProperty", "name": "..." }
    ///             { "op": "renameProperty", "from": "...", "to": "..." }
    /// </summary>
    private static ILexiconMigration? ParseMigrationFile(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            return null;

        var migNsid = obj["nsid"]?.GetValue<string>();
        var from = obj["fromRevision"]?.GetValue<int>();
        var to = obj["toRevision"]?.GetValue<int>();
        var ops = obj["operations"] as JsonArray;

        if (migNsid is null || from is null || to is null || ops is null)
            return null;

        var builder = new MigrationBuilder(migNsid, from.Value, to.Value);

        foreach (var op in ops)
        {
            if (op is not JsonObject opObj)
                continue;

            var opType = opObj["op"]?.GetValue<string>();
            switch (opType)
            {
                case "addProperty":
                    var addName = opObj["name"]?.GetValue<string>();
                    if (addName is not null)
                        builder.AddProperty(addName, opObj["default"]?.DeepClone());
                    break;

                case "removeProperty":
                    var removeName = opObj["name"]?.GetValue<string>();
                    if (removeName is not null)
                        builder.RemoveProperty(removeName);
                    break;

                case "renameProperty":
                    var renameFrom = opObj["from"]?.GetValue<string>();
                    var renameTo = opObj["to"]?.GetValue<string>();
                    if (renameFrom is not null && renameTo is not null)
                        builder.RenameProperty(renameFrom, renameTo);
                    break;
            }
        }

        var description = obj["description"]?.GetValue<string>();
        if (description is not null)
            builder.WithDescription(description);

        return builder.Build();
    }

    private static void PrintMigrateHelp()
    {
        Console.WriteLine("""
            atproto-lexgen migrate — Apply schema migrations to records

            Transforms JSON records from one schema revision to another using a
            chain of migration steps. Can also scaffold migrations from schema diffs.

            MODES:
                Scaffold mode (--baseline + --current):
                  Compares two schema directories and generates migration stubs.

                Run mode (--input + --nsid + --from + --to):
                  Applies migrations to JSON records.

            OPTIONS:
                -i, --input <file>       JSON file containing records to migrate
                -o, --output <file>      Output file for migrated records (default: stdout)
                --nsid <nsid>            NSID of the records
                --from <revision>        Source schema revision
                --to <revision>          Target schema revision
                -m, --migrations <dir>   Directory containing migration .json files
                -b, --baseline <dir>     Baseline schema directory (scaffold mode)
                -c, --current <dir>      Current schema directory (scaffold mode)

            MIGRATION FILE FORMAT:
                {
                  "nsid": "com.example.post",
                  "fromRevision": 1,
                  "toRevision": 2,
                  "description": "Add tags field",
                  "operations": [
                    { "op": "addProperty", "name": "tags", "default": [] },
                    { "op": "removeProperty", "name": "legacy" },
                    { "op": "renameProperty", "from": "old", "to": "new" }
                  ]
                }

            EXAMPLES:
                # Scaffold migrations from schema diff
                atproto-lexgen migrate --baseline ./v1 --current ./v2

                # Apply migrations to records
                atproto-lexgen migrate \
                  --input records.json --output migrated.json \
                  --nsid com.example.post --from 1 --to 2 \
                  --migrations ./migrations
            """);
    }

    private static void PrintPublishHelp()
    {
        Console.WriteLine("""
            atproto-lexgen publish — Publish Lexicon schemas with versioning

            Publishes Lexicon schemas to a directory with automatic diff validation
            and revision bumping. Detects breaking changes and prevents publishing
            them unless --force is used.

            OPTIONS:
                -i, --input <dir>       Directory of Lexicon .json files to publish
                -a, --assembly <path>   Or: .NET assembly to derive schemas from
                -o, --output <dir>      Target directory for published schemas (required)
                -b, --baseline <dir>    Existing published schemas for comparison
                --no-bump               Don't auto-bump revisions for changed schemas
                --force                 Publish even if breaking changes are detected

            WORKFLOW:
                1. Develop your schema changes (Lexicon JSON or C# types)
                2. Diff against baseline: atproto-lexgen diff --baseline ./published --current ./dev
                3. Publish with versioning: atproto-lexgen publish -i ./dev -o ./published -b ./published

            EXAMPLES:
                # Publish schemas from directory with baseline comparison
                atproto-lexgen publish \
                  --input ./schemas --output ./published --baseline ./published

                # Publish from a compiled assembly
                atproto-lexgen publish \
                  --assembly ./bin/MyApp.dll --output ./published

                # Force-publish breaking changes
                atproto-lexgen publish \
                  --input ./schemas --output ./published --baseline ./published --force
            """);
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
