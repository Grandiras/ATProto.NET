using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Publishing;
using ATProtoNet.LexiconGenerator.Schema;
using ATProtoNet.LexiconGenerator.Validation;
using ATProtoNet.Serialization;

namespace ATProtoNet.LexiconGenerator;

/// <summary>
/// What a command runs against: its output streams, the environment, the terminal, and the
/// network. <see cref="Program.Main"/> uses the process's own; tests substitute them.
/// </summary>
internal sealed class CommandContext
{
    /// <summary>Normal output.</summary>
    public required TextWriter Out { get; init; }

    /// <summary>Diagnostics and errors.</summary>
    public required TextWriter Error { get; init; }

    /// <summary>Reads an environment variable.</summary>
    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <summary>
    /// Prompts for a secret without echoing it, or returns <see langword="null"/> when there is no
    /// terminal to ask on.
    /// </summary>
    public Func<string, string?> ReadSecret { get; init; } = _ => null;

    /// <summary>Creates the network access of <c>publish</c> and <c>resolve</c>.</summary>
    public Func<ILexgenNetwork> CreateNetwork { get; init; } = () => new SdkLexgenNetwork();

    /// <summary>The process's own console, environment and network.</summary>
    public static CommandContext ForConsole() => new()
    {
        Out = Console.Out,
        Error = Console.Error,
        ReadSecret = ReadSecretFromConsole,
    };

    private static string? ReadSecretFromConsole(string prompt)
    {
        if (Console.IsInputRedirected)
            return null;

        Console.Error.Write(prompt);
        var secret = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                    secret.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return secret.ToString();
    }
}

/// <summary>
/// CLI entry point for <c>atproto-lexgen</c>: Lexicon JSON ↔ C# generation, linting and diffing,
/// and publishing and resolving schemas on the network.
/// </summary>
public static class Program
{
    /// <summary>Where <c>publish</c> reads the account from when <c>--identifier</c> is not given.</summary>
    internal const string IdentifierVariable = "ATPROTO_IDENTIFIER";

    /// <summary>Where <c>publish</c> reads the (app) password from. It is never taken as an argument.</summary>
    internal const string PasswordVariable = "ATPROTO_PASSWORD";

    /// <summary>Where <c>publish</c> reads the PDS from when <c>--pds</c> is not given.</summary>
    internal const string PdsVariable = "ATPROTO_PDS_URL";

    private static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>Runs the tool against the process's console.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static Task<int> Main(string[] args) => RunAsync(args, CommandContext.ForConsole());

    /// <summary>Runs one command line against <paramref name="context"/>.</summary>
    internal static async Task<int> RunAsync(string[] args, CommandContext context, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp(context.Out);
            return 0;
        }

        if (args[0] is "-v" or "--version")
        {
            context.Out.WriteLine($"atproto-lexgen {Version}");
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "csharp" => await RunCSharpCommand(args[1..], context),
                "lexicon" => RunLexiconCommand(args[1..], context),
                "lint" => RunLintCommand(args[1..], context),
                "diff" => RunDiffCommand(args[1..], context),
                "publish" => await RunPublishCommand(args[1..], context, cancellationToken),
                "resolve" => await RunResolveCommand(args[1..], context, cancellationToken),
                _ => Error(context, $"Unknown command: '{args[0]}'. Run with --help for usage."),
            };
        }
        catch (LexgenException ex)
        {
            return Error(context, ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  csharp
    // ──────────────────────────────────────────────────────────

    /// <summary>Generates C# source files from Lexicon JSON schema files.</summary>
    private static async Task<int> RunCSharpCommand(string[] args, CommandContext context)
    {
        string? inputDir = null;
        string? outputDir = null;
        var namespacePrefix = "ATProtoNet.Lexicon";

        if (ParseOptions(args, context, PrintCSharpHelp, options: new Dictionary<string, Func<string, string?>>
            {
                ["--input"] = v => { inputDir = v; return null; },
                ["-i"] = v => { inputDir = v; return null; },
                ["--output"] = v => { outputDir = v; return null; },
                ["-o"] = v => { outputDir = v; return null; },
                ["--namespace"] = v => { namespacePrefix = v; return null; },
                ["-n"] = v => { namespacePrefix = v; return null; },
            }) is { } exitCode)
        {
            return exitCode;
        }

        if (inputDir is null)
            return Error(context, "--input is required. Specify the directory containing Lexicon .json files.");
        if (outputDir is null)
            return Error(context, "--output is required. Specify the output directory for generated C# files.");
        if (!Directory.Exists(inputDir))
            return Error(context, $"Input directory not found: {inputDir}");

        var load = LexiconLoader.LoadDirectory(inputDir);
        ReportLoad(context, load, verbose: true);

        if (load.Documents.Count == 0)
            return Error(context, $"No valid Lexicon documents found in: {inputDir}");

        var emitter = new CSharpEmitter(namespacePrefix);
        var files = emitter.EmitAll(load.Documents.Select(d => d.Document));
        ReportWarnings(context, emitter.Warnings);

        Directory.CreateDirectory(outputDir);
        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(outputDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content);
            context.Out.WriteLine($"  WRITE {relativePath}");
        }

        context.Out.WriteLine($"\nGenerated {files.Count} C# file(s) in: {outputDir}");
        return 0;
    }

    // ──────────────────────────────────────────────────────────
    //  lexicon
    // ──────────────────────────────────────────────────────────

    /// <summary>Generates Lexicon JSON schema files from a compiled .NET assembly.</summary>
    private static int RunLexiconCommand(string[] args, CommandContext context)
    {
        string? assemblyPath = null;
        string? outputDir = null;

        if (ParseOptions(args, context, PrintLexiconHelp, options: new Dictionary<string, Func<string, string?>>
            {
                ["--assembly"] = v => { assemblyPath = v; return null; },
                ["-a"] = v => { assemblyPath = v; return null; },
                ["--output"] = v => { outputDir = v; return null; },
                ["-o"] = v => { outputDir = v; return null; },
            }) is { } exitCode)
        {
            return exitCode;
        }

        if (assemblyPath is null)
            return Error(context, "--assembly is required. Specify the path to a compiled .NET assembly.");
        if (outputDir is null)
            return Error(context, "--output is required. Specify the output directory for Lexicon .json files.");
        if (!File.Exists(assemblyPath))
            return Error(context, $"Assembly not found: {assemblyPath}");

        context.Out.WriteLine($"Analyzing assembly: {assemblyPath}");

        var emitter = new LexiconEmitter();
        List<(string Nsid, string JsonContent)> schemas;
        try
        {
            schemas = emitter.EmitFromAssembly(assemblyPath);
        }
        catch (Exception ex) when (IsAssemblyLoadFailure(ex))
        {
            return Error(context, $"Failed to load assembly: {ex.Message}");
        }

        ReportWarnings(context, emitter.Warnings);

        if (schemas.Count == 0)
        {
            context.Out.WriteLine("No AT Protocol types found in the assembly.");
            context.Out.WriteLine("Record types must have a property with [JsonPropertyName(\"$type\")] to be detected;");
            context.Out.WriteLine("space types must expose a static SpaceTypeDeclaration alongside an Nsid constant.");
            return 0;
        }

        Directory.CreateDirectory(outputDir);
        foreach (var (nsid, json) in schemas)
        {
            var relativePath = RelativePath(nsid);
            var fullPath = Path.Combine(outputDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
            context.Out.WriteLine($"  WRITE {relativePath}");
        }

        context.Out.WriteLine($"\nGenerated {schemas.Count} Lexicon schema(s) in: {outputDir}");
        return 0;
    }

    // ──────────────────────────────────────────────────────────
    //  lint
    // ──────────────────────────────────────────────────────────

    /// <summary>Checks Lexicon JSON schema files against the Lexicon and permission rules.</summary>
    private static int RunLintCommand(string[] args, CommandContext context)
    {
        string? inputDir = null;
        var strict = false;

        if (ParseOptions(args, context, PrintLintHelp,
            options: new Dictionary<string, Func<string, string?>>
            {
                ["--input"] = v => { inputDir = v; return null; },
                ["-i"] = v => { inputDir = v; return null; },
            },
            flags: new Dictionary<string, Func<string?>>
            {
                ["--strict"] = () => { strict = true; return null; },
            }) is { } exitCode)
        {
            return exitCode;
        }

        if (inputDir is null)
            return Error(context, "--input is required. Specify the directory containing Lexicon .json files.");
        if (!Directory.Exists(inputDir))
            return Error(context, $"Input directory not found: {inputDir}");

        var load = LexiconLoader.LoadDirectory(inputDir);
        ReportLoad(context, load, verbose: false);

        var diagnostics = load.Documents.SelectMany(d => LexiconLinter.Lint(d.Document)).ToList();
        ReportDiagnostics(context.Out, diagnostics);

        var errors = diagnostics.Count(d => d.Severity == LintSeverity.Error) + load.Errors.Count;
        var warnings = diagnostics.Count(d => d.Severity == LintSeverity.Warning);
        context.Out.WriteLine($"\nLinted {load.Documents.Count} schema(s): {errors} error(s), {warnings} warning(s).");

        return errors > 0 || (strict && warnings > 0) ? 1 : 0;
    }

    // ──────────────────────────────────────────────────────────
    //  diff
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compares two sets of Lexicon schemas and reports breaking changes.
    /// Supports directory-to-directory or assembly-to-directory comparison.
    /// </summary>
    private static int RunDiffCommand(string[] args, CommandContext context)
    {
        string? baselineDir = null;
        string? currentDir = null;
        string? currentAssembly = null;
        var strict = false;

        if (ParseOptions(args, context, PrintDiffHelp,
            options: new Dictionary<string, Func<string, string?>>
            {
                ["--baseline"] = v => { baselineDir = v; return null; },
                ["-b"] = v => { baselineDir = v; return null; },
                ["--current"] = v => { currentDir = v; return null; },
                ["-c"] = v => { currentDir = v; return null; },
                ["--assembly"] = v => { currentAssembly = v; return null; },
                ["-a"] = v => { currentAssembly = v; return null; },
            },
            flags: new Dictionary<string, Func<string?>>
            {
                ["--strict"] = () => { strict = true; return null; },
            }) is { } exitCode)
        {
            return exitCode;
        }

        if (baselineDir is null)
            return Error(context, "--baseline is required. Specify the directory containing baseline Lexicon .json files.");
        if (currentDir is null && currentAssembly is null)
            return Error(context, "Either --current (directory) or --assembly (DLL) is required for the new schemas.");
        if (!Directory.Exists(baselineDir))
            return Error(context, $"Baseline directory not found: {baselineDir}");

        var baseline = LexiconLoader.LoadDirectory(baselineDir);
        ReportLoad(context, baseline, verbose: false);
        if (baseline.Documents.Count == 0)
            return Error(context, $"No valid Lexicon documents found in baseline: {baselineDir}");

        context.Out.WriteLine($"Baseline: {baseline.Documents.Count} schema(s) from {baselineDir}");

        LexiconLoadResult current;
        if (currentAssembly is not null)
        {
            if (!File.Exists(currentAssembly))
                return Error(context, $"Assembly not found: {currentAssembly}");

            context.Out.WriteLine($"Current:  analyzing assembly {currentAssembly}");
            var emitter = new LexiconEmitter();
            try
            {
                current = LexiconLoader.LoadAssembly(currentAssembly, emitter);
            }
            catch (Exception ex) when (IsAssemblyLoadFailure(ex))
            {
                return Error(context, $"Failed to load assembly: {ex.Message}");
            }

            ReportWarnings(context, emitter.Warnings);
        }
        else
        {
            if (!Directory.Exists(currentDir))
                return Error(context, $"Current directory not found: {currentDir}");

            current = LexiconLoader.LoadDirectory(currentDir!);
        }

        ReportLoad(context, current, verbose: false);
        if (current.Documents.Count == 0)
            return Error(context, "No valid Lexicon documents found in current schemas.");

        context.Out.WriteLine($"Current:  {current.Documents.Count} schema(s)");
        context.Out.WriteLine();

        var baselineDocs = baseline.Documents.Select(d => d.Document).ToList();
        var result = new LexiconDiffer().Compare(baselineDocs, current.Documents.Select(d => d.Document).ToList());
        context.Out.WriteLine(result.ToReport());

        if (result.HasChanges && !result.HasBreakingChanges)
        {
            var suggestions = result.SuggestRevisions(baselineDocs);
            if (suggestions.Count > 0)
            {
                context.Out.WriteLine("Suggested revision bumps:");
                foreach (var (nsid, rev) in suggestions.OrderBy(s => s.Key, StringComparer.Ordinal))
                    context.Out.WriteLine($"  {nsid}: revision → {rev}");
            }
        }

        // Exit code: 0 = no changes or non-breaking, 1 = breaking (when --strict)
        return strict && result.HasBreakingChanges ? 1 : 0;
    }

    // ──────────────────────────────────────────────────────────
    //  publish
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Publishes Lexicon schemas as <c>com.atproto.lexicon.schema</c> records in the account's
    /// repository, then checks the <c>_lexicon</c> DNS records resolvers will look for.
    /// </summary>
    private static async Task<int> RunPublishCommand(string[] args, CommandContext context, CancellationToken cancellationToken)
    {
        string? inputDir = null;
        string? identifier = null;
        string? pds = null;
        var force = false;

        string? PasswordOnCommandLine() =>
            "Passwords are not taken on the command line, where they end up in shell history and process lists. " +
            $"Set {PasswordVariable} (an app password), or run interactively to be prompted.";

        if (ParseOptions(args, context, PrintPublishHelp,
            options: new Dictionary<string, Func<string, string?>>
            {
                ["--input"] = v => { inputDir = v; return null; },
                ["-i"] = v => { inputDir = v; return null; },
                ["--identifier"] = v => { identifier = v; return null; },
                ["-u"] = v => { identifier = v; return null; },
                ["--pds"] = v => { pds = v; return null; },
            },
            flags: new Dictionary<string, Func<string?>>
            {
                ["--force"] = () => { force = true; return null; },
                ["--password"] = PasswordOnCommandLine,
                ["-p"] = PasswordOnCommandLine,
            }) is { } exitCode)
        {
            return exitCode;
        }

        if (inputDir is null)
            return Error(context, "--input is required. Specify the directory containing the Lexicon .json files to publish.");
        if (!Directory.Exists(inputDir))
            return Error(context, $"Input directory not found: {inputDir}");

        var load = LexiconLoader.LoadDirectory(inputDir);
        ReportLoad(context, load, verbose: false);
        if (load.Errors.Count > 0)
            return Error(context, "Publish refused: some files could not be read.");
        if (load.Documents.Count == 0)
            return Error(context, $"No valid Lexicon documents found in: {inputDir}");

        var diagnostics = load.Documents.SelectMany(d => LexiconLinter.Lint(d.Document)).ToList();
        ReportDiagnostics(context.Error, diagnostics);
        if (diagnostics.Any(d => d.Severity == LintSeverity.Error))
            return Error(context, "Publish refused: fix the errors above first.");

        identifier ??= context.GetEnvironmentVariable(IdentifierVariable);
        if (string.IsNullOrWhiteSpace(identifier))
            return Error(context, $"--identifier is required (or set {IdentifierVariable}): the handle or DID of the account that publishes.");

        pds ??= context.GetEnvironmentVariable(PdsVariable);
        Uri? pdsUrl = null;
        if (!string.IsNullOrWhiteSpace(pds) &&
            (!Uri.TryCreate(pds, UriKind.Absolute, out pdsUrl) || !SdkLexgenNetwork.MaySignInAt(pdsUrl)))
        {
            return Error(context, $"--pds must be an absolute https URL (http only for a PDS on this machine), not '{pds}'.");
        }

        var password = context.GetEnvironmentVariable(PasswordVariable);
        if (string.IsNullOrEmpty(password))
            password = context.ReadSecret($"App password for {identifier}: ");
        if (string.IsNullOrEmpty(password))
            return Error(context, $"No password: set {PasswordVariable} to an app password, or run interactively to be prompted.");

        var network = context.CreateNetwork();
        try
        {
            IReadOnlyList<PublishedLexicon> published;
            Did did;
            await using (var repository = await network.SignInAsync(identifier, password, pdsUrl, cancellationToken))
            {
                did = repository.Did;
                context.Out.WriteLine($"Publishing {load.Documents.Count} schema(s) to {did}");
                published = await new LexiconPublisher(repository).PublishAsync(load.Documents, force, cancellationToken);
            }

            foreach (var result in published)
            {
                context.Out.WriteLine(result.Outcome switch
                {
                    PublishOutcome.Created => $"  CREATE {result.Nsid}  {result.Uri}",
                    PublishOutcome.Updated => $"  UPDATE {result.Nsid}  {result.Uri}",
                    PublishOutcome.Unchanged => $"  SAME   {result.Nsid}",
                    _ => $"  REFUSE {result.Nsid}",
                });
            }

            var breaking = published.Where(p => p.Diff?.HasBreakingChanges == true).ToList();
            if (published.Any(p => p.Outcome == PublishOutcome.Refused))
            {
                foreach (var result in breaking)
                    context.Error.WriteLine(result.Diff!.ToReport());

                return Error(context,
                    "Publish refused: the changes above break schemas that are already published, so nothing was written. " +
                    "Publish the change under a new NSID, or pass --force to overwrite anyway.");
            }

            foreach (var result in breaking)
                context.Error.WriteLine($"  WARN  {result.Nsid}: published a breaking change (--force)");

            ReportDns(context, await LexiconPublisher.CheckDnsAsync(
                network, did, load.Documents.Select(d => Nsid.Parse(d.Document.Id)), cancellationToken));
            return 0;
        }
        catch (Exception ex) when (ex is AtProtoException or HttpRequestException)
        {
            return Error(context, $"Publish failed: {ex.Message}");
        }
        finally
        {
            (network as IDisposable)?.Dispose();
        }
    }

    private static void ReportDns(CommandContext context, IReadOnlyList<DnsRecordCheck> checks)
    {
        context.Out.WriteLine();
        context.Out.WriteLine("Resolvers find these schemas through one DNS TXT record per NSID authority:");
        foreach (var check in checks)
        {
            context.Out.WriteLine(check.Status switch
            {
                DnsRecordStatus.Ok => $"  OK       {check.Name}  TXT \"{check.Value}\"",
                DnsRecordStatus.Missing => $"  MISSING  {check.Name}  TXT \"{check.Value}\"  ← create this record",
                DnsRecordStatus.Mismatch => $"  MISMATCH {check.Name}  names {check.Detail}; set it to TXT \"{check.Value}\"",
                _ => $"  UNKNOWN  {check.Name}  TXT \"{check.Value}\"  (lookup failed: {check.Detail})",
            });
        }
    }

    // ──────────────────────────────────────────────────────────
    //  resolve
    // ──────────────────────────────────────────────────────────

    /// <summary>Resolves published schemas and prints or writes them.</summary>
    private static async Task<int> RunResolveCommand(string[] args, CommandContext context, CancellationToken cancellationToken)
    {
        var nsids = new List<Nsid>();
        Did? authority = null;
        string? outputDir = null;

        if (ParseOptions(args, context, PrintResolveHelp,
            options: new Dictionary<string, Func<string, string?>>
            {
                ["--did"] = v => Did.TryParse(v, out authority) ? null : $"--did must be a DID, not '{v}'.",
                ["--output"] = v => { outputDir = v; return null; },
                ["-o"] = v => { outputDir = v; return null; },
            },
            positional: value =>
            {
                if (!Nsid.TryParse(value, out var nsid))
                    return $"'{value}' is not an NSID.";
                nsids.Add(nsid);
                return null;
            }) is { } exitCode)
        {
            return exitCode;
        }

        if (nsids.Count == 0)
            return Error(context, "Specify the NSID of at least one schema to resolve.");

        var network = context.CreateNetwork();
        try
        {
            var failures = 0;
            foreach (var nsid in nsids)
            {
                ResolvedLexicon resolved;
                try
                {
                    resolved = await network.ResolveAsync(nsid, authority, cancellationToken);
                }
                catch (LexiconResolutionException ex)
                {
                    context.Error.WriteLine($"  FAIL  {nsid}: {ex.Message}");
                    failures++;
                    continue;
                }

                var json = ToDocumentJson(resolved.Schema);
                if (outputDir is null)
                {
                    context.Error.WriteLine($"Resolved {nsid} from {resolved.Uri} ({resolved.Cid})");
                    context.Out.WriteLine(json);
                }
                else
                {
                    var relativePath = RelativePath(nsid.Value);
                    var fullPath = Path.Combine(outputDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    await File.WriteAllTextAsync(fullPath, json + Environment.NewLine, cancellationToken);
                    context.Out.WriteLine($"  WRITE {relativePath}  ({resolved.Uri}, {resolved.Cid})");
                }
            }

            return failures > 0 ? 1 : 0;
        }
        finally
        {
            (network as IDisposable)?.Dispose();
        }
    }

    /// <summary>A resolved schema as a Lexicon file: the record without its <c>$type</c>.</summary>
    internal static string ToDocumentJson(LexiconSchemaRecord schema)
    {
        var node = JsonSerializer.SerializeToNode(schema, AtProtoJsonDefaults.Options)!.AsObject();
        node.Remove("$type");
        foreach (var name in node.Where(p => p.Value is null).Select(p => p.Key).ToList())
            node.Remove(name);

        // Localized titles and descriptions stay readable in the file rather than \u-escaped.
        return node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    // ──────────────────────────────────────────────────────────
    //  Shared output
    // ──────────────────────────────────────────────────────────

    /// <summary>The path a schema is written to: <c>app.example.post</c> → <c>app/example/post.json</c>.</summary>
    private static string RelativePath(string nsid) => Path.Combine(nsid.Split('.')) + ".json";

    private static void ReportLoad(CommandContext context, LexiconLoadResult load, bool verbose)
    {
        if (verbose)
        {
            foreach (var source in load.Documents)
                context.Out.WriteLine($"  OK    {source.Document.Id}");
        }

        foreach (var skipped in load.Skipped)
            context.Error.WriteLine($"  SKIP  {skipped}");
        foreach (var error in load.Errors)
            context.Error.WriteLine($"  ERROR {error}");

        if (load.Errors.Count > 0)
            context.Error.WriteLine($"\n{load.Errors.Count} file(s) had parse errors.");
    }

    private static void ReportDiagnostics(TextWriter writer, IEnumerable<LintDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            writer.WriteLine($"  {(diagnostic.Severity == LintSeverity.Error ? "ERROR" : "WARN ")} {diagnostic}");
    }

    private static void ReportWarnings(CommandContext context, IEnumerable<string> warnings)
    {
        var distinct = warnings.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0)
            return;

        context.Error.WriteLine();
        foreach (var warning in distinct)
            context.Error.WriteLine($"  WARN  {warning}");
        context.Error.WriteLine();
    }

    private static bool IsAssemblyLoadFailure(Exception ex) =>
        ex is BadImageFormatException or FileLoadException or FileNotFoundException
            or ReflectionTypeLoadException or TypeLoadException;

    private static int Error(CommandContext context, string message)
    {
        context.Error.WriteLine($"Error: {message}");
        return 1;
    }

    /// <summary>
    /// The <c>for</c>/<c>switch</c> loop every command's options went through by hand: matches each
    /// argument against <paramref name="options"/> (consumes the next argument) or
    /// <paramref name="flags"/> (does not), prints <paramref name="printHelp"/> and exits 0 on
    /// <c>--help</c>/<c>-h</c>, and passes anything left that does not start with <c>-</c> to
    /// <paramref name="positional"/> when given. A handler returns an error message to fail the
    /// command, or <see langword="null"/> to keep parsing; an argument nothing recognizes fails
    /// with "Unknown option".
    /// </summary>
    /// <returns>The exit code to return immediately, or <see langword="null"/> to keep going.</returns>
    private static int? ParseOptions(
        string[] args,
        CommandContext context,
        Action<TextWriter> printHelp,
        IReadOnlyDictionary<string, Func<string, string?>>? options = null,
        IReadOnlyDictionary<string, Func<string?>>? flags = null,
        Func<string, string?>? positional = null)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                printHelp(context.Out);
                return 0;
            }

            if (flags is not null && flags.TryGetValue(arg, out var setFlag))
            {
                var flagError = setFlag();
                if (flagError is not null)
                    return Error(context, flagError);
                continue;
            }

            if (options is not null && options.TryGetValue(arg, out var setValue) && i + 1 < args.Length)
            {
                var valueError = setValue(args[++i]);
                if (valueError is not null)
                    return Error(context, valueError);
                continue;
            }

            if (positional is not null && !arg.StartsWith('-'))
            {
                var positionalError = positional(arg);
                if (positionalError is not null)
                    return Error(context, positionalError);
                continue;
            }

            return Error(context, $"Unknown option: '{arg}'");
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────
    //  Help
    // ──────────────────────────────────────────────────────────

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine($"""
            atproto-lexgen {Version} — AT Protocol Lexicon tool

            Generates C# from Lexicon JSON and Lexicon JSON from C#, checks schemas, and publishes and
            resolves them on the network.

            USAGE:
                atproto-lexgen <command> [options]

            COMMANDS:
                csharp      Generate C# source files from Lexicon JSON schemas
                lexicon     Generate Lexicon JSON schemas from a compiled .NET assembly
                lint        Check Lexicon JSON schemas, permission sets included
                diff        Compare Lexicon schemas and detect breaking changes
                publish     Publish Lexicon schemas as records in your repository
                resolve     Fetch published Lexicon schemas by NSID

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

                # Publish your schemas, then fetch one back
                ATPROTO_PASSWORD=… atproto-lexgen publish --input ./lexicons --identifier alice.example.com
                atproto-lexgen resolve com.example.todo.item

            Run 'atproto-lexgen <command> --help' for command-specific options.
            """);
    }

    private static void PrintCSharpHelp(TextWriter output)
    {
        output.WriteLine("""
            atproto-lexgen csharp — Generate C# from Lexicon JSON

            Reads AT Protocol Lexicon .json schema files and generates strongly-typed
            C# classes with [JsonPropertyName] attributes and proper $type discriminators.

            OPTIONS:
                -i, --input <dir>         Directory containing Lexicon .json files (required)
                -o, --output <dir>        Output directory for generated .cs files (required)
                -n, --namespace <prefix>  C# namespace prefix (default: ATProtoNet.Lexicon)

            Pass every schema your Lexicons reference in one invocation — refs and unions are
            resolved across the whole input set.

            Generated types follow ATProtoNet SDK conventions:
              - sealed classes with init-only properties
              - required keyword for non-optional fields
              - [JsonPropertyName] on all properties
              - record defs subclass AtProtoRecord and implement IAtProtoRecord ($type override, static Collection, inherited createdAt)
              - unions of object defs become [JsonPolymorphic] base classes
              - refs to com.atproto.*/app.bsky.* defs reuse the SDK's own models
              - token families are grouped into one static class of constants
              - space defs become a static holder for the SDK's SpaceTypeDeclaration
              - permission-set defs become a static class with the NSID and an Include(aud) scope helper

            Refs that cannot be resolved fall back to JsonElement? and are reported as WARN,
            as is a definition type outside the Lexicon vocabulary. Run 'lint' to check the
            schemas themselves.

            EXAMPLE:
                atproto-lexgen csharp \
                  --input ./lexicons/app/bsky \
                  --output ./src/ATProtoNet/Lexicon \
                  --namespace ATProtoNet.Lexicon
            """);
    }

    private static void PrintLexiconHelp(TextWriter output)
    {
        output.WriteLine("""
            atproto-lexgen lexicon — Generate Lexicon JSON from C# assembly

            Loads a compiled .NET assembly and generates AT Protocol Lexicon JSON
            schema files for all types that have a [JsonPropertyName("$type")] property.

            OPTIONS:
                -a, --assembly <path>  Path to the .NET assembly (.dll) to analyze (required)
                -o, --output <dir>     Output directory for generated .json files (required)

            The tool detects AT Protocol record types by looking for properties annotated
            with [JsonPropertyName("$type")] and reads their constant value to determine the
            NSID. Space types are detected by their declaration: a public static
            SpaceTypeDeclaration on a type that also carries an Nsid (or SpaceType) constant.

            EXAMPLE:
                atproto-lexgen lexicon \
                  --assembly ./bin/Debug/net10.0/MyApp.dll \
                  --output ./lexicons
            """);
    }

    private static void PrintLintHelp(TextWriter output)
    {
        output.WriteLine("""
            atproto-lexgen lint — Check Lexicon JSON schemas

            Checks schema files against the Lexicon and permission specifications:
              - 'id' is a valid NSID and the document has definitions
              - primary types (record, query, procedure, subscription, permission-set, space)
                are the 'main' definition
              - no 'null' type (removed from the Lexicon language)
              - space defs list NSIDs in 'collections', never '*'
              - permission sets: only repo and rpc permissions (no blob, account or identity),
                no wildcards, only NSIDs under the set's own namespace, 'action' values from
                create/update/delete, and an rpc 'aud' of '*' or 'inheritAud' — not both

            OPTIONS:
                -i, --input <dir>   Directory containing Lexicon .json files (required)
                --strict            Also exit with code 1 on warnings

            Exits with code 1 when there are errors.

            EXAMPLE:
                atproto-lexgen lint --input ./lexicons
            """);
    }

    private static void PrintDiffHelp(TextWriter output)
    {
        output.WriteLine("""
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

    private static void PrintPublishHelp(TextWriter output)
    {
        output.WriteLine($"""
            atproto-lexgen publish — Publish Lexicon schemas to your repository

            Writes each schema as a com.atproto.lexicon.schema record, keyed by its NSID, in the
            repository of the account you sign in as, then checks the _lexicon DNS TXT record each
            NSID authority needs for resolvers to find the schemas, and prints any that are missing.

            The schemas are linted first; errors stop the publish. Each one is compared with the
            version already published: unchanged schemas are skipped, and a breaking change stops
            the whole publish before anything is written, unless --force is given.

            OPTIONS:
                -i, --input <dir>          Directory of Lexicon .json files to publish (required)
                -u, --identifier <id>      The account's handle or DID (default: ${IdentifierVariable})
                --pds <url>                The account's PDS (default: ${PdsVariable}, else looked up
                                           from the handle or DID)
                --force                    Publish breaking changes too

            The password is read from {PasswordVariable} — use an app password — or prompted for
            without echo when the terminal is interactive. It is never taken as an argument.

            DNS: the NSID app.example.feed.post belongs to the authority feed.example.app, whose
            record is TXT _lexicon.feed.example.app "did=<your DID>". There is no hierarchical
            lookup: each authority needs its own record.

            EXAMPLE:
                export {PasswordVariable}=xxxx-xxxx-xxxx-xxxx
                atproto-lexgen publish --input ./lexicons --identifier lexicons.example.com
            """);
    }

    private static void PrintResolveHelp(TextWriter output)
    {
        output.WriteLine("""
            atproto-lexgen resolve — Fetch published Lexicon schemas

            Resolves each NSID as the Lexicon specification describes: the _lexicon DNS TXT record
            of its authority names a DID, whose PDS holds the schema record; the record is fetched
            with its proof and verified against the DID's signing key.

            USAGE:
                atproto-lexgen resolve <nsid>... [options]

            OPTIONS:
                --did <did>            Read the schemas from this repository, skipping DNS
                -o, --output <dir>     Write each schema to <dir>/<nsid path>.json instead of
                                       printing it

            EXAMPLES:
                atproto-lexgen resolve site.standard.document
                atproto-lexgen resolve app.bsky.feed.post app.bsky.actor.profile --output ./lexicons
            """);
    }
}
