using System.CommandLine;
using System.CommandLine.Parsing;

namespace ATProtoNet.Lexicon.Cli;

/// <summary>The exit codes of every command (TOO-3).</summary>
public static class ExitCodes
{
    /// <summary>Success, including warnings without <c>--strict</c>.</summary>
    public const int Success = 0;

    /// <summary>Findings: lint errors, breaking changes under <c>--strict</c>, <c>generate --check</c> would change files.</summary>
    public const int Findings = 1;

    /// <summary>A usage error: an unknown command or option, a missing or invalid value.</summary>
    public const int Usage = 2;

    /// <summary>An input error: a missing directory, an unreadable or malformed file, a manifest/CID mismatch, no Lexicons.</summary>
    public const int Input = 3;

    /// <summary>A network or remote failure.</summary>
    public const int Network = 4;
}

/// <summary>A failure that ends a command with an exit code and a one-line message.</summary>
/// <param name="exitCode">The exit code.</param>
/// <param name="message">The message.</param>
public sealed class LexgenException(int exitCode, string message) : Exception(message)
{
    /// <summary>The exit code.</summary>
    public int ExitCode { get; } = exitCode;
}

/// <summary><c>atproto-lexgen</c>: parses arguments, runs a command against an environment, and maps failures to exit codes.</summary>
/// <param name="environment">The console, files, environment variables and network.</param>
public sealed partial class LexgenApp(LexgenEnvironment environment)
{
    private LexgenEnvironment Env { get; } = environment;

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code.</returns>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var verbose = new Option<bool>("--verbose") { Description = "Print stack traces of failures.", Recursive = true };
        var root = new RootCommand("Vendors, lints and generates C# from AT Protocol Lexicon schemas.") { verbose };
        root.Subcommands.Add(GenerateCommand());
        root.Subcommands.Add(LintCommand());
        root.Subcommands.Add(InstallCommand());
        root.Subcommands.Add(UpdateCommand());
        root.SetAction(result =>
        {
            new System.CommandLine.Help.HelpAction().Invoke(result);
            return ExitCodes.Success;
        });

        var parsed = root.Parse(args);
        // An unknown option is taken as a value by a command with free arguments (lint's paths); it is still a usage error.
        var errors = parsed.Errors.Select(e => e.Message)
            .Concat(parsed.Tokens.Where(t => t.Type == TokenType.Argument && t.Value.StartsWith("--", StringComparison.Ordinal)).Select(t => $"Unrecognized option '{t.Value}'.")).ToList();
        if (errors.Count > 0 && !args.Any(a => a is "--help" or "-h" or "-?" or "--version"))
        {
            foreach (var error in errors) await Env.Error.WriteLineAsync(error);
            return ExitCodes.Usage;
        }
        try
        {
            return await parsed.InvokeAsync(new InvocationConfiguration { Output = Env.Out, Error = Env.Error, EnableDefaultExceptionHandler = false }, cancellationToken);
        }
        catch (LexgenException e)
        {
            await Env.Error.WriteLineAsync(parsed.GetValue(verbose) ? e.ToString() : e.Message);
            return e.ExitCode;
        }
        catch (HttpRequestException e)
        {
            await Env.Error.WriteLineAsync(parsed.GetValue(verbose) ? e.ToString() : "Network failure: " + e.Message);
            return ExitCodes.Network;
        }
    }

    private static Option<string> Manifest() => new("--manifest") { Description = "The manifest of vendored Lexicons.", DefaultValueFactory = _ => "lexicons.json" };

    private LexiconManifest ReadManifest(string path) =>
        !Env.Files.FileExists(path) ? throw new LexgenException(ExitCodes.Input, $"{path}: the manifest does not exist.")
        : Catch(path, () => LexiconManifest.Parse(Env.Files.ReadAllBytes(path)));

    private static T Catch<T>(string path, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            throw new LexgenException(ExitCodes.Input, $"{path}: {e.Message}");
        }
    }

    // The Lexicons directory next to a manifest.
    private static string LexiconsDirectory(string manifestPath, string? lexicons) =>
        lexicons ?? Path.Combine(Path.GetDirectoryName(manifestPath) is { Length: > 0 } directory ? directory : ".", "lexicons");
}
