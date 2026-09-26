using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Sync;

/// <summary>
/// The binary <c>com.atproto.sync</c> endpoints — <c>getRecord</c>, <c>getBlocks</c> — and the
/// verified-record helper over <c>getRecord</c>.
/// </summary>
public sealed class SyncClientTests : IDisposable
{
    private static readonly JsonElement Vector = LoadVector();
    private static readonly Did Repo = Did.Parse(Vector.GetProperty("did").GetString()!);
    private static readonly string SigningKey = Vector.GetProperty("signingKey").GetString()!;
    private static readonly Nsid Collection = Nsid.Parse(Vector.GetProperty("collection").GetString()!);
    private static readonly RecordKey Rkey = RecordKey.Parse(Vector.GetProperty("rkey").GetString()!);
    private static readonly byte[] ProofCar = Convert.FromBase64String(Vector.GetProperty("presentCar").GetString()!);

    private readonly CarHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public SyncClientTests()
    {
        _httpClient = new HttpClient(_handler);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetRecordAsync_QueriesTheRecordAndStreamsTheCar()
    {
        _handler.Body = ProofCar;

        await using var response = await _client.Sync.GetRecordAsync(Repo, Collection, Rkey);
        using var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy);

        Assert.Equal(HttpMethod.Get, _handler.LastMethod);
        Assert.Equal(
            $"/xrpc/com.atproto.sync.getRecord?did={Repo}&collection={Collection}&rkey={Rkey}",
            Uri.UnescapeDataString(_handler.LastUri!.PathAndQuery));
        Assert.Equal("application/vnd.ipld.car", response.ContentType);
        Assert.Equal(ProofCar, copy.ToArray());
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_ReferenceProof_ReturnsTheVerifiedRecord()
    {
        _handler.Body = ProofCar;

        var record = await _client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey);

        Assert.True(record.Exists);
        Assert.Equal(Vector.GetProperty("recordCid").GetString(), record.Cid!.Value);
        Assert.Equal("the proven record", record.Value!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_ChunkedResponse_IsReadWhole()
    {
        _handler.Body = ProofCar;
        _handler.DeclareLength = false;

        var record = await _client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey);

        Assert.True(record.Exists);
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_WrongSigningKey_Throws()
    {
        _handler.Body = ProofCar;
        using var other = ATProtoNet.Crypto.AtProtoCrypto.GenerateK256Key();

        await Assert.ThrowsAsync<RepoVerificationException>(
            () => _client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, other.ToDidKey()));
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_DeclaredLengthOverTheCeiling_ThrowsWithoutReading()
    {
        _handler.Body = ProofCar;
        _handler.DeclaredLength = 64L * 1024 * 1024;

        var ex = await Assert.ThrowsAsync<RepoVerificationException>(
            () => _client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey));

        Assert.Contains("larger than", ex.Message);
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_ChunkedBodyOverTheCeiling_Throws()
    {
        _handler.Body = new byte[16 * 1024 * 1024 + 1];
        _handler.DeclareLength = false;

        var ex = await Assert.ThrowsAsync<RepoVerificationException>(
            () => _client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey));

        Assert.Contains("larger than", ex.Message);
    }

    [Fact]
    public async Task GetBlocksAsync_RepeatsTheCidsParameter()
    {
        _handler.Body = ProofCar;
        var first = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");
        var second = Cid.Parse(Vector.GetProperty("recordCid").GetString()!);

        await using var response = await _client.Sync.GetBlocksAsync(Repo, [first, second]);
        var car = await CarReader.FromStreamAsync(response.Content);

        Assert.Equal(HttpMethod.Get, _handler.LastMethod);
        Assert.Equal(
            $"/xrpc/com.atproto.sync.getBlocks?did={Repo}&cids={first}&cids={second}",
            Uri.UnescapeDataString(_handler.LastUri!.PathAndQuery));
        Assert.Equal(5, car.Blocks.Count);
    }

    private static JsonElement LoadVector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Repo", "TestData", "record-proof.json");
        return JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.Clone();
    }

    /// <summary>Answers every request with a CAR body, declaring its length or sending it chunked.</summary>
    private sealed class CarHandler : HttpMessageHandler
    {
        public byte[] Body { get; set; } = [];

        public bool DeclareLength { get; set; } = true;

        public long? DeclaredLength { get; set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;

            HttpContent content = DeclareLength
                ? new ByteArrayContent(Body)
                : new StreamContent(new NonSeekableStream(Body));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ipld.car");
            if (DeclaredLength is { } declared)
                content.Headers.ContentLength = declared;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>A body with no length to report, as a chunked response has.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
