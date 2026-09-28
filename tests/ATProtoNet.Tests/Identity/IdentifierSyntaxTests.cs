using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.Interop;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// The identifier parsers validate by hand. These tests hold each check to the regular expression it
/// replaced — on the interop fixtures, on seeded mutations of them and on generated near-misses at
/// the length limits — in UTF-16 and in the UTF-8 bytes the JSON converters read, so that exactly
/// the same values are accepted.
/// </summary>
public partial class IdentifierSyntaxTests
{
    [GeneratedRegex(@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]\z")]
    private static partial Regex DidPattern();

    [GeneratedRegex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\z")]
    private static partial Regex HandlePattern();

    [GeneratedRegex(@"^[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+(\.[a-zA-Z]([a-zA-Z0-9]{0,62})?)\z")]
    private static partial Regex NsidPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._:~-]{1,512}\z")]
    private static partial Regex RecordKeyPattern();

    [GeneratedRegex(@"^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}\z")]
    private static partial Regex TidPattern();

    private static bool ReferenceDid(string s) => s.Length <= 2048 && DidPattern().IsMatch(s);

    private static bool ReferenceHandle(string s) => s.Length <= 253 && HandlePattern().IsMatch(s);

    private static bool ReferenceNsid(string s) => s.Length <= 317 && NsidPattern().IsMatch(s);

    private static bool ReferenceRecordKey(string s) => s is not "." and not ".." && RecordKeyPattern().IsMatch(s);

    private static bool ReferenceTid(string s) => TidPattern().IsMatch(s);

    // The AT URI parser as it composed the patterns: at://AUTHORITY[/COLLECTION[/RKEY]].
    private static (string Authority, string? Collection, string? RecordKey)? ReferenceAtUri(string s)
    {
        if (s.Length > 8 * 1024 || !s.StartsWith("at://", StringComparison.Ordinal))
            return null;

        var path = s[5..];
        var slash = path.IndexOf('/');
        var authority = slash < 0 ? path : path[..slash];
        if (!(authority.StartsWith("did:", StringComparison.Ordinal) ? ReferenceDid(authority) : ReferenceHandle(authority)))
            return null;

        if (slash < 0)
            return (authority, null, null);

        path = path[(slash + 1)..];
        slash = path.IndexOf('/');
        var collection = slash < 0 ? path : path[..slash];
        if (!ReferenceNsid(collection))
            return null;

        if (slash < 0)
            return (authority, collection, null);

        var rkey = path[(slash + 1)..];
        return ReferenceRecordKey(rkey) ? (authority, collection, rkey) : null;
    }

    public static TheoryData<string> Grammars => ["did", "handle", "nsid", "recordkey", "tid"];

    [Theory]
    [MemberData(nameof(Grammars))]
    public void Validator_FixturesAndGeneratedInput_MatchesTheReferencePattern(string grammar)
    {
        var (reference, check, checkUtf8) = Grammar(grammar);
        var accepted = 0;
        var inputs = Inputs(grammar).ToList();

        foreach (var value in inputs)
        {
            var expected = reference(value);
            Assert.True(expected == check(value), $"{grammar} (UTF-16): {Show(value)} should be {(expected ? "valid" : "invalid")}");
            Assert.True(expected == checkUtf8(Encoding.UTF8.GetBytes(value)), $"{grammar} (UTF-8): {Show(value)} should be {(expected ? "valid" : "invalid")}");
            accepted += expected ? 1 : 0;
        }

        // The generator reaches both sides of the grammar.
        Assert.InRange(accepted, inputs.Count / 50, inputs.Count - (inputs.Count / 50));
    }

    [Fact]
    public void AtUriTryParse_FixturesAndGeneratedInput_MatchesTheReferenceComposition()
    {
        var accepted = 0;
        var inputs = Inputs("aturi").ToList();

        foreach (var value in inputs)
        {
            var expected = ReferenceAtUri(value);
            Assert.True(expected is not null == AtUri.TryParse(value, out var uri), $"{Show(value)} should be {(expected is null ? "invalid" : "valid")}");
            if (expected is not { } parts)
                continue;

            accepted++;
            Assert.Equal(value, uri!.Value);
            Assert.Equal(parts.Authority, uri.Authority);
            Assert.Equal(parts.Authority.StartsWith("did:", StringComparison.Ordinal) ? parts.Authority : parts.Authority.ToLowerInvariant(), uri.Repo.Value);
            Assert.Equal(parts.Collection, uri.Collection?.Value);
            Assert.Equal(parts.RecordKey, uri.RecordKey?.Value);
        }

        Assert.InRange(accepted, inputs.Count / 50, inputs.Count - (inputs.Count / 50));
    }

    public static TheoryData<string, string> JsonFixtures => new()
    {
        { nameof(Did), "did_syntax_valid.txt" },
        { nameof(Did), "did_syntax_invalid.txt" },
        { nameof(Handle), "handle_syntax_valid.txt" },
        { nameof(Handle), "handle_syntax_invalid.txt" },
        { nameof(Nsid), "nsid_syntax_valid.txt" },
        { nameof(Nsid), "nsid_syntax_invalid.txt" },
        { nameof(RecordKey), "recordkey_syntax_valid.txt" },
        { nameof(RecordKey), "recordkey_syntax_invalid.txt" },
        { nameof(Tid), "tid_syntax_valid.txt" },
        { nameof(Tid), "tid_syntax_invalid.txt" },
        { nameof(AtIdentifier), "atidentifier_syntax_valid.txt" },
        { nameof(AtIdentifier), "atidentifier_syntax_invalid.txt" },
        { nameof(AtUri), "aturi_syntax_valid.txt" },
        { nameof(AtUri), "aturi_syntax_invalid.txt" },
        { nameof(Cid), "cid_syntax_invalid.txt" },
    };

    [Theory]
    [MemberData(nameof(JsonFixtures))]
    public void Deserialize_FixtureValue_AgreesWithTryParse(string kind, string fileName)
    {
        var values = SyntaxFixtures.Values(fileName).Concat(kind switch
        {
            // Upper case is lower-cased on the UTF-8 path too; an @ prefix leaves it for the string one.
            nameof(Handle) => ["Alice.Bsky.Social", "@alice.bsky.social", "xn--ls8h.test"],
            nameof(Cid) => ["bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku"],
            _ => [],
        });

        foreach (var value in values)
        {
            var expected = TryParse(kind, value);
            foreach (var json in JsonForms(value))
            {
                var actual = TryDeserialize(kind, json);
                Assert.True(expected == actual, $"{kind} {Show(value)} as {json}: parsed '{expected}', read '{actual}'");
            }
        }
    }

    [Theory]
    [InlineData("datetime_syntax_valid.txt")]
    [InlineData("datetime_syntax_invalid.txt")]
    [InlineData("datetime_parse_invalid.txt")]
    public void Deserialize_DatetimeFixture_AgreesWithTheWireReading(string fileName)
    {
        foreach (var value in SyntaxFixtures.Values(fileName))
        {
            var expected = AtDatetime.FromWire(value);
            foreach (var json in JsonForms(value))
            {
                var actual = JsonSerializer.Deserialize<AtDatetime>(json, AtProtoJsonDefaults.Options);

                Assert.Equal(expected.ToString(), actual.ToString());
                Assert.Equal(expected.IsValid, actual.IsValid);
                Assert.Equal(expected.TryGetValue(out var expectedInstant), actual.TryGetValue(out var actualInstant));
                Assert.Equal(expectedInstant, actualInstant);
                Assert.Equal(expectedInstant.Offset, actualInstant.Offset);
            }
        }
    }

    [Fact]
    public void TryParseUtc_FixturesAndGeneratedTimes_AgreesWithDateTimeOffsetTryParse()
    {
        // Jetstream reads a frame's time through AtDatetime.TryParseUtc where that agrees with the
        // framework's parser, and through the framework otherwise.
        var random = new Random(190);
        var inputs = new[] { "datetime_syntax_valid.txt", "datetime_syntax_invalid.txt", "datetime_parse_invalid.txt" }
            .SelectMany(SyntaxFixtures.Values)
            .Concat(Enumerable.Range(0, 5000).Select(_ => GenerateDatetime(random)))
            .ToList();
        var agreed = 0;

        foreach (var value in inputs)
        {
            if (!AtDatetime.TryParseUtc(Encoding.UTF8.GetBytes(value), out var utc))
                continue;

            Assert.True(
                DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expected),
                $"{Show(value)} is read, but DateTimeOffset.TryParse rejects it");
            Assert.True(expected.UtcTicks == utc.Ticks, $"{Show(value)}: {utc:O}, not {expected.UtcDateTime:O}");
            Assert.Equal(DateTimeKind.Utc, utc.Kind);
            agreed++;
        }

        Assert.InRange(agreed, inputs.Count / 10, inputs.Count);
    }

    private static string GenerateDatetime(Random random)
    {
        int Pick(params int[] values) => values[random.Next(values.Length)];

        var year = random.Next(4) == 0 ? Pick(0, 1, 2, 1969, 1970, 9998, 9999) : random.Next(10000);
        var fraction = random.Next(3) == 0 ? "" : "." + Run(random, "0123456789", random.Next(0, 11));
        var zone = random.Next(6) switch
        {
            0 or 1 or 2 => "Z",
            3 => Pick('+', '-') == '+' ? $"+{random.Next(0, 25):00}:{Pick(0, 30, 45, 59, 60):00}" : $"-{random.Next(0, 25):00}:{Pick(0, 30, 45, 59, 60):00}",
            4 => Pick(0, 1) == 0 ? "+14:00" : "-14:00",
            _ => Pick(0, 1) == 0 ? "-00:00" : "+14:01",
        };

        return $"{year:0000}-{random.Next(0, 14):00}-{random.Next(0, 33):00}T{random.Next(0, 25):00}:{Pick(0, 30, 59, 60):00}:{Pick(0, 30, 59, 60):00}{fraction}{zone}";
    }

    // The value as a plain JSON string, which the converters read from its bytes, and with every
    // character escaped, which they read through the decoded string.
    private static IEnumerable<string> JsonForms(string value)
    {
        if (!value.Any(c => c is '"' or '\\' || char.IsControl(c) || char.IsSurrogate(c)))
            yield return $"\"{value}\"";

        yield return "\"" + string.Concat(value.Select(c => $"\\u{(int)c:x4}")) + "\"";
    }

    private static string? TryParse(string kind, string value) => kind switch
    {
        nameof(Did) => Did.TryParse(value, out var did) ? did.Value : null,
        nameof(Handle) => Handle.TryParse(value, out var handle) ? handle.Value : null,
        nameof(Nsid) => Nsid.TryParse(value, out var nsid) ? nsid.Value : null,
        nameof(RecordKey) => RecordKey.TryParse(value, out var rkey) ? rkey.Value : null,
        nameof(Tid) => Tid.TryParse(value, out var tid) ? tid.Value : null,
        nameof(AtIdentifier) => AtIdentifier.TryParse(value, out var identifier) ? identifier.Value : null,
        nameof(AtUri) => AtUri.TryParse(value, out var uri) ? $"{uri.Value}|{uri.Repo}|{uri.Collection}|{uri.RecordKey}" : null,
        nameof(Cid) => Cid.TryParse(value, out var cid) ? cid.Value : null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string? TryDeserialize(string kind, string json)
    {
        try
        {
            return kind switch
            {
                nameof(Did) => Read<Did>(json).Value,
                nameof(Handle) => Read<Handle>(json).Value,
                nameof(Nsid) => Read<Nsid>(json).Value,
                nameof(RecordKey) => Read<RecordKey>(json).Value,
                nameof(Tid) => Read<Tid>(json).Value,
                nameof(AtIdentifier) => Read<AtIdentifier>(json).Value,
                nameof(AtUri) => Read<AtUri>(json) is var uri ? $"{uri.Value}|{uri.Repo}|{uri.Collection}|{uri.RecordKey}" : null,
                nameof(Cid) => Read<Cid>(json).Value,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (JsonException)
        {
            return null;
        }

        static T Read<T>(string json) => JsonSerializer.Deserialize<T>(Encoding.UTF8.GetBytes(json), AtProtoJsonDefaults.Options)!;
    }

    private static (Func<string, bool> Reference, Func<string, bool> Check, Func<byte[], bool> CheckUtf8) Grammar(string grammar) => grammar switch
    {
        "did" => (ReferenceDid, s => IdentifierSyntax.IsDid(s.AsSpan()), b => IdentifierSyntax.IsDid<byte>(b)),
        "handle" => (ReferenceHandle, s => IdentifierSyntax.IsHandle(s.AsSpan(), out _), b => IdentifierSyntax.IsHandle<byte>(b, out _)),
        "nsid" => (ReferenceNsid, s => IdentifierSyntax.IsNsid(s.AsSpan()), b => IdentifierSyntax.IsNsid<byte>(b)),
        "recordkey" => (ReferenceRecordKey, s => IdentifierSyntax.IsRecordKey(s.AsSpan()), b => IdentifierSyntax.IsRecordKey<byte>(b)),
        "tid" => (ReferenceTid, s => IdentifierSyntax.IsTid(s.AsSpan()), b => IdentifierSyntax.IsTid<byte>(b)),
        _ => throw new ArgumentOutOfRangeException(nameof(grammar)),
    };

    // ── Input generation ──

    private const string LabelChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-";

    // Characters every grammar treats specially, or that none allows: separators, the DID and record
    // key extras, the @ prefix, URI delimiters, whitespace and non-ASCII.
    private const string EdgeChars = ".:%_~-@/#? \t\n\u00e9\u00a0\u4e2d0Zz";

    private static IEnumerable<string> Inputs(string grammar)
    {
        var random = new Random(190);
        var seeds = SeedFiles(grammar).SelectMany(SyntaxFixtures.Values).Distinct(StringComparer.Ordinal).ToList();

        foreach (var seed in seeds)
        {
            yield return seed;
            for (var i = 0; i < 25; i++)
                yield return Mutate(seed, random);
        }

        for (var i = 0; i < 2000; i++)
            yield return Generate(grammar, random);

        foreach (var value in AtTheLengthLimit(grammar))
        {
            yield return value;
            yield return value + "a";
        }
    }

    // Valid values exactly at the grammar's length limit, whose last part is short enough to take one
    // more character: the caller tries that too.
    private static string[] AtTheLengthLimit(string grammar) => grammar switch
    {
        "did" => ["did:plc:" + new string('a', 2048 - 8)],
        // 3 × 63 + 61 + 3 dots = 253.
        "handle" => [string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61))],
        // 4 × 63 + 61 + 4 dots = 317.
        "nsid" => [string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 63), new string('e', 61))],
        "recordkey" => [new string('a', 512)],
        _ => [],
    };

    private static string[] SeedFiles(string grammar) => grammar switch
    {
        "did" => ["did_syntax_valid.txt", "did_syntax_invalid.txt", "atidentifier_syntax_invalid.txt"],
        "handle" => ["handle_syntax_valid.txt", "handle_syntax_invalid.txt", "atidentifier_syntax_valid.txt"],
        "nsid" => ["nsid_syntax_valid.txt", "nsid_syntax_invalid.txt"],
        "recordkey" => ["recordkey_syntax_valid.txt", "recordkey_syntax_invalid.txt", "tid_syntax_valid.txt"],
        "tid" => ["tid_syntax_valid.txt", "tid_syntax_invalid.txt"],
        "aturi" => ["aturi_syntax_valid.txt", "aturi_syntax_invalid.txt"],
        _ => throw new ArgumentOutOfRangeException(nameof(grammar)),
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
                    var length = Math.Min(random.Next(1, 70), chars.Length - from);
                    chars.Insert(at, chars.ToString(from, length));
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

    // Dot-separated labels, sized to land on either side of the per-label limit and, now and then, of
    // the total one.
    private static string Labels(Random random, int count, int maxLength)
    {
        var labels = Enumerable.Range(0, count).Select(_ => Run(random, LabelChars, random.Next(8) switch
        {
            0 => 0,
            1 => random.Next(61, 65),
            _ => random.Next(1, 12),
        }));

        var value = string.Join('.', labels);
        if (random.Next(10) == 0 && value.Length > 0)
        {
            // Pad a middle label out so the whole lands at the limit or one past it.
            var target = maxLength + random.Next(-1, 2);
            var first = value.IndexOf('.');
            if (first > 0 && target > value.Length)
                value = value.Insert(first, Run(random, "abcdefghij", Math.Min(target - value.Length, 60)));
        }

        return value;
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

    private static string Show(string value) => JsonSerializer.Serialize(value);
}
