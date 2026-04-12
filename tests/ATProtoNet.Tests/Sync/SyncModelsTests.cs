using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Sync;

public class SyncModelsTests
{
    private readonly JsonSerializerOptions _options = AtProtoJsonDefaults.Options;

    // ──────────────────────────────────────────────────────────
    //  AccountHostingStatus constants
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AccountHostingStatus.Takendown, "takendown")]
    [InlineData(AccountHostingStatus.Suspended, "suspended")]
    [InlineData(AccountHostingStatus.Deleted, "deleted")]
    [InlineData(AccountHostingStatus.Deactivated, "deactivated")]
    [InlineData(AccountHostingStatus.Desynchronized, "desynchronized")]
    [InlineData(AccountHostingStatus.Throttled, "throttled")]
    public void AccountHostingStatus_HasCorrectValues(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    // ──────────────────────────────────────────────────────────
    //  HostStatus constants
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HostStatus.Active, "active")]
    [InlineData(HostStatus.Idle, "idle")]
    [InlineData(HostStatus.Offline, "offline")]
    [InlineData(HostStatus.Throttled, "throttled")]
    [InlineData(HostStatus.Banned, "banned")]
    public void HostStatus_HasCorrectValues(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    // ──────────────────────────────────────────────────────────
    //  getRepoStatus
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetRepoStatusResponse_Deserializes_ActiveRepo()
    {
        var json = """
        {
            "did": "did:plc:abc123",
            "active": true,
            "rev": "3k2la7qbx5c2a"
        }
        """;

        var response = JsonSerializer.Deserialize<GetRepoStatusResponse>(json, _options);

        Assert.NotNull(response);
        Assert.Equal("did:plc:abc123", response.Did);
        Assert.True(response.Active);
        Assert.Equal("3k2la7qbx5c2a", response.Rev);
        Assert.Null(response.Status);
    }

    [Fact]
    public void GetRepoStatusResponse_Deserializes_InactiveRepo()
    {
        var json = """
        {
            "did": "did:plc:abc123",
            "active": false,
            "status": "deactivated"
        }
        """;

        var response = JsonSerializer.Deserialize<GetRepoStatusResponse>(json, _options);

        Assert.NotNull(response);
        Assert.False(response.Active);
        Assert.Equal(AccountHostingStatus.Deactivated, response.Status);
        Assert.Null(response.Rev);
    }

    // ──────────────────────────────────────────────────────────
    //  listHosts
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ListHostsResponse_Deserializes()
    {
        var json = """
        {
            "cursor": "abc123",
            "hosts": [
                {
                    "hostname": "pds1.example.com",
                    "seq": 42000,
                    "accountCount": 150,
                    "status": "active"
                },
                {
                    "hostname": "pds2.example.com",
                    "status": "offline"
                }
            ]
        }
        """;

        var response = JsonSerializer.Deserialize<ListHostsResponse>(json, _options);

        Assert.NotNull(response);
        Assert.Equal("abc123", response.Cursor);
        Assert.Equal(2, response.Hosts.Count);

        Assert.Equal("pds1.example.com", response.Hosts[0].Hostname);
        Assert.Equal(42000, response.Hosts[0].Seq);
        Assert.Equal(150, response.Hosts[0].AccountCount);
        Assert.Equal(HostStatus.Active, response.Hosts[0].Status);

        Assert.Equal("pds2.example.com", response.Hosts[1].Hostname);
        Assert.Null(response.Hosts[1].Seq);
        Assert.Equal(HostStatus.Offline, response.Hosts[1].Status);
    }

    // ──────────────────────────────────────────────────────────
    //  getHostStatus
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetHostStatusResponse_Deserializes()
    {
        var json = """
        {
            "hostname": "pds.example.com",
            "seq": 99000,
            "accountCount": 500,
            "status": "active"
        }
        """;

        var response = JsonSerializer.Deserialize<GetHostStatusResponse>(json, _options);

        Assert.NotNull(response);
        Assert.Equal("pds.example.com", response.Hostname);
        Assert.Equal(99000, response.Seq);
        Assert.Equal(500, response.AccountCount);
        Assert.Equal(HostStatus.Active, response.Status);
    }

    // ──────────────────────────────────────────────────────────
    //  listReposByCollection
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ListReposByCollectionResponse_Deserializes()
    {
        var json = """
        {
            "repos": [
                { "did": "did:plc:aaa" },
                { "did": "did:plc:bbb" },
                { "did": "did:plc:ccc" }
            ],
            "cursor": "next-page"
        }
        """;

        var response = JsonSerializer.Deserialize<ListReposByCollectionResponse>(json, _options);

        Assert.NotNull(response);
        Assert.Equal(3, response.Repos.Count);
        Assert.Equal("did:plc:aaa", response.Repos[0].Did);
        Assert.Equal("did:plc:ccc", response.Repos[2].Did);
        Assert.Equal("next-page", response.Cursor);
    }

    [Fact]
    public void ListReposByCollectionResponse_Deserializes_EmptyResult()
    {
        var json = """{ "repos": [] }""";

        var response = JsonSerializer.Deserialize<ListReposByCollectionResponse>(json, _options);

        Assert.NotNull(response);
        Assert.Empty(response.Repos);
        Assert.Null(response.Cursor);
    }

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
            "commit": "bafyreiabc",
            "rev": "3k2la7qbx5c2a",
            "since": "3k2la7qbx5c29",
            "tooBig": false,
            "rebase": false,
            "prevData": "bafyreiprevdata",
            "blobs": [],
            "ops": [
                {
                    "action": "create",
                    "path": "app.bsky.feed.post/3k2la",
                    "cid": "bafyreicid"
                },
                {
                    "action": "update",
                    "path": "app.bsky.feed.post/3k2lb",
                    "cid": "bafyreinewcid",
                    "prev": "bafyreioldcid"
                },
                {
                    "action": "delete",
                    "path": "app.bsky.feed.post/3k2lc",
                    "cid": null,
                    "prev": "bafyreidelcid"
                }
            ]
        }
        """;

        var msg = JsonSerializer.Deserialize<FirehoseMessage>(json, _options);

        Assert.IsType<CommitEvent>(msg);
        var commit = (CommitEvent)msg;
        Assert.Equal("bafyreiprevdata", commit.PrevData);
        Assert.NotNull(commit.Blobs);
        Assert.Empty(commit.Blobs);
        Assert.Equal(3, commit.Ops!.Count);

        // create — no prev
        Assert.Equal("create", commit.Ops[0].Action);
        Assert.Equal("bafyreicid", commit.Ops[0].Cid);
        Assert.Null(commit.Ops[0].Prev);

        // update — has prev
        Assert.Equal("update", commit.Ops[1].Action);
        Assert.Equal("bafyreinewcid", commit.Ops[1].Cid);
        Assert.Equal("bafyreioldcid", commit.Ops[1].Prev);

        // delete — has prev, null cid
        Assert.Equal("delete", commit.Ops[2].Action);
        Assert.Null(commit.Ops[2].Cid);
        Assert.Equal("bafyreidelcid", commit.Ops[2].Prev);
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
