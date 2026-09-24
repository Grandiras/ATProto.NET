namespace ATProtoNet.Tests.Interop;

/// <summary>
/// Reads the vendored <c>atproto-interop-tests</c> syntax fixtures (see <c>Interop/README.md</c>).
/// </summary>
/// <remarks>
/// The files are read from the source tree instead of being copied to the build output, so
/// re-vendoring or adding a file needs no project change. Test binaries always run from a
/// directory beneath the repository (<c>bin/</c> or <c>artifacts/</c>), so walking up finds them.
/// </remarks>
internal static class SyntaxFixtures
{
    private static readonly Lazy<string> FixtureDirectory = new(FindDirectory);

    /// <summary>
    /// The test lines of one fixture file. Following the upstream harnesses, blank lines and
    /// lines starting with <c>#</c> are skipped and nothing is trimmed: leading and trailing
    /// whitespace is part of several invalid cases.
    /// </summary>
    public static TheoryData<string> Lines(string fileName)
    {
        var data = new TheoryData<string>();
        foreach (var line in File.ReadLines(Path.Combine(FixtureDirectory.Value, fileName)).Distinct(StringComparer.Ordinal))
        {
            if (line.Length > 0 && !line.StartsWith('#'))
                data.Add(line);
        }

        return data;
    }

    private static string FindDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(dir.FullName, "Interop", "syntax"),
                Path.Combine(dir.FullName, "tests", "ATProtoNet.Tests", "Interop", "syntax"),
            })
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Interop/syntax fixtures not found above '{AppContext.BaseDirectory}'.");
    }
}
