using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Star;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>Writing a space repository export and verifying it as a stream.</summary>
public sealed class SpaceRepoExportTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Did Repo = SpaceCommitTests.Repo;
    private static readonly SpaceUri Space = SpaceCommitTests.Space;

    private static readonly (string Path, ReadOnlyMemory<byte> Record)[] Records =
    [
        ("app.bsky.feed.post/3k2a", SpaceCommitTests.Drisl("""{"text":"a"}""")),
        ("app.bsky.feed.post/3k2b", SpaceCommitTests.Drisl("""{"text":"b","n":1}""")),
        ("com.example.note/self", SpaceCommitTests.Drisl("""{"$type":"com.example.note","ref":{"$link":"bafyreidefdycgbfy3oglcb6ism3eqhyp5llsrpzxjsuac2gsy4mtrtx244"}}""")),
    ];

    private readonly SharedSecret _secret;

    public SpaceRepoExportTests()
    {
        using var author = KemPrivateKey.Generate(KemAlgorithm.Default);
        _secret = author.PublicKey.Encapsulate().Secret;
    }

    public void Dispose() => _secret.Dispose();

    [Fact]
    [Trait("Spec", "SPC-59"), Trait("Spec", "SPC-60")]
    public async Task WriteAsync_ThenVerify_ReturnsTheCommitAndEveryRecord()
    {
        var commit = CommitOf(Records);

        var repo = await Verify(await Write(commit, Records), SpaceMac.ComputeCommit(_secret, commit));

        Assert.Equal((commit.Repo, commit.Space, commit.Rev), (repo.Commit.Repo, repo.Commit.Space, Tid.Parse(repo.Commit.Rev)));
        Assert.Equal(commit.Data.ToArray(), repo.Commit.Data.ToArray());
        Assert.Equal(Records.Select(r => r.Path), repo.Records.Select(r => r.Path));
        Assert.Equal(Records.Select(r => Cid.Compute(CidCodec.DagCbor, r.Record.Span)), repo.Records.Select(r => r.Cid));
        Assert.Equal(Records.Select(r => Convert.ToHexString(r.Record.Span)), repo.Records.Select(r => Convert.ToHexString(r.Record.Span)));
    }

    [Fact]
    [Trait("Spec", "SPC-59")]
    public async Task WriteAsync_Metadata_IsTheCommitsStarMetadata()
    {
        var commit = CommitOf(Records);
        using var star = new MemoryStream(await Write(commit, Records));

        var header = await StarReader.ReadHeaderAsync(star, cancellationToken: Ct);

        Assert.Equal(commit.ToStarMetadata(), header.Drisl.ToArray());
    }

    [Fact]
    [Trait("Spec", "SPC-60")]
    public async Task VerifyAsync_EmptyRepository_VerifiesWithNoRecords()
    {
        var commit = CommitOf([]);

        var repo = await Verify(await Write(commit, []), SpaceMac.ComputeCommit(_secret, commit));

        Assert.Empty(repo.Records);
        Assert.Equal(LtHash.Empty().Digest(), repo.Commit.Data.ToArray());
    }

    [Theory]
    [Trait("Spec", "SPC-60"), Trait("Spec", "SPC-77")]
    [InlineData("did:example:bob", "at://did:example:space/space/app.bsky.group/test")]
    [InlineData("did:example:alice", "at://did:example:space/space/app.bsky.group/other")]
    public async Task VerifyAsync_AnotherRepositoryRequested_ThrowsWrongRepository(string repo, string space)
    {
        var commit = CommitOf(Records);
        var star = await Write(commit, Records);

        // Checked before the MAC: the MAC here is valid for the commit, which is just not the one asked for.
        await AssertFails(SpaceVerificationError.WrongRepository, Verify(star, SpaceMac.ComputeCommit(_secret, commit), Did.Parse(repo), SpaceUri.Parse(space)));
    }

    [Fact]
    [Trait("Spec", "SPC-60"), Trait("Spec", "SPC-77")]
    public async Task VerifyAsync_OtherCommitVersion_ThrowsWrongRepository()
    {
        var future = SpaceCommit.FromStarMetadata(SpaceCommitTests.Metadata(ver: "2"));
        var star = Concat(await Header(SpaceCommitTests.Metadata(ver: "2")));

        await AssertFails(SpaceVerificationError.WrongRepository, Verify(star, SpaceMac.ComputeCommit(_secret, future)));
    }

    [Fact]
    [Trait("Spec", "SPC-56"), Trait("Spec", "SPC-60")]
    public async Task VerifyAsync_WrongMac_ThrowsBadMac()
    {
        var commit = CommitOf(Records);
        var star = await Write(commit, Records);
        using var otherAuthor = KemPrivateKey.Generate(KemAlgorithm.Default);
        using var other = otherAuthor.PublicKey.Encapsulate().Secret;
        var mac = SpaceMac.ComputeCommit(_secret, commit);

        await AssertFails(SpaceVerificationError.BadMac, Verify(star, SpaceMac.ComputeCommit(other, commit)));
        await AssertFails(SpaceVerificationError.BadMac, Verify(star, mac[..31]));
        await AssertFails(SpaceVerificationError.BadMac, Verify(star, ReadOnlyMemory<byte>.Empty));
    }

    [Theory]
    [Trait("Spec", "SPC-60")]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("altered")]
    public async Task VerifyAsync_RecordsChangedAfterTheCommit_ThrowsDigestMismatch(string change)
    {
        var commit = CommitOf(Records);
        (string, ReadOnlyMemory<byte>)[] served = change switch
        {
            "added" => [.. Records, ("com.example.note/zzz", SpaceCommitTests.Drisl("""{"text":"late"}"""))],
            "removed" => [Records[0], Records[2]],
            _ => [Records[0], (Records[1].Path, SpaceCommitTests.Drisl("""{"text":"b","n":2}""")), Records[2]],
        };

        await AssertFails(SpaceVerificationError.DigestMismatch, Verify(await Write(commit, served), SpaceMac.ComputeCommit(_secret, commit)));
    }

    public static TheoryData<string, string> MalformedTails() => new()
    {
        { "out of order", Hex(Pair(Records[1])) + Hex(Pair(Records[0])) },
        { "duplicate", Hex(Pair(Records[0])) + Hex(Pair(Records[0])) },
        { "not a record path", Hex(Pair(("app.bsky.feed.post", Records[0].Record))) },
        { "record not a map", Hex(Pair(("app.bsky.feed.post/3k2a", new byte[] { 0x01 }))) },
        { "record keys out of order", Hex(Pair(("app.bsky.feed.post/3k2a", Convert.FromHexString("a2616202616101")))) },
        { "record with an integer key", Hex(Pair(("app.bsky.feed.post/3k2a", Convert.FromHexString("a10101")))) },
        { "record with a float", Hex(Pair(("app.bsky.feed.post/3k2a", Convert.FromHexString("a16161fb3ff8000000000000")))) },
        { "record with a tag other than 42", Hex(Pair(("app.bsky.feed.post/3k2a", Convert.FromHexString("a16161c101")))) },
        { "bytes after a record", Hex(Pair(("app.bsky.feed.post/3k2a", Convert.FromHexString("a1616101f6")))) },
        { "truncated inside a key", "1761" },
        { "a key length and nothing more", "17" },
    };

    [Theory]
    [Trait("Spec", "SPC-17"), Trait("Spec", "SPC-59"), Trait("Spec", "SPC-60")]
    [MemberData(nameof(MalformedTails))]
    public async Task VerifyAsync_MalformedEntries_ThrowsMalformed(string problem, string tailHex)
    {
        var commit = CommitOf([]);
        var star = Concat(await Header(commit.ToStarMetadata()), Convert.FromHexString(tailHex));

        var error = await Assert.ThrowsAsync<SpaceVerificationException>(async () => await Verify(star, SpaceMac.ComputeCommit(_secret, commit)));
        Assert.True(error.Error == SpaceVerificationError.Malformed, $"{problem}: {error.Error}");
    }

    [Theory]
    [Trait("Spec", "SPC-59"), Trait("Spec", "SPC-60")]
    [InlineData("")] // nothing at all
    [InlineData("2a6c0200")] // another STAR version
    [InlineData("3aa265726f6f7473")] // a CAR
    [InlineData("2a6c0100" + "04a1616101")] // metadata without $type
    public async Task VerifyAsync_MalformedArchiveOrMetadata_ThrowsMalformed(string hex) =>
        await AssertFails(SpaceVerificationError.Malformed, Verify(Convert.FromHexString(hex), new byte[32]));

    [Fact]
    [Trait("Spec", "SPC-59"), Trait("Spec", "SPC-60")]
    public async Task VerifyAsync_MetadataOfAnotherType_ThrowsMalformed() =>
        await AssertFails(SpaceVerificationError.Malformed, Verify(await Header(SpaceCommitTests.Drisl("""{"$type":"atproto-space-record"}""")), new byte[32]));

    [Theory]
    [Trait("Spec", "SPC-60")]
    [InlineData(nameof(StarReaderOptions.MaxEntries))]
    [InlineData(nameof(StarReaderOptions.MaxEntryBytes))]
    [InlineData(nameof(StarReaderOptions.MaxTotalBytes))]
    [InlineData(nameof(StarReaderOptions.MaxHeaderBytes))]
    public async Task VerifyAsync_LimitExceeded_ThrowsLimitExceeded(string limit)
    {
        var commit = CommitOf(Records);
        var options = limit switch
        {
            nameof(StarReaderOptions.MaxEntries) => new StarReaderOptions { MaxEntries = 2 },
            nameof(StarReaderOptions.MaxEntryBytes) => new StarReaderOptions { MaxEntryBytes = 12 },
            nameof(StarReaderOptions.MaxTotalBytes) => new StarReaderOptions { MaxTotalBytes = 40 },
            _ => new StarReaderOptions { MaxHeaderBytes = 64 },
        };

        var error = await Assert.ThrowsAsync<SpaceVerificationException>(async () => await Verify(await Write(commit, Records), SpaceMac.ComputeCommit(_secret, commit), options: options));

        Assert.Equal(SpaceVerificationError.LimitExceeded, error.Error);
        Assert.Equal(limit, Assert.IsType<DataLimitExceededException>(error.InnerException).Limit);
    }

    [Fact]
    [Trait("Spec", "SPC-59")]
    public async Task WriteAsync_RecordsOutOfOrder_ThrowsArgumentException() =>
        await Assert.ThrowsAsync<ArgumentException>(async () => await Write(CommitOf(Records), [Records[1], Records[0]]));

    [Fact]
    public async Task VerifyAndWrite_NullArguments_ThrowArgumentNullException()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SpaceRepoExport.WriteAsync(stream, null!, Records.ToAsyncEnumerable(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SpaceRepoExport.VerifyAsync(null!, Repo, Space, _secret, new byte[32], cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await SpaceRepoExport.VerifyAsync(stream, Repo, Space, null!, new byte[32], cancellationToken: Ct));
    }

    private static SpaceCommit CommitOf((string Path, ReadOnlyMemory<byte> Record)[] records)
    {
        var hash = LtHash.Empty();
        foreach (var (path, record) in records)
        {
            var slash = path.IndexOf('/');
            hash.Add(Nsid.Parse(path[..slash]), RecordKey.Parse(path[(slash + 1)..]), Cid.Compute(CidCodec.DagCbor, record.Span));
        }
        return new(Repo, Space, SpaceCommitTests.Rev, hash.Digest());
    }

    private static async Task<byte[]> Write(SpaceCommit commit, (string, ReadOnlyMemory<byte>)[] records)
    {
        using var stream = new MemoryStream();
        await SpaceRepoExport.WriteAsync(stream, commit, records.ToAsyncEnumerable(), Ct);
        return stream.ToArray();
    }

    // An archive of the metadata alone, to append hand-made entries the writer would refuse.
    private static async Task<byte[]> Header(byte[] metadata)
    {
        using var stream = new MemoryStream();
        await StarWriter.WriteAsync(stream, metadata, AsyncEnumerable.Empty<(string, ReadOnlyMemory<byte>)>(), Ct);
        return stream.ToArray();
    }

    private async Task<VerifiedSpaceRepo> Verify(byte[] star, ReadOnlyMemory<byte> mac, Did? repo = null, SpaceUri? space = null, StarReaderOptions? options = null)
    {
        using var stream = new MemoryStream(star);
        return await SpaceRepoExport.VerifyAsync(stream, repo ?? Repo, space ?? Space, _secret, mac, options, Ct);
    }

    private static async Task AssertFails(SpaceVerificationError expected, Task verification) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<SpaceVerificationException>(() => verification)).Error);

    private static byte[] Pair((string Path, ReadOnlyMemory<byte> Record) entry) =>
        [(byte)entry.Path.Length, .. Encoding.ASCII.GetBytes(entry.Path), (byte)entry.Record.Length, .. entry.Record.Span];

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}
