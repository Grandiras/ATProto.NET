using ATProtoNet.Lexicon.Com.AtProto.Sync;
using System.Text;
using System.Text.Json.Serialization;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamEventParserTests
{
    // Real frame shapes from the Jetstream protocol documentation.
    private const string CreateCommitJson =
        """
        {"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329308,"kind":"commit","commit":{"rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b","record":{"$type":"app.bsky.feed.like","createdAt":"2024-09-09T19:46:02.102Z","subject":{"cid":"bafyreidc6sydkkbchcyg62v77wbhzvb2mvytlmsychqgwf2xojjtirmzj4","uri":"at://did:plc:wa7b35aakoll7hugkrjuf3xf/app.bsky.feed.post/3l3pte3p2e325"}},"cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi"}}
        """;

    private const string DeleteCommitJson =
        """
        {"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329309,"kind":"commit","commit":{"rev":"3l3qo2vutsx2b","operation":"delete","collection":"app.bsky.feed.post","rkey":"3l3qo2vuowo2b"}}
        """;

    private const string IdentityJson =
        """
        {"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1725911162329308,"kind":"identity","identity":{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","handle":"yohenrique.com","seq":1409752997,"time":"2024-09-09T19:46:02.102Z"}}
        """;

    private const string AccountJson =
        """
        {"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1725911162329308,"kind":"account","account":{"active":true,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","seq":1409753013,"time":"2024-09-09T19:46:02.102Z"}}
        """;

    private static JetstreamEvent? Parse(string json)
        => JetstreamEventParser.ParseFrame(Encoding.UTF8.GetBytes(json), JetstreamProtocol.V1).Event;

    private sealed class LikeSubject
    {
        [JsonPropertyName("uri")]
        public string? Uri { get; set; }

        [JsonPropertyName("cid")]
        public string? Cid { get; set; }
    }

    private sealed class LikeRecord
    {
        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("subject")]
        public LikeSubject? Subject { get; set; }
    }

    [Fact]
    public void Parse_CreateCommit_ReturnsCommitEvent()
    {
        var evt = Parse(CreateCommitJson);

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("did:plc:eygmaihciaxprqvxpfvl6flk", commit.Did.Value);
        Assert.Equal(1725911162329308, commit.TimeUs);
        Assert.Equal("app.bsky.feed.like", commit.Collection);
        Assert.Equal("3l3qo2vuowo2b", commit.Rkey);
        Assert.Equal(RepoOpAction.Create, commit.Operation);
        Assert.Equal("3l3qo2vutsw2b", commit.Rev);
        Assert.NotNull(commit.Cid);
        Assert.Equal("bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi", commit.Cid!.Value);
        Assert.NotNull(commit.Record);
        Assert.Equal(
            "at://did:plc:eygmaihciaxprqvxpfvl6flk/app.bsky.feed.like/3l3qo2vuowo2b",
            commit.Uri.ToString());
    }

    [Theory]
    [InlineData("\"collection\":\"app.bsky.feed.like\"", "\"collection\":\"not an nsid\"")]
    [InlineData("\"rkey\":\"3l3qo2vuowo2b\"", "\"rkey\":\"not a record key\"")]
    public void Parse_CommitWhosePathDoesNotParse_IsSkipped(string field, string malformed)
    {
        // Such a commit names no record a consumer could act on.
        Assert.Null(Parse(CreateCommitJson.Replace(field, malformed)));
    }

    [Fact]
    public void Parse_CommitWithAMalformedRev_KeepsTheEventWithoutTheRev()
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(
            CreateCommitJson.Replace("\"rev\":\"3l3qo2vutsw2b\"", "\"rev\":\"not-a-tid\"")));

        Assert.Null(commit.Rev);
        Assert.Equal("3l3qo2vuowo2b", commit.Rkey.Value);
    }

    [Fact]
    public void Parse_IdentityWithAMalformedHandle_KeepsTheEventWithoutTheHandle()
    {
        var identity = Assert.IsType<JetstreamIdentityEvent>(Parse(
            IdentityJson.Replace("\"handle\":\"yohenrique.com\"", "\"handle\":\"not a handle\"")));

        Assert.Null(identity.Handle);
        Assert.Equal("did:plc:ufbl4k27gp6kzas5glhz7fim", identity.Did.Value);
    }

    [Fact]
    public void Parse_CreateCommit_RecordSurvivesDocumentDisposal()
    {
        // The parser clones the record element; accessing it after Parse returns must not throw.
        var evt = Parse(CreateCommitJson);

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("app.bsky.feed.like", commit.Record!.Value.GetProperty("$type").GetString());
    }

    [Fact]
    public void GetRecord_TypedDeserialization_MapsFields()
    {
        var commit = (JetstreamCommitEvent)Parse(CreateCommitJson)!;

        var record = commit.GetRecord<LikeRecord>();

        Assert.NotNull(record);
        Assert.Equal("2024-09-09T19:46:02.102Z", record!.CreatedAt);
        Assert.Equal(
            "at://did:plc:wa7b35aakoll7hugkrjuf3xf/app.bsky.feed.post/3l3pte3p2e325",
            record.Subject?.Uri);
    }

    [Fact]
    public void Parse_UpdateCommit_ReturnsUpdateOperation()
    {
        var json = CreateCommitJson.Replace("\"operation\":\"create\"", "\"operation\":\"update\"");

        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(json));
        Assert.Equal(RepoOpAction.Update, commit.Operation);
    }

    [Fact]
    public void Parse_DeleteCommit_HasNullRecordAndCid()
    {
        var evt = Parse(DeleteCommitJson);

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal(RepoOpAction.Delete, commit.Operation);
        Assert.Null(commit.Record);
        Assert.Null(commit.Cid);
        Assert.Null(commit.GetRecord<LikeRecord>());
    }

    [Fact]
    public void Parse_Identity_ReturnsIdentityEvent()
    {
        var evt = Parse(IdentityJson);

        var identity = Assert.IsType<JetstreamIdentityEvent>(evt);
        Assert.Equal("did:plc:ufbl4k27gp6kzas5glhz7fim", identity.Did.Value);
        Assert.Equal("yohenrique.com", identity.Handle);
        Assert.Equal(1409752997, identity.Seq);
        Assert.Equal("2024-09-09T19:46:02.102Z", identity.Time?.ToString());
    }

    [Fact]
    public void Parse_ActiveAccount_ReturnsAccountEvent()
    {
        var evt = Parse(AccountJson);

        var account = Assert.IsType<JetstreamAccountEvent>(evt);
        Assert.True(account.Active);
        Assert.Null(account.Status);
        Assert.Equal(1409753013, account.Seq);
    }

    [Fact]
    public void Parse_InactiveAccount_ParsesStatus()
    {
        var json = AccountJson.Replace("\"active\":true", "\"active\":false,\"status\":\"takendown\"");

        var account = Assert.IsType<JetstreamAccountEvent>(Parse(json));
        Assert.False(account.Active);
        Assert.Equal("takendown", account.Status);
    }

    [Fact]
    public void Parse_UnknownKind_ReturnsNull()
    {
        var json = """{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1,"kind":"somethingNew","somethingNew":{}}""";

        Assert.Null(Parse(json));
    }

    [Fact]
    public void Parse_UnknownOperation_ReturnsNull()
    {
        var json = CreateCommitJson.Replace("\"operation\":\"create\"", "\"operation\":\"merge\"");

        Assert.Null(Parse(json));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"truncated\":")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Parse_MalformedFrame_ReturnsNull(string json)
    {
        Assert.Null(Parse(json));
    }

    [Fact]
    public void Parse_MissingDid_ReturnsNull()
    {
        var json = """{"time_us":1,"kind":"commit","commit":{"operation":"create","collection":"a.b.c","rkey":"x"}}""";

        Assert.Null(Parse(json));
    }

    [Fact]
    public void Parse_InvalidDid_ReturnsNull()
    {
        var json = CreateCommitJson.Replace("did:plc:eygmaihciaxprqvxpfvl6flk", "not-a-did");

        Assert.Null(Parse(json));
    }

    [Fact]
    public void Parse_CommitWithoutBody_ReturnsNull()
    {
        var json = """{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1,"kind":"commit"}""";

        Assert.Null(Parse(json));
    }

    [Fact]
    public void Parse_InvalidCidString_DeliversEventWithoutCid()
    {
        // An unparseable CID must not prevent the commit event (and its record) from being
        // delivered; the event just carries no Cid.
        var json = CreateCommitJson.Replace(
            "bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi", "!!!");

        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(json));
        Assert.NotNull(commit.Record);
        Assert.Null(commit.Cid);
    }

    [Fact]
    public void Parse_ExtraUnknownFields_Ignored()
    {
        var json = CreateCommitJson.Replace(
            "\"kind\":\"commit\"", "\"kind\":\"commit\",\"futureField\":{\"nested\":true}");

        Assert.IsType<JetstreamCommitEvent>(Parse(json));
    }

    [Fact]
    public void ParseFrame_ReportsWhyAFrameWasDropped()
    {
        JetstreamEventParser.Parse(Encoding.UTF8.GetBytes(CreateCommitJson), JetstreamProtocol.V1, out var kept);
        JetstreamEventParser.Parse(Encoding.UTF8.GetBytes(CreateCommitJson.Replace("did:plc:", "not-a-did:")),
            JetstreamProtocol.V1, out var malformed);
        JetstreamEventParser.Parse(Encoding.UTF8.GetBytes(CreateCommitJson.Replace("\"kind\":\"commit\"", "\"kind\":\"future\"")),
            JetstreamProtocol.V1, out var unknown);

        Assert.Null(kept);
        Assert.Equal(StreamDropReason.Malformed, malformed);
        Assert.Equal(StreamDropReason.UnknownType, unknown);
    }

    [Fact]
    public void ParseFrame_MemoryOverload_RecordOutlivesTheBuffer()
    {
        // The parsed document reads the caller's buffer directly, so the Record element has
        // to be detached from it before ParseFrame returns.
        var utf8 = Encoding.UTF8.GetBytes(CreateCommitJson);
        var commit = Assert.IsType<JetstreamCommitEvent>(
            JetstreamEventParser.ParseFrame(utf8.AsMemory(), JetstreamProtocol.V1).Event);

        Array.Clear(utf8);

        Assert.Equal("app.bsky.feed.like", commit.Record!.Value.GetProperty("$type").GetString());
    }

    [Fact]
    public void ParseFrame_MemoryOverload_MalformedFrame_ReturnsDefault()
    {
        var frame = JetstreamEventParser.ParseFrame("{not json"u8.ToArray().AsMemory(), JetstreamProtocol.V1);

        Assert.Null(frame.Event);
        Assert.Null(frame.Info);
        Assert.Null(frame.Error);
    }
}
