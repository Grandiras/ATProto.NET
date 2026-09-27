using ATProtoNet.Identity;
using ATProtoNet.Streaming;
using static ATProtoNet.Streaming.JetstreamProtocol;

namespace ATProtoNet.Tests.Streaming;

/// <summary>The subscription URL <see cref="JetstreamConsumer"/> connects to, on either wire protocol.</summary>
public class JetstreamEndpointTests
{
    private sealed class NoopDecompressor : IJetstreamDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> frame) => frame.ToArray();
    }

    private static JetstreamConsumerOptions Options(
        JetstreamProtocol protocol,
        string serviceUrl = "wss://jetstream.test",
        IReadOnlyList<string>? collections = null,
        IReadOnlyList<string>? dids = null,
        IReadOnlyList<JetstreamEventKind>? kinds = null,
        long? maxMessageSizeBytes = null,
        IJetstreamDecompressor? decompressor = null,
        int? zstdDictionaryId = null) => new()
    {
        ServiceUrl = serviceUrl,
        Protocol = protocol,
        WantedCollections = collections,
        WantedDids = dids?.Select(Did.Parse).ToList(),
        WantedKinds = kinds,
        MaxMessageSizeBytes = maxMessageSizeBytes,
        Decompressor = decompressor,
        ZstdDictionaryId = zstdDictionaryId,
    };

    [Theory]
    [InlineData(V1, "wss://jetstream.test", "wss://jetstream.test/subscribe")]
    [InlineData(V1, "https://jetstream.test/", "wss://jetstream.test/subscribe")]
    [InlineData(V1, "http://localhost:6008", "ws://localhost:6008/subscribe")]
    [InlineData(V2, "wss://jetstream.test/", "wss://jetstream.test/xrpc/network.bsky.jetstream.subscribeEvents")]
    [InlineData(V2, "https://jetstream.test", "wss://jetstream.test/xrpc/network.bsky.jetstream.subscribeEvents")]
    [InlineData(V2, "http://localhost:6008", "ws://localhost:6008/xrpc/network.bsky.jetstream.subscribeEvents")]
    public void Endpoint_IsTheProtocolsPathOnTheWebSocketUrl(JetstreamProtocol protocol, string serviceUrl, string expected)
    {
        // v1 is the default, so existing configurations keep their URL.
        var options = protocol == V1 ? new JetstreamConsumerOptions { ServiceUrl = serviceUrl } : Options(protocol, serviceUrl);

        Assert.Equal(expected, JetstreamConsumer.Endpoint(options, cursor: null).ToString());
    }

    [Theory]
    [InlineData(V1, null,
        "?wantedCollections=app.bsky.feed.post&wantedCollections=app.bsky.graph.*&wantedDids=did:plc:abc123&wantedDids=did:web:example.com&cursor=24664288881&maxMessageSizeBytes=1000000")]
    [InlineData(V2, new[] { JetstreamEventKind.Commit, JetstreamEventKind.Account, JetstreamEventKind.Sync },
        "?collections=app.bsky.feed.post&collections=app.bsky.graph.*&dids=did:plc:abc123&dids=did:web:example.com&kinds=commit&kinds=account&kinds=sync&cursor=24664288881&maxMessageSizeBytes=1000000")]
    public void Endpoint_Filters_UseTheProtocolsParameterNames(JetstreamProtocol protocol, JetstreamEventKind[]? kinds, string expected)
    {
        // v2 renamed the v1 filters; sending the old names is a pre-upgrade 400. Wildcards survive escaping.
        var uri = JetstreamConsumer.Endpoint(
            Options(protocol,
                collections: ["app.bsky.feed.post", "app.bsky.graph.*"],
                dids: ["did:plc:abc123", "did:web:example.com"],
                kinds: kinds,
                maxMessageSizeBytes: 1_000_000),
            cursor: 24664288881);

        Assert.Equal(expected, Uri.UnescapeDataString(uri.Query));
    }

    [Theory]
    [InlineData(V1, false, "")]
    [InlineData(V1, true, "?compress=true")]
    [InlineData(V2, true, "?zstdDictionary=7")]
    public void Endpoint_Compression_UsesTheProtocolsScheme(JetstreamProtocol protocol, bool compressed, string expected)
    {
        var options = Options(protocol,
            decompressor: compressed ? new NoopDecompressor() : null,
            zstdDictionaryId: compressed && protocol == V2 ? 7 : null);

        Assert.Equal(expected, JetstreamConsumer.Endpoint(options, cursor: null).Query);
    }

    public static TheoryData<JetstreamConsumerOptions, string> Invalid => new()
    {
        { Options(V1, serviceUrl: "  "), "ServiceUrl" },
        { Options(V2, collections: [.. Enumerable.Range(0, 101).Select(i => $"com.example.col{i}")]), "at most 100 wantedCollections" },
        { Options(V1, dids: [.. Enumerable.Range(0, 10_001).Select(i => $"did:plc:x{i}")]), "at most 10000 wantedDids" },
        { Options(V2, maxMessageSizeBytes: 4_294_967_296), "MaxMessageSizeBytes" },
        // A collection filter only constrains commit events, so the server rejects this
        // pre-upgrade rather than serving a filter that could never apply.
        { Options(V2, collections: ["app.bsky.feed.post"], kinds: [JetstreamEventKind.Account]), "WantedKinds list that excludes" },
        { Options(V2, decompressor: new NoopDecompressor()), "set ZstdDictionaryId" },
        { Options(V2, zstdDictionaryId: 7), "requires a Decompressor" },
        // v1 has no kinds filter, so silently dropping the list would deliver more than asked.
        { Options(V1, kinds: [JetstreamEventKind.Commit]), "WantedKinds requires JetstreamProtocol.V2" },
        { Options(V1, zstdDictionaryId: 7), "ZstdDictionaryId requires JetstreamProtocol.V2" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Endpoint_OptionsNoServerAccepts_Throw(JetstreamConsumerOptions options, string reason)
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => JetstreamConsumer.Endpoint(options, cursor: null));

        Assert.Contains(reason, ex.Message);
    }
}
