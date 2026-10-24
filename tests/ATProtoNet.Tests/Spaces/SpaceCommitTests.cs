using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>The space commit and its STAR metadata codec.</summary>
public sealed class SpaceCommitTests
{
    internal static readonly Did Repo = Did.Parse("did:example:alice");
    internal static readonly SpaceUri Space = SpaceUri.Parse("at://did:example:space/space/app.bsky.group/test");
    internal static readonly Tid Rev = Tid.Parse("3kbcq3p7ad422");
    internal static readonly byte[] Data = [.. Enumerable.Repeat((byte)1, 32)];

    private const string DataBase64 = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE";

    [Fact]
    [Trait("Spec", "SPC-77")]
    public void Constructor_Fields_AreTheCommitOfTheCurrentVersion()
    {
        var data = (byte[])Data.Clone();

        var commit = new SpaceCommit(Repo, Space, Rev, data);
        data[0] = 0xFF;

        Assert.Equal(1, SpaceCommit.CurrentVersion);
        Assert.Equal((1L, Repo, Space, Rev), (commit.Ver, commit.Repo, commit.Space, Tid.Parse(commit.Rev)));
        Assert.Equal(Data, commit.Data.ToArray());
    }

    [Theory]
    [Trait("Spec", "SPC-77")]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_DataNot32Bytes_ThrowsArgumentException(int length) =>
        Assert.Throws<ArgumentException>(() => new SpaceCommit(Repo, Space, Rev, new byte[length]));

    [Fact]
    public void Constructor_NullIdentifier_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SpaceCommit(null!, Space, Rev, Data));
        Assert.Throws<ArgumentNullException>(() => new SpaceCommit(Repo, null!, Rev, Data));
    }

    [Fact]
    [Trait("Spec", "SPC-59"), Trait("Spec", "SPC-77")]
    public void ToStarMetadata_IsCanonicalDrislOfTheTypeAndTheFields()
    {
        var expected = Drisl($$$"""{"$type":"atproto-space-repo","ver":1,"repo":"{{{Repo}}}","space":"{{{Space}}}","rev":"{{{Rev}}}","data":{"$bytes":"{{{DataBase64}}}"}}""");

        Assert.Equal(expected, new SpaceCommit(Repo, Space, Rev, Data).ToStarMetadata());
    }

    [Fact]
    [Trait("Spec", "SPC-59"), Trait("Spec", "SPC-77")]
    public void FromStarMetadata_WrittenMetadata_RoundTrips()
    {
        var commit = SpaceCommit.FromStarMetadata(new SpaceCommit(Repo, Space, Rev, Data).ToStarMetadata());

        Assert.Equal((1L, Repo, Space, Rev), (commit.Ver, commit.Repo, commit.Space, Tid.Parse(commit.Rev)));
        Assert.Equal(Data, commit.Data.ToArray());
        Assert.True(commit.Describes(Repo, Space));
    }

    [Fact]
    [Trait("Spec", "SPC-77")]
    public void FromStarMetadata_UnknownFields_AreIgnored()
    {
        var commit = SpaceCommit.FromStarMetadata(Metadata(""","later":{"a":[1,2]},"z":"x" """));

        Assert.True(commit.Describes(Repo, Space));
    }

    [Fact]
    [Trait("Spec", "SPC-77")]
    public void FromStarMetadata_OtherVersion_ReadsButDoesNotDescribe()
    {
        var commit = SpaceCommit.FromStarMetadata(Metadata(ver: "2"));

        Assert.Equal(2, commit.Ver);
        Assert.False(commit.Describes(Repo, Space));
    }

    public static TheoryData<string, string> MalformedMetadata() => new()
    {
        { "no $type", Json(type: null) },
        { "another $type", Json(type: "\"atproto-space-record\"") },
        { "no ver", Json(ver: null) },
        { "ver a string", Json(ver: "\"1\"") },
        { "no repo", Json(repo: null) },
        { "repo not a DID", Json(repo: "\"alice\"") },
        { "no space", Json(space: null) },
        { "space an AT URI", Json(space: "\"at://did:example:space/app.bsky.group/test\"") },
        { "no rev", Json(rev: null) },
        { "rev not a string", Json(rev: "1") },
        { "rev longer than a MAC context field", Json(rev: $"\"{new string('r', 65536)}\"") },
        { "no data", Json(data: null) },
        { "data 31 bytes", Json(data: "{\"$bytes\":\"AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB\"}") },
        { "data a string", Json(data: $"\"{DataBase64}\"") },
    };

    [Theory]
    [Trait("Spec", "SPC-60"), Trait("Spec", "SPC-77")]
    [MemberData(nameof(MalformedMetadata))]
    public void FromStarMetadata_MalformedRow_ThrowsMalformed(string problem, string json)
    {
        var error = Assert.Throws<SpaceVerificationException>(() => SpaceCommit.FromStarMetadata(Drisl(json)));

        Assert.True(error.Error == SpaceVerificationError.Malformed, problem);
    }

    [Theory]
    [Trait("Spec", "SPC-60")]
    [InlineData("")] // nothing
    [InlineData("01")] // not a map
    [InlineData("a2616101616202")] // not this map: a, b
    [InlineData("a2616202616101")] // keys out of canonical order
    [InlineData("a1616101f6")] // bytes after the map
    [InlineData("a10101")] // an integer key
    [InlineData("a165247479706501")] // $type an integer
    public void FromStarMetadata_NotStrictDrisl_ThrowsMalformed(string hex) =>
        Assert.Equal(SpaceVerificationError.Malformed, Assert.Throws<SpaceVerificationException>(() => SpaceCommit.FromStarMetadata(Convert.FromHexString(hex))).Error);

    [Fact]
    [Trait("Spec", "SPC-77")]
    public void Describes_RequestedRepository_OnlyWhenRepoAndSpaceMatch()
    {
        var commit = new SpaceCommit(Repo, Space, Rev, Data);

        Assert.True(commit.Describes(Did.Parse("did:example:alice"), SpaceUri.Parse("at://did:example:space/space/app.bsky.group/test")));
        Assert.False(commit.Describes(Did.Parse("did:example:bob"), Space));
        Assert.False(commit.Describes(Repo, SpaceUri.Parse("at://did:example:space/space/app.bsky.group/other")));
        Assert.False(commit.Describes(Repo, SpaceUri.Parse("at://did:example:other/space/app.bsky.group/test")));
    }

    internal static byte[] Metadata(string extra = "", string ver = "1") => Drisl(Json(ver: ver, extra: extra));

    private static string Json(string? type = "\"atproto-space-repo\"", string? ver = "1", string? repo = $"\"did:example:alice\"",
        string? space = "\"at://did:example:space/space/app.bsky.group/test\"", string? rev = "\"3kbcq3p7ad422\"",
        string? data = $"{{\"$bytes\":\"{DataBase64}\"}}", string extra = "")
    {
        var fields = new[] { ("$type", type), ("ver", ver), ("repo", repo), ("space", space), ("rev", rev), ("data", data) }
            .Where(f => f.Item2 is not null).Select(f => $"\"{f.Item1}\":{f.Item2}");
        return $"{{{string.Join(',', fields)}{extra}}}";
    }

    internal static byte[] Drisl(string json) => DrislJson.ToDrisl(Encoding.UTF8.GetBytes(json));
}
