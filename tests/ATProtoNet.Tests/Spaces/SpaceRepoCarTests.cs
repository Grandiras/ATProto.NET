using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Repo;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceRepoCarTests
{
    private static readonly SpaceUri _space =
        SpaceUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default");

    private const string Author = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string Rev = "3l6oveex3ii2l";

    private static SpaceRepoRecord Record(string collection, string rkey, string text)
    {
        var value = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["$type"] = collection,
            ["text"] = text,
        });

        return SpaceRepoRecord.Create(collection, rkey, value);
    }

    private static (byte[] Car, AtProtoKey Key, List<SpaceRepoRecord> Records) BuildRepo(
        params (string Collection, string Rkey, string Text)[] entries)
    {
        var key = AtProtoCrypto.GenerateP256Key();
        var records = entries.Select(e => Record(e.Collection, e.Rkey, e.Text)).ToList();

        var commit = SpaceRepoCommit
            .FromRecords(records.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        return (SpaceRepoCar.Serialize(commit, records), key, records);
    }

    [Fact]
    public void SerializeThenVerify_RoundTripsTheWholeRepo()
    {
        var (car, key, records) = BuildRepo(
            ("com.atmoboards.thread", "aaa", "first"),
            ("com.atmoboards.reply", "bbb", "second"),
            ("com.atmoboards.reply", "ccc", "third"));
        using var _ = key;

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Equal(Rev, verified.Commit.Rev);
        Assert.Equal(records.Count, verified.Index.Count);
        Assert.Equal(records.Count, verified.Records.Count);
        Assert.Equal(
            records.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal),
            verified.Records.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Serialize_DeclaresTheCommitAndIndexAsItsTwoRoots()
    {
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;

        var reader = CarReader.FromBytes(car);

        Assert.Equal(2, reader.Roots.Count);
        Assert.Equal(reader.Roots[0], reader.Blocks[0].Cid);
        Assert.Equal(reader.Roots[1], reader.Blocks[1].Cid);
    }

    [Fact]
    public void Serialize_OrdersRecordBlocksToMatchTheIndex()
    {
        // A consumer walks the index and the blocks in lockstep, so the block order has to be
        // the canonical map-key order the index was encoded in — length-first, then bytewise.
        var (car, key, _) = BuildRepo(
            ("com.example.n", "z", "short path"),
            ("com.example.note", "aaa", "long path"),
            ("com.example.n", "a", "shortest path"));
        using var _k = key;

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Equal(
            ["com.example.n/a", "com.example.n/z", "com.example.note/aaa"],
            verified.Records.Select(r => r.Path));
    }

    [Fact]
    public void Verify_IndexAuthenticatesAgainstTheCommitWithoutReadingRecords()
    {
        var (car, key, records) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.True(SpaceRepoCommit.FromIndex(verified.Index).Matches(verified.Commit));
        Assert.Equal(records[0].Cid, verified.Index[0].Value);
    }

    [Fact]
    public void Verify_IndexOnlyCar_SucceedsWhenValuesAreNotExpected()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var records = new[] { Record("com.atmoboards.thread", "aaa", "first") };
        var commit = SpaceRepoCommit
            .FromRecords(records.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, records, excludeValues: true);

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey(), expectValues: false);

        Assert.Empty(verified.Records);
        // The index is still fully authenticated — that is what makes excludeValues useful for
        // diffing against a local copy.
        Assert.Single(verified.Index);
        Assert.True(SpaceRepoCommit.FromIndex(verified.Index).Matches(verified.Commit));
    }

    [Fact]
    public void Verify_IndexOnlyCar_FailsWhenValuesAreExpected()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var records = new[] { Record("com.atmoboards.thread", "aaa", "first") };
        var commit = SpaceRepoCommit
            .FromRecords(records.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, records, excludeValues: true);

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));
    }

    [Fact]
    public void Verify_EmptyRepo_Succeeds()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, []);
        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Empty(verified.Index);
        Assert.Empty(verified.Records);
    }

    [Fact]
    public void Verify_WithTheWrongSigningKey_Throws()
    {
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;
        using var other = AtProtoCrypto.GenerateP256Key();

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, other.ToDidKey()));

        Assert.Contains("commit failed verification", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_ForTheWrongSpace_Throws()
    {
        // The space is bound into the commit context, so a repo served for one space cannot be
        // passed off as the same account's repo in another.
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;
        var otherSpace = SpaceUri.Create(_space.Authority, _space.SpaceType, "other");

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, otherSpace, Author, key.ToDidKey()));
    }

    [Fact]
    public void Verify_ForTheWrongAuthor_Throws()
    {
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, "did:plc:ewvi7nxzyoun6zhxrhs64oiz", key.ToDidKey()));
    }

    [Fact]
    public void Verify_IndexThatDoesNotMatchTheCommit_Throws()
    {
        // A commit signed over one set of records, served with an index describing another.
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var claimed = new[] { Record("com.atmoboards.thread", "aaa", "first") };
        var actual = new[] { Record("com.atmoboards.thread", "aaa", "tampered") };

        var commit = SpaceRepoCommit
            .FromRecords(claimed.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, actual);

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("does not match the commit hash", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_CarWithASingleRoot_Throws()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(_space, Author, Rev), key);
        var commitBytes = commit.ToDagCbor();
        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBytes);

        var car = CarWriter.Write(commitCid, new[] { new CarBlock(commitCid, commitBytes) });

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("2 CAR roots", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Garbage_ThrowsVerificationExceptionRatherThanLeakingTheDecoder()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(new byte[] { 0xff, 0x00, 0x12 }, _space, Author, key.ToDidKey()));
    }

    [Fact]
    public void Serialize_LastWriteForAPathWins()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var stale = Record("com.atmoboards.thread", "aaa", "stale");
        var current = Record("com.atmoboards.thread", "aaa", "current");

        var commit = SpaceRepoCommit
            .FromRecords([(current.Collection, current.Rkey, current.Cid)])
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, [stale, current]);
        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Single(verified.Records);
        Assert.Equal(current.Cid, verified.Records[0].Cid);
    }
}
