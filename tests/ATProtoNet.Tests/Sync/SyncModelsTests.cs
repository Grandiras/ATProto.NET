using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Sync;

public class SyncModelsTests
{
    private readonly JsonSerializerOptions _options = AtProtoJsonDefaults.Options;

    // ──────────────────────────────────────────────────────────
    //  SyncEvent (#sync) — Sync v1.1
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SyncEvent_Deserializes()
    {
        var json = """
        {
            "$type": "#sync",
            "seq": 1001,
            "time": "2026-01-15T10:30:00Z",
            "did": "did:plc:abc123",
            "rev": "3k2la7qbx5c2a",
            "blocks": "AAAA"
        }
        """;

        var msg = JsonSerializer.Deserialize<FirehoseMessage>(json, _options);

        Assert.IsType<SyncEvent>(msg);
        var sync = (SyncEvent)msg;
        Assert.Equal(1001, sync.Seq);
        Assert.Equal("did:plc:abc123", sync.Did);
        Assert.Equal("3k2la7qbx5c2a", sync.Rev);
        Assert.NotNull(sync.Blocks);
    }

    // ──────────────────────────────────────────────────────────
    //  CommitEvent — Sync v1.1 fields (prevData, blobs, prev)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void CommitEvent_Deserializes_SyncV1_1_Fields()
    {
        var json = """
        {
            "$type": "#commit",
            "seq": 500,
            "repo": "did:plc:test",
            "commit": "bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta",
            "rev": "3k2la7qbx5c2a",
            "since": "3k2la7qbx5c27",
            "tooBig": false,
            "rebase": false,
            "prevData": "bafyreihlzn2lwoicy7x46zrj4ysc3eqhmpvghca5vbhcwtubpytilc6xsi",
            "blobs": [],
            "ops": [
                {
                    "action": "create",
                    "path": "app.bsky.feed.post/3k2la",
                    "cid": "bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry"
                },
                {
                    "action": "update",
                    "path": "app.bsky.feed.post/3k2lb",
                    "cid": "bafyreif5xp2wdd3kcu54tgzu4zjp2iazrs2zabrfkpz6gklpco7cfhfopa",
                    "prev": "bafyreidlviq64injlkcffzcrpomojfo5wrbhwn4niudum4kgilgu754laa"
                },
                {
                    "action": "delete",
                    "path": "app.bsky.feed.post/3k2lc",
                    "cid": null,
                    "prev": "bafyreiem2r3qkjputcvajc5j4gbodhjwchw6wkcnac4cx3nqme5cw4t6sy"
                }
            ]
        }
        """;

        var msg = JsonSerializer.Deserialize<FirehoseMessage>(json, _options);

        Assert.IsType<CommitEvent>(msg);
        var commit = (CommitEvent)msg;
        Assert.Equal("bafyreihlzn2lwoicy7x46zrj4ysc3eqhmpvghca5vbhcwtubpytilc6xsi", commit.PrevData);
        Assert.NotNull(commit.Blobs);
        Assert.Empty(commit.Blobs);
        Assert.Equal(3, commit.Ops!.Count);

        // create — no prev
        Assert.Equal(RepoOpAction.Create, commit.Ops[0].Action);
        Assert.Equal("bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry", commit.Ops[0].Cid);
        Assert.Null(commit.Ops[0].Prev);

        // update — has prev
        Assert.Equal(RepoOpAction.Update, commit.Ops[1].Action);
        Assert.Equal("bafyreif5xp2wdd3kcu54tgzu4zjp2iazrs2zabrfkpz6gklpco7cfhfopa", commit.Ops[1].Cid);
        Assert.Equal("bafyreidlviq64injlkcffzcrpomojfo5wrbhwn4niudum4kgilgu754laa", commit.Ops[1].Prev);

        // delete — has prev, null cid
        Assert.Equal(RepoOpAction.Delete, commit.Ops[2].Action);
        Assert.Null(commit.Ops[2].Cid);
        Assert.Equal("bafyreiem2r3qkjputcvajc5j4gbodhjwchw6wkcnac4cx3nqme5cw4t6sy", commit.Ops[2].Prev);
    }

    // ──────────────────────────────────────────────────────────
    //  AccountEvent — known status values
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("desynchronized")]
    [InlineData("throttled")]
    [InlineData("takendown")]
    [InlineData("suspended")]
    [InlineData("deleted")]
    [InlineData("deactivated")]
    public void AccountEvent_Deserializes_AllStatusValues(string status)
    {
        var json = $$"""
        {
            "$type": "#account",
            "seq": 100,
            "did": "did:plc:test",
            "active": false,
            "status": "{{status}}"
        }
        """;

        var msg = JsonSerializer.Deserialize<FirehoseMessage>(json, _options);

        Assert.IsType<AccountEvent>(msg);
        var account = (AccountEvent)msg;
        Assert.False(account.Active);
        Assert.Equal(status, account.Status);
    }
}
