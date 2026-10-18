using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATProtoNet.Conformance.Suites;

// The reference-regex oracle (SYN-8): every grammar's verdicts match the reference pattern of
// syntax/reference-patterns.json over the interop lines, seeded mutations of them and generated
// near-misses. Ported from 0.8 IdentifierSyntaxTests.
public abstract partial class SyntaxConformance<TAdapter>
{
    private static readonly JsonNode Patterns = Vectors.Json("syntax/reference-patterns.json");

    private static readonly Dictionary<string, Func<string, string?>> References = Patterns["grammars"]!.AsArray()
        .Where(g => g!["pattern"] is not null)
        .ToDictionary(g => (string)g!["name"]!, Reference);

    /// <summary>One row per grammar of the oracle file.</summary>
    public static TheoryData<string, string> OracleGrammars() =>
        new(Patterns["grammars"]!.AsArray().Select(g => ($"syntax/reference-patterns.json#{g!["name"]}", (string)g["name"]!)));

    /// <summary>The adapter accepts exactly what the reference pattern accepts, normalized as the reference does.</summary>
    [Theory]
    [MemberData(nameof(OracleGrammars))]
    [Trait("Spec", "SYN-8")]
    public void ParseIdentifier_OracleInput_MatchesTheReferencePattern(string vectorId, string grammar)
    {
        Vector(vectorId);
        var node = Patterns["grammars"]!.AsArray().Single(g => (string)g!["name"]! == grammar)!;
        var kind = Enum.Parse<IdentifierKind>((string)node["kind"]!);
        var reference = grammar == "aturi" ? ReferenceAtUri : References[grammar];
        var inputs = OracleInputs(grammar, node).ToList();
        var mismatches = new List<string>();
        var accepted = 0;

        foreach (var input in inputs)
        {
            var expected = reference(input);
            var verdict = Adapter.ParseIdentifier(kind, input);
            accepted += expected is null ? 0 : 1;
            if (verdict.Valid != expected is not null || expected is not null && verdict.Normalized != expected)
                mismatches.Add($"{Show(input)}: expected {Show(expected)}, got {Show(verdict.Valid ? verdict.Normalized : null)}");
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {inputs.Count} {grammar} inputs disagree:\n" + string.Join('\n', mismatches.Take(20)));
        Assert.InRange(accepted, inputs.Count / 50, inputs.Count - inputs.Count / 50);
    }

    // The canonical text the pattern admits the input as, or null.
    private static Func<string, string?> Reference(JsonNode? grammar)
    {
        var pattern = new Regex((string)grammar!["pattern"]!, RegexOptions.CultureInvariant);
        var maxLength = (int?)grammar["maxLength"] ?? int.MaxValue;
        var excluded = grammar["excluded"]?.AsArray().Select(e => (string)e!).ToHashSet() ?? [];
        var prefix = (string?)grammar["stripPrefix"];
        var lower = (bool?)grammar["lowerCase"] ?? false;
        return input =>
        {
            var value = prefix is not null && input.StartsWith(prefix, StringComparison.Ordinal) ? input[prefix.Length..] : input;
            if (value.Length > maxLength || excluded.Contains(value) || !pattern.IsMatch(value)) return null;
            return lower ? value.ToLowerInvariant() : value;
        };
    }

    // at://AUTHORITY[/COLLECTION[/RKEY]] composed of the other grammars' patterns.
    private static string? ReferenceAtUri(string s)
    {
        if (s.Length > 8 * 1024 || !s.StartsWith("at://", StringComparison.Ordinal)) return null;
        var parts = s[5..].Split('/', 3);
        var authority = parts[0];
        var authorityValid = authority.StartsWith("did:", StringComparison.Ordinal)
            ? References["did"](authority) is not null
            : !authority.StartsWith('@') && References["handle"](authority) is not null;
        if (!authorityValid) return null;
        if (parts.Length > 1 && References["nsid"](parts[1]) is null) return null;
        if (parts.Length > 2 && References["recordkey"](parts[2]) is null) return null;
        return s;
    }

    private const string LabelChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-";

    // Characters every grammar treats specially, or that none allows.
    private const string EdgeChars = ".:%_~-@/#? \t\né 中0Zz";

    private static IEnumerable<string> OracleInputs(string grammar, JsonNode node)
    {
        var random = new Random((int)Patterns["seed"]!);
        var mutations = (int)Patterns["mutationsPerSeed"]!;
        var seeds = node["seedFiles"]!.AsArray().SelectMany(f => Vectors.Lines($"syntax/{f}")).Distinct(StringComparer.Ordinal).ToList();

        foreach (var seed in seeds)
        {
            yield return seed;
            for (var i = 0; i < mutations; i++)
                yield return Mutate(seed, random);
        }

        for (var i = (int)Patterns["generated"]!; i > 0; i--)
            yield return Generate(grammar, random);

        foreach (var value in AtTheLengthLimit(grammar))
        {
            yield return value;
            yield return value + "a";
        }
    }

    private static string[] AtTheLengthLimit(string grammar) => grammar switch
    {
        "did" => ["did:plc:" + new string('a', 2048 - 8)],
        "handle" => [string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61))],
        "nsid" => [string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 63), new string('e', 61))],
        "recordkey" => [new string('a', 512)],
        _ => [],
    };

    private static string Mutate(string seed, Random random)
    {
        var chars = new StringBuilder(seed);
        for (var edits = random.Next(1, 3); edits > 0; edits--)
        {
            var at = random.Next(chars.Length + 1);
            switch (random.Next(5))
            {
                case 0 when at < chars.Length:
                    chars[at] = RandomChar(random);
                    break;
                case 1:
                    chars.Insert(at, RandomChar(random));
                    break;
                case 2 when at < chars.Length:
                    chars.Remove(at, 1);
                    break;
                case 3 when at < chars.Length:
                    chars[at] = char.IsUpper(chars[at]) ? char.ToLowerInvariant(chars[at]) : char.ToUpperInvariant(chars[at]);
                    break;
                default:
                    // Lengthen a run, to reach the per-label and total length limits.
                    var from = random.Next(chars.Length + 1);
                    chars.Insert(at, chars.ToString(from, Math.Min(random.Next(1, 70), chars.Length - from)));
                    break;
            }
        }

        return chars.ToString();
    }

    private static string Generate(string grammar, Random random) => grammar switch
    {
        "did" => $"did:{Run(random, "abcdefghijklmnopqrstuvwxyzA0:", random.Next(0, 5))}:{Run(random, LabelChars + "._:%~", RandomLength(random, 2048))}",
        "handle" => Labels(random, random.Next(1, 6), 253),
        "nsid" => Labels(random, random.Next(1, 7), 317),
        "recordkey" => Run(random, LabelChars + "._:~", RandomLength(random, 512)),
        "tid" => Run(random, "234567abcdefghijklmnopqrstuvwxyz", random.Next(11, 15)),
        "aturi" => $"at://{(random.Next(2) == 0 ? Generate("did", random) : Generate("handle", random))}"
            + (random.Next(4) == 0 ? "" : "/" + Generate("nsid", random) + (random.Next(3) == 0 ? "" : "/" + Generate("recordkey", random))),
        _ => throw new ArgumentOutOfRangeException(nameof(grammar)),
    };

    // Dot-separated labels, sized to land on either side of the per-label limit and, now and then,
    // of the total one.
    private static string Labels(Random random, int count, int maxLength)
    {
        var value = string.Join('.', Enumerable.Range(0, count).Select(_ => Run(random, LabelChars, random.Next(8) switch
        {
            0 => 0,
            1 => random.Next(61, 65),
            _ => random.Next(1, 12),
        })));

        if (random.Next(10) != 0 || value.Length == 0) return value;
        var target = maxLength + random.Next(-1, 2);
        var first = value.IndexOf('.');
        return first > 0 && target > value.Length
            ? value.Insert(first, Run(random, "abcdefghij", Math.Min(target - value.Length, 60)))
            : value;
    }

    private static int RandomLength(Random random, int limit) => random.Next(10) switch
    {
        0 => 0,
        1 => limit + random.Next(-2, 3),
        _ => random.Next(1, 20),
    };

    private static string Run(Random random, string alphabet, int length)
    {
        var chars = new char[Math.Max(length, 0)];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = random.Next(12) == 0 ? RandomChar(random) : alphabet[random.Next(alphabet.Length)];
        return new string(chars);
    }

    private static char RandomChar(Random random) => random.Next(3) == 0
        ? EdgeChars[random.Next(EdgeChars.Length)]
        : LabelChars[random.Next(LabelChars.Length)];

    private static string Show(string? value) => value is null ? "invalid" : System.Text.Json.JsonSerializer.Serialize(value);
}
