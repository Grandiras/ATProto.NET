using System.Text.RegularExpressions;

namespace ATProtoNet.Tests.Lexicon.Upstream;

/// <summary>
/// Reads the query-parameter keys each XRPC call site sends, by walking the source around it.
/// </summary>
/// <remarks>
/// Most calls pass an <c>XrpcParams</c> built inline (<c>new XrpcParams().Add(…)</c>, or for the
/// Ozone filter objects <c>filter.ToParams(…)</c>); the rest build it with a
/// <c>var parameters = new XrpcParams()…;</c> statement
/// immediately before the call that uses it — occasionally with more <c>parameters.Add(…)</c>
/// statements chained in between, such as a filter block guarded by an <c>if</c>. Rather than a
/// full parser, this finds the declaration, then the nearest following call that mentions the
/// same variable name, and collects every <c>.Add</c>/<c>.AddAll</c> key in between.
/// </remarks>
internal static partial class SdkSource
{
    /// <summary>The query-parameter keys one call site sends, and where its builder starts.</summary>
    internal sealed record ParamCall(string Nsid, IReadOnlyList<string> Keys, string Location);

    /// <summary>
    /// Every call site that builds an <c>XrpcParams</c> with a resolvable literal key set, across
    /// <c>src/</c>.
    /// </summary>
    public static IReadOnlyList<ParamCall> ParamCalls { get; } = FindParamCalls();

    // var parameters = new XrpcParams()… or var parameters = (filter ?? Foo.None).ToParams(…)…
    [GeneratedRegex(@"^\s*var\s+(?<var>\w+)\s*=\s*(?:new XrpcParams\(\)|.*\.ToParams\()")]
    private static partial Regex ParamBuilderDeclPattern();

    [GeneratedRegex(@"new XrpcParams\(\)|\.ToParams\(")]
    private static partial Regex InlineBuilderPattern();

    [GeneratedRegex(@"\.(?:Add|AddAll)\(\s*""(?<key>[^""]+)""")]
    private static partial Regex AddKeyPattern();

    // (filter ?? ReportFilter.None).ToParams(status): the type name sits right before '.None)'.
    [GeneratedRegex(@"(?<type>\w+)(?:\?)?\.None\)\s*\.ToParams")]
    private static partial Regex ToParamsDefaultPattern();

    [GeneratedRegex(@"^\s*(?:public|internal)\s+(?:static\s+)?(?:sealed\s+|abstract\s+)?(?:partial\s+)?(?:class|record)\s+(?<type>\w+)")]
    private static partial Regex TypeDeclPattern();

    [GeneratedRegex(@"XrpcParams\??\s+ToParams\s*\(")]
    private static partial Regex ToParamsMethodPattern();

    private static List<ParamCall> FindParamCalls()
    {
        var helperKeys = FindHelperKeys();
        var callsByFile = XrpcCalls
            .Select(c =>
            {
                var colon = c.Location.LastIndexOf(':');
                return (Path: c.Location[..colon], Line: int.Parse(c.Location[(colon + 1)..]), c.Nsid);
            })
            .ToLookup(c => c.Path, c => (c.Line, c.Nsid));

        var result = new List<ParamCall>();
        foreach (var (path, lines) in Files.Value)
        {
            var calls = callsByFile[path].OrderBy(c => c.Line).ToList();
            if (calls.Count == 0)
                continue;

            foreach (var (line, nsid) in calls)
            {
                // The builder passed inline: the statement the call sits in, up to its ';'.
                var end = line - 1;
                while (end < lines.Length - 1 && !lines[end].TrimEnd().EndsWith(';'))
                    end++;
                var inline = string.Join('\n', lines[(line - 1)..(end + 1)]);
                if (!InlineBuilderPattern().IsMatch(inline))
                    continue;

                var keys = new List<string>();
                var typeMatch = ToParamsDefaultPattern().Match(inline);
                if (typeMatch.Success && helperKeys.TryGetValue(typeMatch.Groups["type"].Value, out var hk))
                    keys.AddRange(hk);
                foreach (Match m in AddKeyPattern().Matches(inline))
                    keys.Add(m.Groups["key"].Value);

                if (keys.Count > 0)
                    result.Add(new ParamCall(nsid, [.. keys.Distinct(StringComparer.Ordinal)], $"{path}:{line}"));
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var decl = ParamBuilderDeclPattern().Match(lines[i]);
                if (!decl.Success)
                    continue;

                var varName = decl.Groups["var"].Value;
                var call = FindAssociatedCall(lines, calls, i, varName);
                if (call is null)
                    continue;

                var spanEnd = Math.Min(lines.Length, call.Value.Line + 2);
                var span = string.Join('\n', lines[i..spanEnd]);

                var keys = new List<string>();
                var typeMatch = ToParamsDefaultPattern().Match(lines[i]);
                if (typeMatch.Success && helperKeys.TryGetValue(typeMatch.Groups["type"].Value, out var hk))
                    keys.AddRange(hk);
                foreach (Match m in AddKeyPattern().Matches(span))
                    keys.Add(m.Groups["key"].Value);

                if (keys.Count > 0)
                    result.Add(new ParamCall(call.Value.Nsid, [.. keys.Distinct(StringComparer.Ordinal)], $"{path}:{i + 1}"));
            }
        }

        return result;
    }

    /// <summary>
    /// The nearest call after a builder declaration whose line, or the two lines after it (for a
    /// call split across lines), mentions the builder's variable — bounded to a reasonable
    /// distance so an unrelated later call in the same file is never picked.
    /// </summary>
    private static (int Line, string Nsid)? FindAssociatedCall(
        string[] lines, List<(int Line, string Nsid)> calls, int declIndex, string varName)
    {
        var token = new Regex($@"(?<![\w.]){Regex.Escape(varName)}(?![\w])", RegexOptions.None);
        foreach (var call in calls)
        {
            var callIndex = call.Line - 1;
            if (callIndex <= declIndex)
                continue;
            if (callIndex - declIndex > 200)
                break;

            var upper = Math.Min(lines.Length - 1, callIndex + 2);
            for (var k = declIndex; k <= upper; k++)
            {
                if (token.IsMatch(lines[k]))
                    return call;
            }
        }

        return null;
    }

    /// <summary>
    /// The keys a <c>ToParams()</c> helper method builds, keyed by its declaring type name (the
    /// nearest preceding <c>class</c>/<c>record</c> declaration in the file).
    /// </summary>
    private static Dictionary<string, List<string>> FindHelperKeys()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, lines) in Files.Value)
        {
            string? currentType = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var typeDecl = TypeDeclPattern().Match(lines[i]);
                if (typeDecl.Success)
                    currentType = typeDecl.Groups["type"].Value;

                if (currentType is null || !ToParamsMethodPattern().IsMatch(lines[i]))
                    continue;

                var keys = new List<string>();
                var j = i;
                while (j < lines.Length)
                {
                    foreach (Match m in AddKeyPattern().Matches(lines[j]))
                        keys.Add(m.Groups["key"].Value);
                    if (lines[j].TrimEnd().EndsWith(';'))
                        break;
                    j++;
                }

                result[currentType] = keys;
            }
        }

        return result;
    }
}
