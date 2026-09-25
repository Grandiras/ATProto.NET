using System.Text.RegularExpressions;

namespace ATProtoNet.Tests.Lexicon.Upstream;

/// <summary>
/// Reads the NSIDs the SDK names from its source: the XRPC calls it makes, and every NSID or
/// def reference in a string literal.
/// </summary>
/// <remarks>
/// The call sites are found in source rather than by reflection because a method's NSID and
/// HTTP method are only in its body: every client passes a literal NSID to one of the internal
/// <c>XrpcClient</c> methods, whose name fixes the HTTP method.
/// </remarks>
internal static partial class SdkSource
{
    private static readonly Lazy<List<(string Path, string[] Lines)>> Files = new(ReadFiles);

    /// <summary>One XRPC call: its NSID, whether it is a GET, and where it is.</summary>
    internal sealed record XrpcCall(string Nsid, bool IsGet, string Location);

    /// <summary>An NSID or def reference in a string literal, and where it is.</summary>
    internal sealed record NsidLiteral(string Value, string Location);

    /// <summary>Every XRPC call in <c>src/</c> made with a literal NSID.</summary>
    public static IReadOnlyList<XrpcCall> XrpcCalls { get; } = FindCalls();

    /// <summary>
    /// Every NSID or <c>nsid#def</c> reference in a string literal in <c>src/</c>, outside
    /// comments, in the namespaces the upstream snapshot covers.
    /// </summary>
    public static IReadOnlyList<NsidLiteral> NsidLiterals { get; } = FindLiterals();

    // QueryAsync<T>("nsid", …), ProcedureAsync("nsid", …), …: the generic arguments hold no
    // parentheses, so the match can span the line break most calls put after the '('.
    [GeneratedRegex(@"\b(?<method>QueryAsync|DownloadAsync|ProcedureAsync|UploadAsync|ProcedureWithTokenAsync)\s*(?:<[^()]*>)?\s*\(\s*""(?<nsid>[^""]+)""")]
    private static partial Regex CallPattern();

    [GeneratedRegex(@"""(?:[^""\\\n]|\\.)*""")]
    private static partial Regex StringLiteralPattern();

    [GeneratedRegex(@"(?<![\w.])(?:app\.bsky|chat\.bsky|com\.atproto|tools\.ozone|site\.standard)(?:\.[A-Za-z][A-Za-z0-9]*)+(?:#[A-Za-z][A-Za-z0-9]*)?(?![\w])")]
    private static partial Regex NsidPattern();

    private static List<XrpcCall> FindCalls()
    {
        var calls = new List<XrpcCall>();
        foreach (var (path, lines) in Files.Value)
        {
            var text = string.Join('\n', lines);
            foreach (Match match in CallPattern().Matches(text))
            {
                var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                var method = match.Groups["method"].Value;
                calls.Add(new XrpcCall(
                    match.Groups["nsid"].Value,
                    method is "QueryAsync" or "DownloadAsync",
                    $"{path}:{line}"));
            }
        }

        return calls;
    }

    private static List<NsidLiteral> FindLiterals()
    {
        var literals = new List<NsidLiteral>();
        foreach (var (path, lines) in Files.Value)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                foreach (Match literal in StringLiteralPattern().Matches(lines[i]))
                {
                    foreach (Match nsid in NsidPattern().Matches(literal.Value))
                        literals.Add(new NsidLiteral(nsid.Value, $"{path}:{i + 1}"));
                }
            }
        }

        return literals;
    }

    private static List<(string Path, string[] Lines)> ReadFiles()
    {
        var src = Path.Combine(UpstreamLexicons.RepositoryRoot, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(UpstreamLexicons.RepositoryRoot, f).Replace('\\', '/'), File.ReadAllLines(f)))
            .ToList();
    }
}
