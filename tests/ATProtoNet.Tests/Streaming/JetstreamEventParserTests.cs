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
        var evt = JetstreamEventParser.Parse(CreateCommitJson);

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("did:plc:eygmaihciaxprqvxpfvl6flk", commit.Did.Value);
        Assert.Equal(1725911162329308, commit.TimeUs);
        Assert.Equal("app.bsky.feed.like", commit.Collection);
        Assert.Equal("3l3qo2vuowo2b", commit.RKey);
        Assert.Equal(JetstreamOperation.Create, commit.Operation);
        Assert.Equal("3l3qo2vutsw2b", commit.Rev);
        Assert.NotNull(commit.Cid);
        Assert.Equal("bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi", commit.Cid!.Value);
        Assert.NotNull(commit.Record);
        Assert.Equal(
            "at://did:plc:eygmaihciaxprqvxpfvl6flk/app.bsky.feed.like/3l3qo2vuowo2b",
            commit.Uri.ToString());
    }

    [Fact]
    public void Parse_CreateCommit_RecordSurvivesDocumentDisposal()
    {
        // The parser clones the record element; accessing it after Parse returns must not throw.
        var evt = JetstreamEventParser.Parse(CreateCommitJson);

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("app.bsky.feed.like", commit.Record!.Value.GetProperty("$type").GetString());
    }

    [Fact]
    public void GetRecord_TypedDeserialization_MapsFields()
    {
        var commit = (JetstreamCommitEvent)JetstreamEventParser.Parse(CreateCommitJson)!;

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

        var commit = Assert.IsType<JetstreamCommitEvent>(JetstreamEventParser.Parse(json));
        Assert.Equal(JetstreamOperation.Update, commit.Operation);
    }

    [Fact]
    public void Parse_DeleteCommit_HasNullRecordAndCid()
    {
        var evt = JetstreamEventParser.Parse(DeleteCommitJson);

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal(JetstreamOperation.Delete, commit.Operation);
        Assert.Null(commit.Record);
        Assert.Null(commit.Cid);
        Assert.Null(commit.GetRecord<LikeRecord>());
    }

    [Fact]
    public void Parse_Identity_ReturnsIdentityEvent()
    {
        var evt = JetstreamEventParser.Parse(IdentityJson);

        var identity = Assert.IsType<JetstreamIdentityEvent>(evt);
        Assert.Equal("did:plc:ufbl4k27gp6kzas5glhz7fim", identity.Did.Value);
        Assert.Equal("yohenrique.com", identity.Handle);
        Assert.Equal(1409752997, identity.Seq);
        Assert.Equal("2024-09-09T19:46:02.102Z", identity.Time);
    }

    [Fact]
    public void Parse_ActiveAccount_ReturnsAccountEvent()
    {
        var evt = JetstreamEventParser.Parse(AccountJson);

        var account = Assert.IsType<JetstreamAccountEvent>(evt);
        Assert.True(account.Active);
        Assert.Null(account.Status);
        Assert.Equal(1409753013, account.Seq);
    }

    [Fact]
    public void Parse_InactiveAccount_ParsesStatus()
    {
        var json = AccountJson.Replace("\"active\":true", "\"active\":false,\"status\":\"takendown\"");

        var account = Assert.IsType<JetstreamAccountEvent>(JetstreamEventParser.Parse(json));
        Assert.False(account.Active);
        Assert.Equal("takendown", account.Status);
    }

    [Fact]
    public void Parse_UnknownKind_ReturnsNull()
    {
        var json = """{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1,"kind":"somethingNew","somethingNew":{}}""";

        Assert.Null(JetstreamEventParser.Parse(json));
    }

    [Fact]
    public void Parse_UnknownOperation_ReturnsNull()
    {
        var json = CreateCommitJson.Replace("\"operation\":\"create\"", "\"operation\":\"merge\"");

        Assert.Null(JetstreamEventParser.Parse(json));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"truncated\":")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Parse_MalformedFrame_ReturnsNull(string json)
    {
        Assert.Null(JetstreamEventParser.Parse(json));
    }

    [Fact]
    public void Parse_MissingDid_ReturnsNull()
    {
        var json = """{"time_us":1,"kind":"commit","commit":{"operation":"create","collection":"a.b.c","rkey":"x"}}""";

        Assert.Null(JetstreamEventParser.Parse(json));
    }

    [Fact]
    public void Parse_InvalidDid_ReturnsNull()
    {
        var json = CreateCommitJson.Replace("did:plc:eygmaihciaxprqvxpfvl6flk", "not-a-did");

        Assert.Null(JetstreamEventParser.Parse(json));
    }

    [Fact]
    public void Parse_CommitWithoutBody_ReturnsNull()
    {
        var json = """{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time_us":1,"kind":"commit"}""";

        Assert.Null(JetstreamEventParser.Parse(json));
    }

    [Fact]
    public void Parse_LooseCidString_DoesNotDropEvent()
    {
        // Cid.Parse is lenient about the string form; whatever it accepts must not
        // prevent the commit event (and its record) from being delivered.
        var json = CreateCommitJson.Replace(
            "bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi", "!!!");

        var commit = Assert.IsType<JetstreamCommitEvent>(JetstreamEventParser.Parse(json));
        Assert.NotNull(commit.Record);
    }

    [Fact]
    public void Parse_ExtraUnknownFields_Ignored()
    {
        var json = CreateCommitJson.Replace(
            "\"kind\":\"commit\"", "\"kind\":\"commit\",\"futureField\":{\"nested\":true}");

        Assert.IsType<JetstreamCommitEvent>(JetstreamEventParser.Parse(json));
    }

    [Fact]
    public void Parse_Utf8Overload_MatchesStringOverload()
    {
        var evt = JetstreamEventParser.Parse(Encoding.UTF8.GetBytes(CreateCommitJson));

        var commit = Assert.IsType<JetstreamCommitEvent>(evt);
        Assert.Equal("app.bsky.feed.like", commit.Collection);
    }

    [Fact]
    public void Parse_NullString_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JetstreamEventParser.Parse((string)null!));
    }
}
