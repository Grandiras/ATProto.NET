using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class RecordEventTests
{
    private static readonly Did Repo = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");

    private static (byte[] Car, Cid RecordCid) CarWithRecord(string json)
    {
        var (bytes, cid) = DagCborEncoder.EncodeWithCid(JsonDocument.Parse(json).RootElement);
        var commitBlock = DagCborEncoder.Encode(JsonDocument.Parse("""{"version":3}""").RootElement);
        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBlock);
        var car = CarWriter.Write(commitCid, [new CarBlock(commitCid, commitBlock), new CarBlock(cid.ToBytes(), bytes)]);
        return (car, cid);
    }

    private static CommitEvent Commit(byte[]? blocks, params RepoOp[] ops) => new()
    {
        Repo = Repo,
        Commit = Cid.Parse("bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta"),
        Rev = Tid.Parse("3jzfcijpj2z2a"),
        Seq = 42,
        Blocks = blocks,
        Ops = ops,
    };

    [Fact]
    public void GetRecordEvents_DecodesEachRecordFromTheCommitBlocks()
    {
        var (car, cid) = CarWithRecord("""{"$type":"app.bsky.feed.post","text":"hello","createdAt":"2024-01-15T12:00:00.000Z"}""");
        var commit = Commit(car,
            new RepoOp { Action = RepoOpAction.Create, Path = "app.bsky.feed.post/3jzfcijpj2z2b", Cid = cid },
            new RepoOp { Action = RepoOpAction.Delete, Path = "app.bsky.feed.like/3jzfcijpj2z2c", Prev = cid });

        var events = commit.GetRecordEvents();

        Assert.Equal(2, events.Count);
        IRecordEvent created = events[0];
        Assert.Equal(Repo, created.Did);
        Assert.Equal("app.bsky.feed.post", created.Collection.Value);
        Assert.Equal("3jzfcijpj2z2b", created.Rkey.Value);
        Assert.Equal(RepoOpAction.Create, created.Operation);
        Assert.Equal(cid, created.Cid);
        Assert.Equal("3jzfcijpj2z2a", created.Rev?.Value);
        Assert.Equal("hello", created.Record!.Value.GetProperty("text").GetString());
        Assert.Equal("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3jzfcijpj2z2b", created.Uri.ToString());
        Assert.Equal(42, events[0].Seq);

        Assert.Equal(RepoOpAction.Delete, events[1].Operation);
        Assert.Null(events[1].Record);
        Assert.Equal(cid, events[1].Prev);
    }

    [Fact]
    public void GetRecordEvents_SkipsAnInvalidPathAndToleratesMissingBlocks()
    {
        var commit = Commit(blocks: null,
            new RepoOp { Action = RepoOpAction.Create, Path = "not a path", Cid = null },
            new RepoOp
            {
                Action = RepoOpAction.Update,
                Path = "app.bsky.feed.post/3jzfcijpj2z2b",
                Cid = Cid.Parse("bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry"),
            });

        var update = Assert.Single(commit.GetRecordEvents());

        Assert.Equal(RepoOpAction.Update, update.Operation);
        Assert.Null(update.Record);
    }

    [Fact]
    public void JetstreamCommitEvent_IsARecordEvent()
    {
        IRecordEvent evt = new JetstreamCommitEvent
        {
            Did = Repo,
            TimeUs = 1,
            Collection = Nsid.Parse("app.bsky.feed.post"),
            Rkey = RecordKey.Parse("3jzfcijpj2z2b"),
            Operation = RepoOpAction.Create,
            Record = JsonDocument.Parse("""{"text":"hi"}""").RootElement,
        };

        Assert.Equal("hi", evt.GetRecord<Dictionary<string, string>>()!["text"]);
        Assert.Equal("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3jzfcijpj2z2b", evt.Uri.ToString());
    }
}
