using LexGen = ATProtoNet.LexiconGenerator.Program;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Tests for the <c>atproto-lexgen</c> command-line entry point.</summary>
public sealed class LexiconGeneratorProgramTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task Main_VersionFlag_PrintsPackageVersion(string flag)
    {
        var assemblyVersion = typeof(LexGen).Assembly.GetName().Version!;
        var packageVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        var (exitCode, output) = await RunAsync(flag);

        Assert.Equal(0, exitCode);
        // Console output is process-wide, so a test running in parallel may add lines of its own.
        var line = Assert.Single(output.Split('\n'), l => l.StartsWith("atproto-lexgen ", StringComparison.Ordinal));
        Assert.StartsWith($"atproto-lexgen {packageVersion}", line.TrimEnd('\r'), StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exitCode = await LexGen.Main(args);
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
