using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamV2UrlBuilderTests
{
    private const string V2Path = "/xrpc/network.bsky.jetstream.subscribeEvents";

    private sealed class NoopDecompressor : IJetstreamDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> frame) => frame.ToArray();
    }

    private static JetstreamConsumerOptions Options(
        string serviceUrl = JetstreamEndpoints.UsEast,
        IReadOnlyList<string>? collections = null,
        IReadOnlyList<string>? dids = null,
        IReadOnlyList<JetstreamEventKind>? kinds = null,
        long? maxMessageSizeBytes = null,
        IJetstreamDecompressor? decompressor = null,
        int? zstdDictionaryId = null) => new()
    {
        ServiceUrl = serviceUrl,
        Protocol = JetstreamProtocol.V2,
        WantedCollections = collections,
        WantedDids = dids,
        WantedKinds = kinds,
        MaxMessageSizeBytes = maxMessageSizeBytes,
        Decompressor = decompressor,
        ZstdDictionaryId = zstdDictionaryId,
    };

    [Fact]
    public void BuildSubscribeUri_NoFilters_UsesLexiconCanonicalPath()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(), cursor: null);

        Assert.Equal(JetstreamEndpoints.UsEast + V2Path, uri.ToString());
    }

    [Fact]
    public void BuildSubscribeUri_Collections_UsesV2ParameterName()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(collections: ["app.bsky.feed.post", "app.bsky.feed.like"]), cursor: null);

        // v2 renamed the v1 filters; sending the old names is a pre-upgrade 400.
        Assert.DoesNotContain("wantedCollections", uri.Query);
        Assert.Equal(
            "?collections=app.bsky.feed.post&collections=app.bsky.feed.like",
            uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_Dids_UsesV2ParameterName()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(dids: ["did:plc:abc123", "did:web:example.com"]), cursor: null);

        var query = Uri.UnescapeDataString(uri.Query);
        Assert.DoesNotContain("wantedDids", query);
        Assert.Contains("dids=did:plc:abc123", query);
        Assert.Contains("dids=did:web:example.com", query);
    }

    [Fact]
    public void BuildSubscribeUri_Kinds_EmitsLowercaseFragmentNames()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(kinds: [JetstreamEventKind.Commit, JetstreamEventKind.Account, JetstreamEventKind.Sync]),
            cursor: null);

        Assert.Equal("?kinds=commit&kinds=account&kinds=sync", uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_WildcardCollection_SurvivesEscaping()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(collections: ["app.bsky.graph.*"]), cursor: null);

        Assert.Equal("app.bsky.graph.*", Uri.UnescapeDataString(uri.Query["?collections=".Length..]));
    }

    [Fact]
    public void BuildSubscribeUri_Cursor_Appended()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(), cursor: 24664288881);

        Assert.Contains("cursor=24664288881", uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_Compression_EmitsDictionaryId()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(decompressor: new NoopDecompressor(), zstdDictionaryId: 7), cursor: null);

        // v1's bare compress=true is not the v2 scheme.
        Assert.DoesNotContain("compress=true", uri.Query);
        Assert.Contains("zstdDictionary=7", uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_MaxMessageSize_Appended()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(maxMessageSizeBytes: 1_000_000), cursor: null);

        Assert.Contains("maxMessageSizeBytes=1000000", uri.Query);
    }

    [Theory]
    [InlineData("https://jetstream.us-east.bsky.network", "wss")]
    [InlineData("http://localhost:6008", "ws")]
    public void BuildSubscribeUri_HttpSchemes_ConvertedToWebSocket(string serviceUrl, string expectedScheme)
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(serviceUrl), cursor: null);

        Assert.Equal(expectedScheme, uri.Scheme);
        Assert.Equal(V2Path, uri.AbsolutePath);
    }

    [Fact]
    public void BuildSubscribeUri_CollectionsWithoutCommitKind_Throws()
    {
        // A collection filter only constrains commit events, so the server rejects this
        // pre-upgrade rather than serving a filter that could never apply.
        var ex = Assert.Throws<ArgumentException>(() => JetstreamClient.BuildSubscribeUri(
            Options(collections: ["app.bsky.feed.post"], kinds: [JetstreamEventKind.Account]),
            cursor: null));

        Assert.Contains("WantedKinds", ex.Message);
    }

    [Fact]
    public void BuildSubscribeUri_CollectionsWithCommitKind_Allowed()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(collections: ["app.bsky.feed.post"],
                kinds: [JetstreamEventKind.Commit, JetstreamEventKind.Account]),
            cursor: null);

        Assert.Contains("collections=app.bsky.feed.post", uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_DecompressorWithoutDictionaryId_Throws()
    {
        Assert.Throws<ArgumentException>(() => JetstreamClient.BuildSubscribeUri(
            Options(decompressor: new NoopDecompressor()), cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_DictionaryIdWithoutDecompressor_Throws()
    {
        Assert.Throws<ArgumentException>(() => JetstreamClient.BuildSubscribeUri(
            Options(zstdDictionaryId: 7), cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_TooManyCollections_Throws()
    {
        var collections = Enumerable.Range(0, 101).Select(i => $"com.example.col{i}").ToList();

        Assert.Throws<ArgumentException>(
            () => JetstreamClient.BuildSubscribeUri(Options(collections: collections), cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_TooManyDids_Throws()
    {
        var dids = Enumerable.Range(0, 10_001).Select(i => $"did:plc:x{i}").ToList();

        Assert.Throws<ArgumentException>(
            () => JetstreamClient.BuildSubscribeUri(Options(dids: dids), cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_MaxMessageSizeOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => JetstreamClient.BuildSubscribeUri(
            Options(maxMessageSizeBytes: 4_294_967_296), cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_KindsOnV1_Throws()
    {
        // v1 has no kinds filter, so silently dropping the list would deliver more than asked.
        Assert.Throws<ArgumentException>(() => JetstreamClient.BuildSubscribeUri(
            new JetstreamConsumerOptions
            {
                ServiceUrl = JetstreamEndpoints.LegacyUsEast2,
                WantedKinds = [JetstreamEventKind.Commit],
            },
            cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_DictionaryIdOnV1_Throws()
    {
        Assert.Throws<ArgumentException>(() => JetstreamClient.BuildSubscribeUri(
            new JetstreamConsumerOptions
            {
                ServiceUrl = JetstreamEndpoints.LegacyUsEast2,
                ZstdDictionaryId = 7,
            },
            cursor: null));
    }

    [Fact]
    public void BuildSubscribeUri_V1Default_UnchangedByV2Support()
    {
        // The default protocol stays v1, so existing configurations keep their URL.
        var uri = JetstreamClient.BuildSubscribeUri(
            new JetstreamConsumerOptions
            {
                ServiceUrl = JetstreamEndpoints.LegacyUsEast2,
                WantedCollections = ["app.bsky.feed.post"],
            },
            cursor: null);

        Assert.Equal(
            JetstreamEndpoints.LegacyUsEast2 + "/subscribe?wantedCollections=app.bsky.feed.post",
            uri.ToString());
    }
}
