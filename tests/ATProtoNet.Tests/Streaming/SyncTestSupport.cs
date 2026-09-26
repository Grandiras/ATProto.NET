using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Streaming;

/// <summary>What to get wrong in a commit a <see cref="SyncTestRepo"/> builds.</summary>
internal sealed record CommitTweaks
{
    public int? Version { get; init; }

    public AtProtoKey? SignWith { get; init; }

    public string? SignedDid { get; init; }

    public string? SignedRev { get; init; }

    public bool OmitRecords { get; init; }

    public bool OmitPrevData { get; init; }

    public Cid? PrevData { get; init; }

    public Tid? Since { get; init; }

    public bool OmitSince { get; init; }

    public Cid? Commit { get; init; }

    /// <summary>Rewrites the operations the event lists (not the tree the blocks prove).</summary>
    public Func<List<RepoOp>, List<RepoOp>>? Ops { get; init; }

    /// <summary>Carry only the root node and the commit, not the covering proof.</summary>
    public bool RootOnly { get; init; }

    /// <summary>Microseconds since the epoch for the revision, instead of the next tick.</summary>
    public long? RevMicros { get; init; }
}

/// <summary>
/// A repository that emits the firehose events a Sync 1.1 PDS would: signed commits whose blocks
/// carry the covering proof of their operations, with <c>prevData</c> and <c>since</c>.
/// </summary>
internal sealed class SyncTestRepo : IDisposable
{
    /// <summary>2026-01-01T00:00:00Z: revisions tick up from here, safely in the past.</summary>
    private const long EpochMicros = 1_767_225_600_000_000;

    private readonly Dictionary<string, byte[]> _records = new(StringComparer.Ordinal);
    private long _clock = EpochMicros;
    private int _next;

    public SyncTestRepo(string did = "did:plc:synctestrepoaaaaaaaaaaaa", AtProtoKey? key = null)
    {
        Did = Did.Parse(did);
        Key = key ?? AtProtoCrypto.GenerateK256Key();
    }

    public Did Did { get; }

    public AtProtoKey Key { get; set; }

    public MerkleSearchTree Tree { get; } = MerkleSearchTree.Create();

    public Tid? Rev { get; private set; }

    public Cid Data => ToCid(Tree.ComputeRootCid());

    public string SigningKey => Key.ToDidKey();

    public static Cid ToCid(byte[] binary) => Cid.Parse(CidComputation.EncodeCidToString(binary));

    /// <summary>A fresh record path in <paramref name="collection"/>.</summary>
    public string NewPath(string collection = "com.example.record") =>
        $"{collection}/{Tid.FromInt64(((EpochMicros + ++_next * 7919) << 10) | 1)}";

    /// <summary>Creates one new record.</summary>
    public CommitEvent Create(CommitTweaks? tweaks = null) => Commit([(RepoOpAction.Create, NewPath())], tweaks);

    public CommitEvent Commit(params (RepoOpAction Action, string Path)[] ops) => Commit(ops, null);

    /// <summary>Applies <paramref name="ops"/> and returns the event announcing them.</summary>
    public CommitEvent Commit(IReadOnlyList<(RepoOpAction Action, string Path)> ops, CommitTweaks? tweaks)
    {
        tweaks ??= new CommitTweaks();
        var prevData = Data;
        var since = Rev;

        var repoOps = new List<RepoOp>();
        var recordBlocks = new List<CarBlock>();
        foreach (var (action, path) in ops)
        {
            var previous = Tree.Get(path);
            byte[]? value = null;
            if (action != RepoOpAction.Delete)
            {
                var record = RecordBlock(++_next);
                value = CidComputation.ComputeBinaryForDagCbor(record);
                recordBlocks.Add(new CarBlock(value, record));
            }

            switch (action)
            {
                case RepoOpAction.Create:
                    Tree.Add(path, value!);
                    break;
                case RepoOpAction.Update:
                    Tree.Update(path, value!);
                    break;
                case RepoOpAction.Delete:
                    Tree.Delete(path);
                    break;
            }

            repoOps.Add(new RepoOp
            {
                Action = action,
                Path = path,
                Cid = value is null ? null : ToCid(value),
                Prev = previous is null ? null : ToCid(previous),
            });
        }

        var (root, proof) = Tree.SerializeProof(ops.Select(o => o.Path));
        var rev = NextRev(tweaks.RevMicros);
        var signed = Sign(root, rev, tweaks);

        var blocks = new List<CarBlock> { new(signed.BinaryCid, signed.Bytes) };
        if (tweaks.RootOnly)
        {
            var rootText = CidComputation.EncodeCidToString(root);
            blocks.Add(new CarBlock(root, proof[rootText]));
        }
        else
        {
            blocks.AddRange(proof.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)));
        }

        if (!tweaks.OmitRecords)
            blocks.AddRange(recordBlocks);

        Rev = rev;
        return new CommitEvent
        {
            Seq = _next,
            Repo = Did,
            Commit = tweaks.Commit ?? signed.Cid,
            Rev = rev,
            Since = tweaks.OmitSince ? null : tweaks.Since ?? since,
            PrevData = tweaks.OmitPrevData ? null : tweaks.PrevData ?? prevData,
            Blocks = CarWriter.Write(signed.BinaryCid, blocks),
            Ops = tweaks.Ops is { } rewrite ? rewrite(repoOps) : repoOps,
            Time = AtDatetime.Now(),
        };
    }

    /// <summary>A <c>#sync</c> event asserting the repository's current tree at a new revision.</summary>
    public SyncEvent Sync(CommitTweaks? tweaks = null)
    {
        tweaks ??= new CommitTweaks();
        var rev = NextRev(tweaks.RevMicros);
        var signed = Sign(Tree.ComputeRootCid(), rev, tweaks);
        Rev = rev;
        return new SyncEvent
        {
            Seq = ++_next,
            Did = Did,
            Rev = rev,
            Blocks = CarWriter.Write(signed.BinaryCid, [new CarBlock(signed.BinaryCid, signed.Bytes)]),
            Time = AtDatetime.Now(),
        };
    }

    /// <summary>The repository's full export, as <c>com.atproto.sync.getRepo</c> serves it.</summary>
    public byte[] Export()
    {
        var (root, blocks) = Tree.Serialize();
        var rev = Rev ?? NextRev(null);
        var signed = Sign(root, rev, new CommitTweaks());
        var all = new List<CarBlock> { new(signed.BinaryCid, signed.Bytes) };
        all.AddRange(blocks.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)));
        foreach (var (_, value) in Tree.GetEntries())
        {
            var text = CidComputation.EncodeCidToString(value);
            if (_records.TryGetValue(text, out var record))
                all.Add(new CarBlock(value, record));
        }

        return CarWriter.Write(signed.BinaryCid, all);
    }

    private SignedRepoCommit Sign(byte[] root, Tid rev, CommitTweaks tweaks) => new RepoCommit
    {
        Did = tweaks.SignedDid ?? Did.Value,
        Data = root,
        Rev = tweaks.SignedRev ?? rev.Value,
        Version = tweaks.Version ?? RepoCommit.CurrentVersion,
    }.Sign(tweaks.SignWith ?? Key);

    private Tid NextRev(long? micros)
    {
        _clock = micros ?? _clock + 1_000_000;
        return Tid.FromInt64((_clock << 10) | 7);
    }

    private byte[] RecordBlock(int n)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(2);
        writer.WriteTextString("n");
        writer.WriteInt32(n);
        writer.WriteTextString("$type");
        writer.WriteTextString("com.example.record");
        writer.WriteEndMap();
        var bytes = writer.Encode();
        _records[CidComputation.EncodeCidToString(CidComputation.ComputeBinaryForDagCbor(bytes))] = bytes;
        return bytes;
    }

    public void Dispose() => Key.Dispose();
}

/// <summary>Serves fixed DID documents and counts how often it was asked.</summary>
internal sealed class StubDidResolver : IDidResolver
{
    private readonly ConcurrentDictionary<Did, string> _keys = new();
    private readonly ConcurrentDictionary<Did, string?> _pds = new();
    private int _resolves;
    private int _refreshes;
    private int _invalidations;

    public int Resolves => Volatile.Read(ref _resolves);

    public int Refreshes => Volatile.Read(ref _refreshes);

    public int Invalidations => Volatile.Read(ref _invalidations);

    /// <summary>A key the next refresh (not a cached resolve) serves instead.</summary>
    public ConcurrentDictionary<Did, string> RotatedKeys { get; } = new();

    public StubDidResolver Add(Did did, string signingKey, string? pds = "https://pds.example.com")
    {
        _keys[did] = signingKey;
        _pds[did] = pds;
        return this;
    }

    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _resolves);
        return Task.FromResult(Document(did, _keys));
    }

    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refreshes);
        if (RotatedKeys.TryGetValue(did, out var rotated))
            _keys[did] = rotated;
        return Task.FromResult(Document(did, _keys));
    }

    public Task InvalidateAsync(Did did, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invalidations);
        return Task.CompletedTask;
    }

    private DidDocument Document(Did did, ConcurrentDictionary<Did, string> keys)
    {
        if (!keys.TryGetValue(did, out var key))
            throw new DidResolutionException($"{did} is not known.", DidResolutionErrorKind.NotFound, did);
        return DidDocs.Parse(did.Value, pds: _pds.GetValueOrDefault(did), signingKey: key);
    }
}

/// <summary>Encodes repository events as the wire frames a relay sends.</summary>
internal static class SyncFrames
{
    public static byte[] Of(FirehoseEvent evt) => evt switch
    {
        CommitEvent commit => EventStreamFrames.Message("#commit", w => WriteCommit(w, commit)),
        SyncEvent sync => EventStreamFrames.Message("#sync", w => WriteSync(w, sync)),
        _ => throw new ArgumentException("Not a repository event.", nameof(evt)),
    };

    private static void WriteCommit(CborWriter writer, CommitEvent commit)
    {
        writer.WriteStartMap(commit.PrevData is null ? 10 : 11);
        writer.WriteTextString("ops");
        var ops = commit.Ops ?? [];
        writer.WriteStartArray(ops.Count);
        foreach (var op in ops)
        {
            writer.WriteStartMap(op.Prev is null ? 3 : 4);
            writer.WriteTextString("cid");
            DagCborLink.WriteNullable(writer, op.Cid?.ToBytes());
            writer.WriteTextString("path");
            writer.WriteTextString(op.Path);
            writer.WriteTextString("action");
            writer.WriteTextString(op.Action.ToString().ToLowerInvariant());
            if (op.Prev is { } prev)
            {
                writer.WriteTextString("prev");
                DagCborLink.Write(writer, prev.ToBytes());
            }

            writer.WriteEndMap();
        }

        writer.WriteEndArray();
        writer.WriteTextString("rev");
        writer.WriteTextString(commit.Rev.Value);
        writer.WriteTextString("seq");
        writer.WriteInt64(commit.Seq);
        writer.WriteTextString("repo");
        writer.WriteTextString(commit.Repo.Value);
        writer.WriteTextString("time");
        writer.WriteTextString("2026-09-26T12:00:00.000Z");
        writer.WriteTextString("blobs");
        writer.WriteStartArray(0);
        writer.WriteEndArray();
        writer.WriteTextString("since");
        if (commit.Since is { } since)
            writer.WriteTextString(since.Value);
        else
            writer.WriteNull();
        writer.WriteTextString("blocks");
        writer.WriteByteString(commit.Blocks ?? []);
        writer.WriteTextString("commit");
        DagCborLink.Write(writer, commit.Commit.ToBytes());
        writer.WriteTextString("tooBig");
        writer.WriteBoolean(false);
        if (commit.PrevData is { } prevData)
        {
            writer.WriteTextString("prevData");
            DagCborLink.Write(writer, prevData.ToBytes());
        }

        writer.WriteEndMap();
    }

    private static void WriteSync(CborWriter writer, SyncEvent sync)
    {
        writer.WriteStartMap(5);
        writer.WriteTextString("did");
        writer.WriteTextString(sync.Did.Value);
        writer.WriteTextString("rev");
        writer.WriteTextString(sync.Rev.Value);
        writer.WriteTextString("seq");
        writer.WriteInt64(sync.Seq);
        writer.WriteTextString("time");
        writer.WriteTextString("2026-09-26T12:00:00.000Z");
        writer.WriteTextString("blocks");
        writer.WriteByteString(sync.Blocks ?? []);
        writer.WriteEndMap();
    }
}

/// <summary>A repository fetcher that serves what the test hands it, and counts requests.</summary>
internal sealed class ScriptedRepoFetcher(Func<Did, Task<byte[]?>> fetch) : IRepoFetcher
{
    private int _requests;

    public int Requests => Volatile.Read(ref _requests);

    public async Task<byte[]?> FetchAsync(Did did, long maxBytes, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _requests);
        return await fetch(did);
    }
}

/// <summary><c>Streaming/TestData/firehose-sync-frames.json</c>: real Sync 1.1 frames.</summary>
internal static class LiveFrames
{
    private static readonly Lazy<(Dictionary<string, byte[]> Frames, Dictionary<Did, string> Keys, Dictionary<Did, string> Pds)> Loaded = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Streaming", "TestData", "firehose-sync-frames.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        var frames = root.GetProperty("frames").EnumerateArray()
            .ToDictionary(f => f.GetProperty("name").GetString()!, f => Convert.FromBase64String(f.GetProperty("frame").GetString()!));
        var keys = root.GetProperty("signingKeys").EnumerateObject()
            .ToDictionary(p => Did.Parse(p.Name), p => p.Value.GetString()!);
        var pds = root.GetProperty("pds").EnumerateObject()
            .ToDictionary(p => Did.Parse(p.Name), p => p.Value.GetString()!);
        return (frames, keys, pds);
    });

    public static IEnumerable<string> Names => Loaded.Value.Frames.Keys;

    public static byte[] Frame(string name) => Loaded.Value.Frames[name];

    public static FirehoseEvent Event(string name) => (FirehoseEvent)FirehoseEventParser.Parse(Frame(name))!;

    public static CommitEvent Commit(string name) => (CommitEvent)Event(name);

    /// <summary>A resolver serving each captured account's signing key.</summary>
    public static StubDidResolver Resolver()
    {
        var resolver = new StubDidResolver();
        foreach (var (did, key) in Loaded.Value.Keys)
            resolver.Add(did, key, Loaded.Value.Pds.GetValueOrDefault(did));
        return resolver;
    }

    /// <summary>A copy of a commit with some fields replaced.</summary>
    public static CommitEvent With(
        CommitEvent commit,
        IReadOnlyList<RepoOp>? ops = null,
        Cid? prevData = null,
        Tid? since = null,
        byte[]? blocks = null) => new()
    {
        Seq = commit.Seq,
        Time = commit.Time,
        Repo = commit.Repo,
        Commit = commit.Commit,
        Rev = commit.Rev,
        Since = since ?? commit.Since,
        Blocks = blocks ?? commit.Blocks,
        Ops = ops ?? commit.Ops,
        PrevData = prevData ?? commit.PrevData,
    };
}
