using System.Text.Json;
using ATProtoNet.LexiconGenerator.CodeGen;
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

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
