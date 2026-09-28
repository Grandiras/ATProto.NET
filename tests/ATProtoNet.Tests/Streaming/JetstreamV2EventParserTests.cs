using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamV2EventParserTests
{
    // Real frame shapes from the network.bsky.jetstream.subscribeEvents Lexicon and the
    // Jetstream v2 protocol documentation.
    private const string CommitJson =
        """
        {"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#commit","seq":24664288881,"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time":"2024-09-09T19:46:02.329308Z","rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b","cid":"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi","record":{"$type":"app.bsky.feed.like","createdAt":"2024-09-09T19:46:02.102Z","subject":{"cid":"bafyreidc6sydkkbchcyg62v77wbhzvb2mvytlmsychqgwf2xojjtirmzj4","uri":"at://did:plc:wa7b35aakoll7hugkrjuf3xf/app.bsky.feed.post/3l3pte3p2e325"}}}}
        """;

    private const string DeleteCommitJson =
        """
        {"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#commit","seq":24664288882,"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time":"2024-09-09T19:46:02.329309Z","rev":"3l3qo2vutsx2b","operation":"delete","collection":"app.bsky.feed.post","rkey":"3l3qo2vuowo2b"}}
        """;

    private const string IdentityJson =
        """
        {"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#identity","seq":24664288883,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time":"2024-09-09T19:46:02.329308Z","identity":{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","handle":"yohenrique.com","seq":1409752997,"time":"2024-09-09T19:46:02.102Z"}}}
        """;

    private const string AccountJson =
        """
        {"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#account","seq":24664288884,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time":"2024-09-09T19:46:02.329308Z","account":{"active":false,"status":"deleted","did":"did:plc:ufbl4k27gp6kzas5glhz7fim","seq":1409753013,"time":"2024-09-09T19:46:02.102Z"}}}
        """;

    private const string SyncJson =
        """
        {"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#sync","seq":24664288885,"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","time":"2024-09-09T19:46:02.329308Z","sync":{"did":"did:plc:ufbl4k27gp6kzas5glhz7fim","rev":"3l3qo2vutsw2b","seq":1409753014,"time":"2024-09-09T19:46:02.102Z","blocks":{"$bytes":"emVyb0NBUg=="}}}}
        """;

    private const string InfoJson =
        """
        {"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#info","name":"OutdatedCursor","message":"resumed from seq 24664288000"}}
        """;

    private const string ErrorJson =
        """
        {"$type":"error","error":"ConsumerTooSlow","message":"reader below floor rate"}
        """;

    private sealed class LikeSubject
    {
        [JsonPropertyName("uri")]
        public string? Uri { get; set; }
    }

    private sealed class LikeRecord
    {
        [JsonPropertyName("subject")]
        public LikeSubject? Subject { get; set; }
    }

    private static JetstreamFrame Parse(string json)
        => JetstreamEventParser.ParseFrame(Encoding.UTF8.GetBytes(json), JetstreamProtocol.V2);

    private static JetstreamFrame ParseV1(string json)
        => JetstreamEventParser.ParseFrame(Encoding.UTF8.GetBytes(json), JetstreamProtocol.V1);

    [Fact]
    public void ParseFrame_Commit_ReadsFlattenedFields()
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson).Event);

        Assert.Equal("did:plc:eygmaihciaxprqvxpfvl6flk", commit.Did.Value);
        Assert.Equal("app.bsky.feed.like", commit.Collection);
        Assert.Equal("3l3qo2vuowo2b", commit.Rkey);
        Assert.Equal(RepoOpAction.Create, commit.Operation);
        Assert.Equal("3l3qo2vutsw2b", commit.Rev);
        Assert.NotNull(commit.Cid);
        Assert.Equal(24664288881, commit.Cursor);
    }

    [Fact]
    public void ParseFrame_Commit_DerivesMicrosecondTimeFromRfc3339()
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson).Event);

        // "2024-09-09T19:46:02.329308Z" — the v2 wire's six fractional digits must survive
        // the conversion, since TimeUs is what a v1-shaped consumer keys off.
        Assert.Equal(1725911162329308, commit.TimeUs);
        Assert.Equal(
            DateTimeOffset.Parse("2024-09-09T19:46:02.329308Z"),
            commit.Timestamp);
    }

    [Fact]
    public void ParseFrame_Commit_TypedRecordDeserializes()
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson).Event);

        var like = commit.GetRecord<LikeRecord>();
        Assert.Equal(
            "at://did:plc:wa7b35aakoll7hugkrjuf3xf/app.bsky.feed.post/3l3pte3p2e325",
            like?.Subject?.Uri);
    }

    [Fact]
    public void ParseFrame_Commit_UriComposesFromFlatFields()
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson).Event);

        Assert.Equal(
            "at://did:plc:eygmaihciaxprqvxpfvl6flk/app.bsky.feed.like/3l3qo2vuowo2b",
            commit.Uri.ToString());
    }

    [Fact]
    public void ParseFrame_Delete_HasNoRecordOrCid()
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(DeleteCommitJson).Event);

        Assert.Equal(RepoOpAction.Delete, commit.Operation);
        Assert.Null(commit.Record);
        Assert.Null(commit.Cid);
    }

    [Fact]
    public void ParseFrame_Identity_KeepsUpstreamSeqSeparateFromCursor()
    {
        var identity = Assert.IsType<JetstreamIdentityEvent>(Parse(IdentityJson).Event);

        Assert.Equal("yohenrique.com", identity.Handle);
        Assert.Equal(1409752997, identity.Seq);          // the relay's sequence number
        Assert.Equal(24664288883, identity.Cursor);      // Jetstream's own
    }

    [Fact]
    public void ParseFrame_Account_ReadsActiveAndStatus()
    {
        var account = Assert.IsType<JetstreamAccountEvent>(Parse(AccountJson).Event);

        Assert.False(account.Active);
        Assert.Equal("deleted", account.Status);
        Assert.Equal(24664288884, account.Cursor);
    }

    [Fact]
    public void ParseFrame_Sync_DecodesBlocksFromBytesWrapper()
    {
        var sync = Assert.IsType<JetstreamSyncEvent>(Parse(SyncJson).Event);

        Assert.Equal("3l3qo2vutsw2b", sync.Rev);
        Assert.Equal(1409753014, sync.Seq);
        Assert.Equal(24664288885, sync.Cursor);
        Assert.Equal("zeroCAR", Encoding.UTF8.GetString(sync.Blocks!));
    }

    [Fact]
    public void ParseFrame_Sync_UnpaddedBytes_DecodesBlocks()
    {
        // The data model specifies unpadded base64, which Convert.FromBase64String rejects alone.
        var sync = Assert.IsType<JetstreamSyncEvent>(
            Parse(SyncJson.Replace("emVyb0NBUg==", "emVyb0NBUg")).Event);

        Assert.Equal("zeroCAR", Encoding.UTF8.GetString(sync.Blocks!));
    }

    [Fact]
    public void ParseFrame_Sync_UndecodableBlocks_StillDeliversEvent()
    {
        // The DID and rev are enough to trigger a resync even when the CAR is unusable.
        var sync = Assert.IsType<JetstreamSyncEvent>(
            Parse(SyncJson.Replace("emVyb0NBUg==", "not base64!")).Event);

        Assert.Null(sync.Blocks);
        Assert.Equal("3l3qo2vutsw2b", sync.Rev);
    }

    [Fact]
    public void ParseFrame_Info_ReturnedOutOfBandNotAsEvent()
    {
        var frame = Parse(InfoJson);

        Assert.Null(frame.Event);
        Assert.Null(frame.Error);
        Assert.Equal("OutdatedCursor", frame.Info?.Name);
        Assert.Equal("resumed from seq 24664288000", frame.Info?.Message);
    }

    [Fact]
    public void ParseFrame_Error_ReturnedOutOfBandNotAsEvent()
    {
        var frame = Parse(ErrorJson);

        Assert.Null(frame.Event);
        Assert.Null(frame.Info);
        Assert.Equal("ConsumerTooSlow", frame.Error?.Error);
        Assert.Equal("reader below floor rate", frame.Error?.Message);
    }

    [Fact]
    public void ParseFrame_UnqualifiedTypeFragment_Dispatches()
    {
        // Dispatch is on the fragment, so a payload tagged only "#commit" still resolves.
        var json = CommitJson.Replace("network.bsky.jetstream.subscribeEvents#commit", "#commit");

        Assert.IsType<JetstreamCommitEvent>(Parse(json).Event);
    }

    [Theory]
    // No envelope at all — this is a v1 frame handed to the v2 parser.
    [InlineData("""{"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1,"kind":"commit"}""")]
    // Unknown envelope type.
    [InlineData("""{"$type":"heartbeat"}""")]
    // Unknown message type inside a known envelope.
    [InlineData("""{"$type":"message","payload":{"$type":"network.bsky.jetstream.subscribeEvents#future"}}""")]
    // Envelope with no payload.
    [InlineData("""{"$type":"message"}""")]
    // Error envelope with no error name.
    [InlineData("""{"$type":"error","message":"..."}""")]
    // Not JSON at all.
    [InlineData("not json")]
    public void ParseFrame_UnrecognizedFrames_YieldEmptyFrame(string json)
    {
        var frame = Parse(json);

        Assert.Null(frame.Event);
        Assert.Null(frame.Info);
        Assert.Null(frame.Error);
    }

    [Fact]
    public void ParseFrame_MissingTime_Skipped()
    {
        // time is required on every event message; without it there is no TimeUs to report.
        var json = CommitJson.Replace("\"time\":\"2024-09-09T19:46:02.329308Z\",", "");

        Assert.Null(Parse(json).Event);
    }

    [Theory]
    [InlineData("2024-09-09T19:46:02.329308Z")]
    [InlineData("2024-09-09T19:46:02Z")]
    [InlineData("2024-09-09T19:46:02.1234567+01:45")]
    [InlineData("2024-09-09T19:46:02.123456789Z")]      // finer than a tick: the framework rounds
    [InlineData("2024-09-09T19:46:02.99999995Z")]       // and rounds up into the next second
    [InlineData("2024-09-09T19:46:02.329-14:00")]
    [InlineData("2024-09-09T19:46:02.329+14:30")]       // beyond a DateTimeOffset's offset
    [InlineData("2024-09-09T19:46:02.329-00:00")]
    [InlineData("2024-09-09t19:46:02.329z")]
    [InlineData("2024-09-09 19:46:02.329Z")]
    [InlineData("2024-09-09T19:46:02")]
    [InlineData("2024-02-29T00:00:00Z")]
    [InlineData("2023-02-29T00:00:00Z")]
    [InlineData("2016-12-31T23:59:60Z")]
    [InlineData("0001-01-01T00:30:00+01:00")]           // before year one in UTC
    [InlineData("0001-01-01T00:30:00-01:00")]
    [InlineData("9999-12-31T23:30:00-01:00")]           // after year 9999 in UTC
    [InlineData("0000-06-01T00:00:00Z")]
    [InlineData("\\u0032024-09-09T19:46:02.329308Z")]  // escaped
    [InlineData("not a time")]
    public void ParseFrame_Time_ReadsAsTheFrameworkParserDoes(string time)
    {
        var json = CommitJson.Replace("2024-09-09T19:46:02.329308Z", time);
        var text = JsonDocument.Parse($"\"{time}\"").RootElement.GetString();
        long? expected = DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? (parsed.UtcDateTime - DateTime.UnixEpoch).Ticks / 10
            : null;

        Assert.Equal(expected, Parse(json).Event?.TimeUs);
    }

    [Theory]
    [InlineData("\"collection\":\"app.bsky.feed.like\"", "\"collection\":\"app.bsky.feed.\\u006cike\"")]
    [InlineData("\"rkey\":\"3l3qo2vuowo2b\"", "\"rkey\":\"3l3qo2vu\\u006fwo2b\"")]
    [InlineData("\"did\":\"did:plc:eygmaihciaxprqvxpfvl6flk\"", "\"did\":\"did:plc:eygmaihciaxprqvxpfvl6fl\\u006b\"")]
    [InlineData("\"rev\":\"3l3qo2vutsw2b\"", "\"rev\":\"3l3qo2vutsw2\\u0062\"")]
    [InlineData("\"operation\":\"create\"", "\"operation\":\"cr\\u0065ate\"")]
    public void ParseFrame_EscapedFields_ReadAsTheirUnescapedText(string field, string escaped)
    {
        var expected = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson).Event);
        var actual = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson.Replace(field, escaped)).Event);

        Assert.Equal(
            (expected.Did, expected.Collection, expected.Rkey, expected.Operation, expected.Rev, expected.Cid, expected.TimeUs),
            (actual.Did, actual.Collection, actual.Rkey, actual.Operation, actual.Rev, actual.Cid, actual.TimeUs));
    }

    [Theory]
    [InlineData("\"collection\":\"app.bsky.feed.like\"", "\"collection\":\"app.bsky.feed.-like\"")]
    [InlineData("\"collection\":\"app.bsky.feed.like\"", "\"collection\":7")]
    [InlineData("\"rkey\":\"3l3qo2vuowo2b\"", "\"rkey\":\"..\"")]
    [InlineData("\"operation\":\"create\"", "\"operation\":\"Create\"")]
    [InlineData("\"operation\":\"create\"", "\"operation\":null")]
    public void ParseFrame_InvalidCommitField_Skipped(string field, string invalid)
    {
        Assert.Null(Parse(CommitJson.Replace(field, invalid)).Event);
    }

    [Theory]
    [InlineData("\"rev\":\"3l3qo2vutsw2b\"", "\"rev\":\"not-a-tid\"")]
    [InlineData("\"cid\":\"bafyreidwaivazkwu67xztlmuobx35hs2lnfh3kolmgfmucldvhd3sgzcqi\"", "\"cid\":\"Qm-not-a-cid\"")]
    public void ParseFrame_InvalidOptionalField_DropsOnlyThatField(string field, string invalid)
    {
        var commit = Assert.IsType<JetstreamCommitEvent>(Parse(CommitJson.Replace(field, invalid)).Event);

        Assert.True(commit.Rev is null || commit.Cid is null);
        Assert.Equal("app.bsky.feed.like", commit.Collection);
    }

    [Fact]
    public void ParseFrame_InvalidDid_Skipped()
    {
        var json = CommitJson.Replace("did:plc:eygmaihciaxprqvxpfvl6flk", "not-a-did");

        Assert.Null(Parse(json).Event);
    }

    [Fact]
    public void ParseFrame_UnknownOperation_Skipped()
    {
        var json = CommitJson.Replace("\"operation\":\"create\"", "\"operation\":\"merge\"");

        Assert.Null(Parse(json).Event);
    }

    [Fact]
    public void ParseFrame_ExtraUnknownFields_Ignored()
    {
        var json = CommitJson.Replace("\"seq\":24664288881", "\"seq\":24664288881,\"prevRev\":\"3l3q\"");

        Assert.IsType<JetstreamCommitEvent>(Parse(json).Event);
    }

    [Fact]
    public void ParseFrame_V2FrameOnV1Protocol_Skipped()
    {
        // The envelope has no "kind", so the v1 parser must not half-read it.
        Assert.Null(ParseV1(CommitJson).Event);
    }

    [Fact]
    public void ParseFrame_V1FrameFromV2Host_ExposesCursor()
    {
        // A v2 host serving the frozen v1 wire adds its sequence number as "cursor".
        var json =
            """
            {"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329308,"cursor":12345,"kind":"commit","commit":{"rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b"}}
            """;

        var commit = Assert.IsType<JetstreamCommitEvent>(
            ParseV1(json).Event);

        Assert.Equal(12345, commit.Cursor);
        Assert.Equal(1725911162329308, commit.TimeUs);
    }

    [Fact]
    public void ParseFrame_V1FrameFromLegacyHost_HasNoCursor()
    {
        var json =
            """
            {"did":"did:plc:eygmaihciaxprqvxpfvl6flk","time_us":1725911162329308,"kind":"commit","commit":{"rev":"3l3qo2vutsw2b","operation":"create","collection":"app.bsky.feed.like","rkey":"3l3qo2vuowo2b"}}
            """;

        Assert.Null(ParseV1(json).Event!.Cursor);
    }
}
