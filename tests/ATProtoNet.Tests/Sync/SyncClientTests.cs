using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Repo;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Sync;

/// <summary>
/// The binary <c>com.atproto.sync</c> endpoints — <c>getRecord</c>, <c>getBlocks</c> — and the
/// verified-record helper over <c>getRecord</c>.
/// </summary>
public sealed class SyncClientTests : IDisposable
{
    private const string GetRecord = "com.atproto.sync.getRecord";
    private const string GetBlocks = "com.atproto.sync.getBlocks";

    private static readonly JsonElement Vector = LoadVector();
    private static readonly Did Repo = Did.Parse(Vector.GetProperty("did").GetString()!);
    private static readonly string SigningKey = Vector.GetProperty("signingKey").GetString()!;
    private static readonly Nsid Collection = Nsid.Parse(Vector.GetProperty("collection").GetString()!);
    private static readonly RecordKey Rkey = RecordKey.Parse(Vector.GetProperty("rkey").GetString()!);
    private static readonly byte[] ProofCar = Convert.FromBase64String(Vector.GetProperty("presentCar").GetString()!);

    private readonly XrpcTestClient _fixture = new();

    private AtProtoClient Client => _fixture.Client;

    public void Dispose() => _fixture.Dispose();

    /// <summary>Scripts a CAR body, declaring its length or sending it chunked (as <c>DeclareLength: false</c> does).</summary>
    private void RespondWithCar(string nsid, byte[] body, bool declareLength = true, long? declaredLength = null)
    {
        _fixture.On(nsid, _ =>
        {
            HttpContent content = declareLength
                ? new ByteArrayContent(body)
                : new StreamContent(new NonSeekableStream(body));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ipld.car");
            if (declaredLength is { } declared)
                content.Headers.ContentLength = declared;

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
    }

    [Fact]
    public async Task GetRecordAsync_QueriesTheRecordAndStreamsTheCar()
    {
        RespondWithCar(GetRecord, ProofCar);

        await using var response = await Client.Sync.GetRecordAsync(Repo, Collection, Rkey);
        using var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy);

        var sent = _fixture.To(GetRecord).Single();
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(
            $"/xrpc/com.atproto.sync.getRecord?did={Repo}&collection={Collection}&rkey={Rkey}",
            Uri.UnescapeDataString(sent.Uri.PathAndQuery));
        Assert.Equal("application/vnd.ipld.car", response.ContentType);
        Assert.Equal(ProofCar, copy.ToArray());
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_ReferenceProof_ReturnsTheVerifiedRecord()
    {
        RespondWithCar(GetRecord, ProofCar);

        var record = await Client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey);

        Assert.True(record.Exists);
        Assert.Equal(Vector.GetProperty("recordCid").GetString(), record.Cid!.Value);
        Assert.Equal("the proven record", record.Value!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_ChunkedResponse_IsReadWhole()
    {
        RespondWithCar(GetRecord, ProofCar, declareLength: false);

        var record = await Client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey);

        Assert.True(record.Exists);
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_WrongSigningKey_Throws()
    {
        RespondWithCar(GetRecord, ProofCar);
        using var other = ATProtoNet.Crypto.AtProtoCrypto.GenerateK256Key();

        await Assert.ThrowsAsync<RepoVerificationException>(
            () => Client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, other.ToDidKey()));
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_DeclaredLengthOverTheCeiling_ThrowsWithoutReading()
    {
        RespondWithCar(GetRecord, ProofCar, declaredLength: 64L * 1024 * 1024);

        var ex = await Assert.ThrowsAsync<RepoVerificationException>(
            () => Client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey));

        Assert.Contains("larger than", ex.Message);
    }

    [Fact]
    public async Task GetVerifiedRecordAsync_ChunkedBodyOverTheCeiling_Throws()
    {
        RespondWithCar(GetRecord, new byte[16 * 1024 * 1024 + 1], declareLength: false);

        var ex = await Assert.ThrowsAsync<RepoVerificationException>(
            () => Client.Sync.GetVerifiedRecordAsync(Repo, Collection, Rkey, SigningKey));

        Assert.Contains("larger than", ex.Message);
    }

    [Fact]
    public async Task GetBlocksAsync_RepeatsTheCidsParameter()
    {
        RespondWithCar(GetBlocks, ProofCar);
        var first = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");
        var second = Cid.Parse(Vector.GetProperty("recordCid").GetString()!);

        await using var response = await Client.Sync.GetBlocksAsync(Repo, [first, second]);
        var car = await CarReader.FromStreamAsync(response.Content);

        var sent = _fixture.To(GetBlocks).Single();
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(
            $"/xrpc/com.atproto.sync.getBlocks?did={Repo}&cids={first}&cids={second}",
            Uri.UnescapeDataString(sent.Uri.PathAndQuery));
        Assert.Equal(5, car.Blocks.Count);
    }

    private static JsonElement LoadVector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Repo", "TestData", "record-proof.json");
        return JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.Clone();
    }

    /// <summary>A body with no length to report, as a chunked response has.</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
