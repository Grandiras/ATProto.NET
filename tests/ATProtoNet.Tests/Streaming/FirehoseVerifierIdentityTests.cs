using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// How signature verification gets its keys: from a cache that costs no network on a hit,
/// refetched once when a signature fails, and dropped on <c>#identity</c>.
/// </summary>
public sealed class FirehoseVerifierIdentityTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private static readonly Did RepoDid = Did.Parse(DidText);

    private readonly AtProtoKey _key = AtProtoCrypto.GenerateK256Key();
    private readonly AtProtoKey _rotated = AtProtoCrypto.GenerateK256Key();
    private readonly ManualClock _clock = new();
    private AtProtoKey _published;

    public FirehoseVerifierIdentityTests() => _published = _key;

    public void Dispose()
    {
        _key.Dispose();
        _rotated.Dispose();
    }

    /// <summary>A verifier over a real <see cref="CachingDidResolver"/> and <see cref="PlcClient"/>, counting directory requests.</summary>
    private (FirehoseVerifier Verifier, ScriptedHandler Directory, CachingDidResolver Cache) Create()
    {
        var directory = new ScriptedHandler(_ => ScriptedHandler.Json(DidDocs.Json(DidText, "atproto.com", signingKey: _published.ToDidKey())));
        var plc = new PlcClient(new HttpClient(directory), new Uri("https://plc.directory"));
        var cache = new CachingDidResolver(new DidResolver(plc, new DidWebResolver()), timeProvider: _clock);
        return (new FirehoseVerifier(cache), directory, cache);
    }

    private static CommitEvent Commit(AtProtoKey signer, string rev)
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("app.bsky.feed.post/3l6oveex3ii2l", CidComputation.ComputeBinaryForDagCbor([0xA0]));
        var (root, blocks) = mst.Serialize();
        var commit = new RepoCommit { Did = DidText, Data = root, Rev = rev }.Sign(signer);

        var car = CarWriter.Write(
            commit.BinaryCid,
            [
                new CarBlock(commit.BinaryCid, commit.Bytes),
                .. blocks.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)),
            ]);

        return new CommitEvent
        {
            Repo = RepoDid,
            Commit = commit.Cid,
            Rev = Tid.Parse(rev),
            Blocks = car,
        };
    }

    [Fact]
    public async Task VerifySignatureAsync_CacheHits_MakeNoNetworkCalls()
    {
        var (verifier, directory, _) = Create();
        using var __ = verifier;

        for (var i = 0; i < 50; i++)
        {
            var result = await verifier.VerifySignatureAsync(Commit(_key, "3l6oveex3ii2l"));
            Assert.True(result.IsValid, result.Error);
        }

        // One resolution for the first commit; the other 49 are served from the cache.
        Assert.Equal(1, directory.Count);
    }

    [Fact]
    public async Task VerifySignatureAsync_KeyRotatedSinceCaching_RefetchesOnceAndVerifies()
    {
        var (verifier, directory, _) = Create();
        using var __ = verifier;
        Assert.True((await verifier.VerifySignatureAsync(Commit(_key, "3l6oveex3ii2l"))).IsValid);

        _published = _rotated;
        _clock.Advance(TimeSpan.FromMinutes(5));
        var result = await verifier.VerifySignatureAsync(Commit(_rotated, "3l6oveex3ii2m"));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public async Task VerifySignatureAsync_ForgedSignatures_DoNotEachCostAFetch()
    {
        using var forger = AtProtoCrypto.GenerateK256Key();
        var (verifier, directory, _) = Create();
        using var __ = verifier;
        Assert.True((await verifier.VerifySignatureAsync(Commit(_key, "3l6oveex3ii2l"))).IsValid);
        _clock.Advance(TimeSpan.FromMinutes(5));

        for (var i = 0; i < 20; i++)
            Assert.False((await verifier.VerifySignatureAsync(Commit(forger, "3l6oveex3ii2m"))).IsValid);

        // One refetch for the first failure, then the refresh interval holds.
        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public async Task InvalidateIdentityAsync_NextCommitResolvesAfresh()
    {
        var (verifier, directory, _) = Create();
        using var __ = verifier;
        await verifier.VerifySignatureAsync(Commit(_key, "3l6oveex3ii2l"));

        await verifier.InvalidateIdentityAsync(RepoDid);
        await verifier.VerifySignatureAsync(Commit(_key, "3l6oveex3ii2m"));

        Assert.Equal(2, directory.Count);
    }

    [Fact]
    public async Task TypedFirehoseConsumer_IdentityEvent_InvalidatesTheVerifiersCache()
    {
        var (verifier, directory, _) = Create();
        using var __ = verifier;
        var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
        {
            ServiceUrl = "wss://relay.example.com",
            Verifier = verifier,
        });

        Assert.True(await consumer.AcceptAsync(Commit(_key, "3l6oveex3ii2l"), default));
        Assert.True(await consumer.AcceptAsync(Commit(_key, "3l6oveex3ii2m"), default));
        Assert.Equal(1, directory.Count);

        // The account rotated its key; the relay announces it before the next commit.
        _published = _rotated;
        Assert.True(await consumer.AcceptAsync(new IdentityEvent { Did = RepoDid, Seq = 3 }, default));
        Assert.True(await consumer.AcceptAsync(Commit(_rotated, "3l6oveex3ii2n"), default));

        Assert.Equal(2, directory.Count);
    }
}
