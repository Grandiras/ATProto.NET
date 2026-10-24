using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

// The shared-secret commitment: KEM decapsulation, commit and record MACs, their headers, and
// repository exports (spaces/kem.json, space-mac.json, star.json).
public abstract partial class SpacesConformance<TAdapter>
{
    private const string MacFile = "spaces/space-mac.json";

    private static readonly IReadOnlyDictionary<string, JsonNode> KemCases = Vectors.Cases("spaces/kem.json");
    private static readonly IReadOnlyDictionary<string, JsonNode> KemRefusals = Vectors.Cases("spaces/kem.json", "refusals");
    private static readonly IReadOnlyDictionary<string, JsonNode> CommitMacCases = Vectors.Cases(MacFile, "commitMacs");
    private static readonly IReadOnlyDictionary<string, JsonNode> RecordMacCases = Vectors.Cases(MacFile, "recordMacs");
    private static readonly IReadOnlyDictionary<string, JsonNode> FormattedCases = Vectors.Cases(MacFile, "formatted");
    private static readonly IReadOnlyDictionary<string, JsonNode> ParsingCases = Vectors.Cases(MacFile, "parsing");
    private static readonly IReadOnlyDictionary<string, JsonNode> StarCases = Vectors.Cases("spaces/star.json");

    /// <summary>The KEM vectors, by ID.</summary>
    public static TheoryData<string> Kems() => new(KemCases.Keys);

    /// <summary>The refused KEM keys, by ID.</summary>
    public static TheoryData<string> RefusedKems() => new(KemRefusals.Keys);

    /// <summary>The commit MAC vectors, by ID.</summary>
    public static TheoryData<string> CommitMacs() => new(CommitMacCases.Keys);

    /// <summary>The record MAC vectors, by ID.</summary>
    public static TheoryData<string> RecordMacs() => new(RecordMacCases.Keys);

    /// <summary>The header formatting vectors, by ID.</summary>
    public static TheoryData<string> FormattedHeaders() => new(FormattedCases.Keys);

    /// <summary>The header parsing vectors, by ID.</summary>
    public static TheoryData<string> ParsedHeaders() => new(ParsingCases.Keys);

    /// <summary>The repository export vectors, by ID.</summary>
    public static TheoryData<string> Exports() => new(StarCases.Keys);

    /// <summary>The author's KEM key derives the reference secret from the key share, and encapsulating to it round-trips.</summary>
    [Theory]
    [Trait("Spec", "SPC-54"), Trait("Spec", "SPC-55")]
    [MemberData(nameof(Kems))]
    public void KemDecapsulate_Vector_DerivesTheReferenceSecret(string vectorId)
    {
        Vector(vectorId);
        var vector = KemCases[vectorId];

        Assert.Equal((string)vector["sharedSecret"]!, Convert.ToHexStringLower(Adapter.KemDecapsulate((string)vector["authorPrivateKey"]!, (string)vector["keyShare"]!) ?? []));
        var encapsulated = Adapter.KemEncapsulate((string)vector["authorKey"]!);
        Assert.NotNull(encapsulated);
        Assert.Equal(encapsulated.Value.Secret, Adapter.KemDecapsulate((string)vector["authorPrivateKey"]!, encapsulated.Value.KeyShare));
    }

    /// <summary>A key share or author key that is not a P-256 <c>did:key</c> is refused.</summary>
    [Theory]
    [Trait("Spec", "SPC-54"), Trait("Spec", "SPC-55")]
    [MemberData(nameof(RefusedKems))]
    public void Kem_RefusedKey_IsRefused(string vectorId)
    {
        Vector(vectorId);
        var vector = KemRefusals[vectorId];

        if ((string)vector["operation"]! == "decapsulate") Assert.Null(Adapter.KemDecapsulate((string)vector["authorPrivateKey"]!, (string)vector["keyShare"]!));
        else Assert.Null(Adapter.KemEncapsulate((string)vector["authorKey"]!));
    }

    /// <summary>A commit MAC covers the length-prefixed space, repo and rev, then data, as the reference computes it.</summary>
    [Theory]
    [Trait("Spec", "SPC-56")]
    [MemberData(nameof(CommitMacs))]
    public void SpaceMac_CommitVector_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var vector = CommitMacCases[vectorId];
        var commit = vector["commit"]!;

        var mac = Adapter.SpaceMac(Convert.FromHexString((string)vector["sharedSecret"]!), "atproto-space-repo",
            [(string)commit["space"]!, (string)commit["repo"]!, (string)commit["rev"]!], Convert.FromHexString((string)commit["data"]!));

        Assert.Equal((string?)vector["mac"], mac is null ? null : Convert.ToHexStringLower(mac));
    }

    /// <summary>A record MAC covers the length-prefixed URI, then the binary CID.</summary>
    [Theory]
    [Trait("Spec", "SPC-56")]
    [MemberData(nameof(RecordMacs))]
    public void SpaceMac_RecordVector_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var vector = RecordMacCases[vectorId];

        var mac = Adapter.SpaceMac(Convert.FromHexString((string)vector["sharedSecret"]!), "atproto-space-record",
            [(string)vector["uri"]!], Convert.FromHexString((string)vector["cidBytes"]!));

        Assert.Equal((string)vector["mac"]!, Convert.ToHexStringLower(mac ?? []));
    }

    /// <summary>Both headers are written as the reference writes them.</summary>
    [Theory]
    [Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    [MemberData(nameof(FormattedHeaders))]
    public void FormatMacHeader_Vector_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var vector = FormattedCases[vectorId];
        var type = (string)vector["type"]!;

        var value = (string)vector["header"]! == "want"
            ? Adapter.FormatWantMac(type, (string)vector["keyShare"]!)
            : Adapter.FormatMac(type, Convert.FromHexString((string)vector["mac"]!));

        Assert.Equal((string)vector["value"]!, value);
    }

    /// <summary>A header of the expected type and version yields its key share or MAC; anything else is refused.</summary>
    [Theory]
    [Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    [MemberData(nameof(ParsedHeaders))]
    public void ParseMacHeader_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = ParsingCases[vectorId];
        var (value, type, expect) = ((string?)vector["value"], (string)vector["type"]!, vector["expect"]!);

        if ((string)vector["header"]! == "want")
            Assert.Equal((bool)expect["ok"]! ? (string)expect["keyShare"]! : null, Adapter.ParseWantMac(value, type));
        else
        {
            var mac = Adapter.ParseMac(value, type);
            Assert.Equal((bool)expect["ok"]! ? (string)expect["mac"]! : null, mac is null ? null : Convert.ToHexStringLower(mac));
        }
    }

    /// <summary>An export verifies as the expected commit and records, or is refused as a whole.</summary>
    [Theory]
    [Trait("Spec", "SPC-59"), Trait("Spec", "SPC-60"), Trait("Spec", "SPC-17"), Trait("Spec", "SPC-77")]
    [MemberData(nameof(Exports))]
    public void VerifySpaceRepo_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = StarCases[vectorId];
        var (request, expect) = (vector["request"]!, vector["expect"]!);

        var verdict = Adapter.VerifySpaceRepo(Vectors.Hex(vector["archive"]), (string)request["repo"]!, (string)request["space"]!,
            Convert.FromHexString((string)vector["sharedSecret"]!), Convert.FromHexString((string)vector["mac"]!));

        if (!(bool)expect["ok"]!)
        {
            Assert.Null(verdict);
            return;
        }
        Assert.NotNull(verdict);
        var commit = expect["commit"]!;
        Assert.Equal(((long)commit["ver"]!, (string)commit["repo"]!, (string)commit["space"]!, (string)commit["rev"]!, (string)commit["data"]!),
            (verdict.Ver, verdict.Repo, verdict.Space, verdict.Rev, Convert.ToHexStringLower(verdict.Data)));
        Assert.Equal(expect["records"]!.AsArray().Select(r => ((string)r!["path"]!, (string)r["cid"]!)), verdict.Records);
    }
}
