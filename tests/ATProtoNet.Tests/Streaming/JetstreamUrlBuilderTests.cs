using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamUrlBuilderTests
{
    private sealed class NoopDecompressor : IJetstreamDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> frame) => frame.ToArray();
    }

    private static JetstreamConsumerOptions Options(
        string serviceUrl = "wss://jetstream2.us-east.bsky.network",
        IReadOnlyList<string>? collections = null,
        IReadOnlyList<string>? dids = null,
        long? maxMessageSizeBytes = null,
        IJetstreamDecompressor? decompressor = null) => new()
    {
        ServiceUrl = serviceUrl,
        WantedCollections = collections,
        WantedDids = dids,
        MaxMessageSizeBytes = maxMessageSizeBytes,
        Decompressor = decompressor,
    };

    [Fact]
    public void BuildSubscribeUri_NoFilters_ReturnsBareSubscribe()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(), cursor: null);

        Assert.Equal("wss://jetstream2.us-east.bsky.network/subscribe", uri.ToString());
    }

    [Fact]
    public void BuildSubscribeUri_Collections_EmitsRepeatedParams()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(collections: ["exchange.recipe.recipe", "exchange.recipe.collection"]), cursor: null);

        Assert.Equal(
            "wss://jetstream2.us-east.bsky.network/subscribe" +
            "?wantedCollections=exchange.recipe.recipe&wantedCollections=exchange.recipe.collection",
            uri.ToString());
    }

    [Fact]
    public void BuildSubscribeUri_WildcardCollection_SurvivesEscaping()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(collections: ["app.bsky.graph.*"]), cursor: null);

        Assert.Contains("wantedCollections=", uri.Query);
        Assert.Equal("app.bsky.graph.*", Uri.UnescapeDataString(uri.Query["?wantedCollections=".Length..]));
    }

    [Fact]
    public void BuildSubscribeUri_Cursor_Appended()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(), cursor: 1725911162329308);

        Assert.Contains("cursor=1725911162329308", uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_Dids_EmitsRepeatedParams()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options(dids: ["did:plc:abc123", "did:web:example.com"]), cursor: null);

        var query = Uri.UnescapeDataString(uri.Query);
        Assert.Contains("wantedDids=did:plc:abc123", query);
        Assert.Contains("wantedDids=did:web:example.com", query);
    }

    [Fact]
    public void BuildSubscribeUri_MaxMessageSize_Appended()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(maxMessageSizeBytes: 1_000_000), cursor: null);

        Assert.Contains("maxMessageSizeBytes=1000000", uri.Query);
    }

    [Fact]
    public void BuildSubscribeUri_NoDecompressor_OmitsCompress()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(), cursor: null);

        Assert.DoesNotContain("compress", uri.ToString());
    }

    [Fact]
    public void BuildSubscribeUri_WithDecompressor_RequestsCompression()
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(decompressor: new NoopDecompressor()), cursor: null);

        Assert.Contains("compress=true", uri.Query);
    }

    [Theory]
    [InlineData("https://jetstream2.us-east.bsky.network", "wss")]
    [InlineData("http://localhost:6008", "ws")]
    [InlineData("wss://jetstream2.us-east.bsky.network", "wss")]
    public void BuildSubscribeUri_HttpSchemes_ConvertedToWebSocket(string serviceUrl, string expectedScheme)
    {
        var uri = JetstreamClient.BuildSubscribeUri(Options(serviceUrl), cursor: null);

        Assert.Equal(expectedScheme, uri.Scheme);
    }

    [Fact]
    public void BuildSubscribeUri_TrailingSlash_Trimmed()
    {
        var uri = JetstreamClient.BuildSubscribeUri(
            Options("wss://jetstream2.us-east.bsky.network/"), cursor: null);

        Assert.Equal("wss://jetstream2.us-east.bsky.network/subscribe", uri.ToString());
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
    public void JetstreamClient_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JetstreamClient(null!));
    }

    [Fact]
    public void JetstreamClient_EmptyServiceUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JetstreamClient(new JetstreamConsumerOptions
        {
            ServiceUrl = "  ",
        }));
    }
}
