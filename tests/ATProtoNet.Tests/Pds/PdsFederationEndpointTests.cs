using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Pds;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Pds;

public sealed class PdsFederationEndpointTests : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;

    public PdsFederationEndpointTests()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddAtProtoPds(opts =>
                    {
                        opts.Hostname = "test.local";
                        opts.PublicUrl = "https://test.local";
                        opts.OpenRegistration = true;
                        opts.AvailableUserDomains = ["test.local"];
                        opts.DidMethod = PdsDidMethod.Web;
                    });
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseWebSockets();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapAtProtoPds());
                });
            })
            .Build();

        _host.Start();
        _client = _host.GetTestServer().CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<(string Did, string Jwt)> CreateAccountAsync(string handle = "alice.test.local")
    {
        var response = await _client.PostAsJsonAsync("/xrpc/com.atproto.server.createAccount", new
        {
            handle,
            email = $"{handle}@test.local",
            password = "password123",
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("did").GetString()!, json.GetProperty("accessJwt").GetString()!);
    }

    private async Task CreatePostAsync(string did, string jwt, string text, string rkey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.createRecord")
        {
            Content = JsonContent.Create(new
            {
                repo = did,
                collection = "app.bsky.feed.post",
                rkey,
                record = new { text, createdAt = "2026-07-25T00:00:00Z" },
            }),
        };
        request.Headers.Authorization = new("Bearer", jwt);

        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    // ── getRepo ──────────────────────────────────────────────

    [Fact]
    public async Task GetRepo_ReturnsAVerifiableCarFile()
    {
        var (did, jwt) = await CreateAccountAsync();
        await CreatePostAsync(did, jwt, "hello", "aaa");

        var response = await _client.GetAsync($"/xrpc/com.atproto.sync.getRepo?did={did}");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/vnd.ipld.car", response.Content.Headers.ContentType?.MediaType);

        var car = await response.Content.ReadAsByteArrayAsync();
        var reader = CarReader.FromBytes(car, verifyBlockCids: true);

        Assert.Single(reader.Roots);
        Assert.NotEmpty(reader.Blocks);
    }

    [Fact]
    public async Task GetRepo_UnknownDid_ReturnsRepoNotFound()
    {
        var response = await _client.GetAsync("/xrpc/com.atproto.sync.getRepo?did=did:plc:nobody");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RepoNotFound", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetRepo_MissingDid_ReturnsBadRequest()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.GetAsync("/xrpc/com.atproto.sync.getRepo")).StatusCode);
    }

    // ── getLatestCommit / getRepoStatus ──────────────────────

    [Fact]
    public async Task GetLatestCommit_ReturnsTheHeadCidAndRev()
    {
        var (did, jwt) = await CreateAccountAsync();
        await CreatePostAsync(did, jwt, "hello", "aaa");

        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.getLatestCommit?did={did}");

        Assert.StartsWith("bafyrei", json.GetProperty("cid").GetString());
        Assert.Equal(13, json.GetProperty("rev").GetString()!.Length);
    }

    [Fact]
    public async Task GetLatestCommit_AdvancesAfterEachWrite()
    {
        var (did, jwt) = await CreateAccountAsync();

        var first = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.getLatestCommit?did={did}");
        await CreatePostAsync(did, jwt, "hello", "aaa");
        var second = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.getLatestCommit?did={did}");

        Assert.True(string.CompareOrdinal(
            second.GetProperty("rev").GetString(), first.GetProperty("rev").GetString()) > 0);
    }

    [Fact]
    public async Task GetRepoStatus_ReturnsActiveWithTheCurrentRev()
    {
        var (did, _) = await CreateAccountAsync();

        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.getRepoStatus?did={did}");

        Assert.Equal(did, json.GetProperty("did").GetString());
        Assert.True(json.GetProperty("active").GetBoolean());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("rev").GetString()));
    }

    // ── listRepos ────────────────────────────────────────────

    [Fact]
    public async Task ListRepos_ListsEveryHostedRepository()
    {
        var (alice, _) = await CreateAccountAsync("alice.test.local");
        var (bob, _) = await CreateAccountAsync("bob.test.local");

        var json = await _client.GetFromJsonAsync<JsonElement>("/xrpc/com.atproto.sync.listRepos");
        var dids = json.GetProperty("repos").EnumerateArray()
            .Select(r => r.GetProperty("did").GetString()).ToList();

        Assert.Contains(alice, dids);
        Assert.Contains(bob, dids);

        var entry = json.GetProperty("repos")[0];
        Assert.StartsWith("bafyrei", entry.GetProperty("head").GetString());
        Assert.True(entry.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task ListRepos_ReportsDeactivatedAccountsAsInactive()
    {
        // listRepos used to hardcode active=true, so a relay enumerating repos saw a different
        // picture than getRepoStatus gave for the same DID.
        var (did, _) = await CreateAccountAsync();

        var accounts = _host.Services.GetRequiredService<IAccountStore>();
        var account = await accounts.GetByDidAsync(did);
        account!.IsActive = false;
        await accounts.UpdateAsync(account);

        var json = await _client.GetFromJsonAsync<JsonElement>("/xrpc/com.atproto.sync.listRepos");
        var entry = json.GetProperty("repos").EnumerateArray()
            .Single(r => r.GetProperty("did").GetString() == did);

        Assert.False(entry.GetProperty("active").GetBoolean());
        Assert.Equal("deactivated", entry.GetProperty("status").GetString());

        var status = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.getRepoStatus?did={did}");
        Assert.Equal(status.GetProperty("active").GetBoolean(), entry.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task ListRepos_OmitsStatusForActiveRepositories()
    {
        var (did, _) = await CreateAccountAsync();

        var json = await _client.GetFromJsonAsync<JsonElement>("/xrpc/com.atproto.sync.listRepos");
        var entry = json.GetProperty("repos").EnumerateArray()
            .Single(r => r.GetProperty("did").GetString() == did);

        Assert.True(entry.GetProperty("active").GetBoolean());
        Assert.False(entry.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task ListRepos_PagesWithACursor()
    {
        await CreateAccountAsync("alice.test.local");
        await CreateAccountAsync("bob.test.local");
        await CreateAccountAsync("carol.test.local");

        var first = await _client.GetFromJsonAsync<JsonElement>("/xrpc/com.atproto.sync.listRepos?limit=2");
        Assert.Equal(2, first.GetProperty("repos").GetArrayLength());

        var cursor = first.GetProperty("cursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));

        var second = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.listRepos?limit=2&cursor={Uri.EscapeDataString(cursor!)}");

        Assert.Equal(1, second.GetProperty("repos").GetArrayLength());
    }

    // ── sync.getRecord / getBlocks / listBlobs ───────────────

    [Fact]
    public async Task SyncGetRecord_ReturnsACarProofContainingTheRecord()
    {
        var (did, jwt) = await CreateAccountAsync();
        await CreatePostAsync(did, jwt, "proof me", "xyz");

        var response = await _client.GetAsync(
            $"/xrpc/com.atproto.sync.getRecord?did={did}&collection=app.bsky.feed.post&rkey=xyz");
        response.EnsureSuccessStatusCode();

        var reader = CarReader.FromBytes(await response.Content.ReadAsByteArrayAsync(), verifyBlockCids: true);
        var byCid = reader.Blocks.ToDictionary(
            b => CidComputation.EncodeCidToString(b.Cid), b => b.Data, StringComparer.Ordinal);

        var commit = DagCborDecoder.Decode(byCid[CidComputation.EncodeCidToString(reader.Roots[0])]);
        var mst = MerkleSearchTree.Deserialize(
            CidComputation.DecodeCidString(commit.GetProperty("data").GetProperty("$link").GetString()!),
            cid => byCid.GetValueOrDefault(cid));

        var recordCid = mst.Get("app.bsky.feed.post/xyz");
        Assert.NotNull(recordCid);

        var record = DagCborDecoder.Decode(byCid[CidComputation.EncodeCidToString(recordCid!)]);
        Assert.Equal("proof me", record.GetProperty("text").GetString());
    }

    [Fact]
    public async Task GetBlocks_ReturnsTheRequestedBlock()
    {
        var (did, jwt) = await CreateAccountAsync();
        await CreatePostAsync(did, jwt, "blocky", "aaa");

        var head = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.getLatestCommit?did={did}");
        var commitCid = head.GetProperty("cid").GetString()!;

        var response = await _client.GetAsync(
            $"/xrpc/com.atproto.sync.getBlocks?did={did}&cids={commitCid}");
        response.EnsureSuccessStatusCode();

        var reader = CarReader.FromBytes(await response.Content.ReadAsByteArrayAsync(), verifyBlockCids: true);
        Assert.Single(reader.Blocks);
        Assert.Equal(commitCid, CidComputation.EncodeCidToString(reader.Blocks[0].Cid));
    }

    [Fact]
    public async Task GetBlocks_MissingCids_ReturnsBadRequest()
    {
        var (did, _) = await CreateAccountAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.GetAsync($"/xrpc/com.atproto.sync.getBlocks?did={did}")).StatusCode);
    }

    [Fact]
    public async Task ListBlobs_ReturnsUploadedBlobCids()
    {
        var (did, jwt) = await CreateAccountAsync();

        using var upload = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.uploadBlob")
        {
            Content = new ByteArrayContent("an image"u8.ToArray()),
        };
        upload.Content.Headers.ContentType = new("image/png");
        upload.Headers.Authorization = new("Bearer", jwt);

        var uploadResponse = await _client.SendAsync(upload);
        uploadResponse.EnsureSuccessStatusCode();
        var blobCid = (await uploadResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("blob").GetProperty("cid").GetString();

        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/xrpc/com.atproto.sync.listBlobs?did={did}");

        Assert.Equal([blobCid], json.GetProperty("cids").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task UploadBlob_ReturnsARawCodecCid()
    {
        var (_, jwt) = await CreateAccountAsync();
        var data = "an image"u8.ToArray();

        using var upload = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.uploadBlob")
        {
            Content = new ByteArrayContent(data),
        };
        upload.Content.Headers.ContentType = new("image/png");
        upload.Headers.Authorization = new("Bearer", jwt);

        var json = await (await _client.SendAsync(upload)).Content.ReadFromJsonAsync<JsonElement>();
        var cid = json.GetProperty("blob").GetProperty("cid").GetString();

        // Raw-codec CIDv1 with a SHA-256 multihash renders with the "bafkrei" prefix.
        Assert.Equal(CidComputation.ComputeForRaw(data).Value, cid);
        Assert.StartsWith("bafkrei", cid);
    }

    // ── Identity ─────────────────────────────────────────────

    [Fact]
    public async Task ResolveHandle_ReturnsTheDid()
    {
        var (did, _) = await CreateAccountAsync();

        var json = await _client.GetFromJsonAsync<JsonElement>(
            "/xrpc/com.atproto.identity.resolveHandle?handle=alice.test.local");

        Assert.Equal(did, json.GetProperty("did").GetString());
    }

    [Fact]
    public async Task ResolveHandle_UnknownHandle_ReturnsHandleNotFound()
    {
        var response = await _client.GetAsync(
            "/xrpc/com.atproto.identity.resolveHandle?handle=nobody.test.local");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HandleNotFound", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WellKnownAtprotoDid_ResolvesTheRequestHostAsAHandle()
    {
        var (did, _) = await CreateAccountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://alice.test.local/.well-known/atproto-did");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(did, (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task WellKnownAtprotoDid_UnknownHost_Returns404()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://nobody.test.local/.well-known/atproto-did");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task WellKnownDidJson_ServesTheDidWebDocument()
    {
        var (did, _) = await CreateAccountAsync();
        Assert.Equal("did:web:alice.test.local", did);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://alice.test.local/.well-known/did.json");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(did, json.GetProperty("id").GetString());
        Assert.Equal("at://alice.test.local", json.GetProperty("alsoKnownAs")[0].GetString());
        Assert.Equal("https://test.local",
            json.GetProperty("service")[0].GetProperty("serviceEndpoint").GetString());
        Assert.Equal("Multikey", json.GetProperty("verificationMethod")[0].GetProperty("type").GetString());
        Assert.StartsWith("z", json.GetProperty("verificationMethod")[0].GetProperty("publicKeyMultibase").GetString());
    }

    [Fact]
    public async Task WellKnownDidJson_UnknownHost_Returns404()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://nobody.test.local/.well-known/did.json");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(request)).StatusCode);
    }

    // ── subscribeRepos ───────────────────────────────────────

    [Fact]
    public async Task SubscribeRepos_NonWebSocketRequest_Returns426()
    {
        var response = await _client.GetAsync("/xrpc/com.atproto.sync.subscribeRepos");
        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeRepos_StreamsLiveCommitEvents()
    {
        var (did, jwt) = await CreateAccountAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await _host.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://test.local/xrpc/com.atproto.sync.subscribeRepos"), cts.Token);

        await CreatePostAsync(did, jwt, "over the wire", "aaa");

        var message = await ReceiveFrameAsync(socket, cts.Token);
        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(message));

        Assert.Equal(did, parsed.Repo);
        var op = Assert.Single(parsed.Ops!);
        Assert.Equal("create", op.Action);
        Assert.Equal("app.bsky.feed.post/aaa", op.Path);
    }

    [Fact]
    public async Task SubscribeRepos_WithCursor_ReplaysTheBacklog()
    {
        var (did, _) = await CreateAccountAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await _host.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://test.local/xrpc/com.atproto.sync.subscribeRepos?cursor=0"), cts.Token);

        // Account creation published #account, #identity and #commit before we connected;
        // a cursor of 0 must replay all three.
        var account = Assert.IsType<AccountEvent>(FirehoseEventParser.Parse(await ReceiveFrameAsync(socket, cts.Token)));
        var identity = Assert.IsType<IdentityEvent>(FirehoseEventParser.Parse(await ReceiveFrameAsync(socket, cts.Token)));
        var commit = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(await ReceiveFrameAsync(socket, cts.Token)));

        Assert.Equal(did, account.Did);
        Assert.True(account.Active);
        Assert.Equal("alice.test.local", identity.Handle);
        Assert.Equal(did, commit.Repo);
        Assert.Equal([1L, 2L, 3L], new[] { account.Seq, identity.Seq, commit.Seq });
    }

    [Fact]
    public async Task SubscribeRepos_FutureCursor_SendsAnErrorFrameAndCloses()
    {
        await CreateAccountAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await _host.GetTestServer().CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://test.local/xrpc/com.atproto.sync.subscribeRepos?cursor=999999"), cts.Token);

        var frame = await ReceiveFrameAsync(socket, cts.Token);

        // Error frames use op:-1, which the regular parser deliberately rejects.
        Assert.Null(FirehoseEventParser.Parse(frame));
        Assert.Contains("FutureCursor", System.Text.Encoding.UTF8.GetString(frame));
    }

    [Fact]
    public async Task SubscribeRepos_InvalidCursor_IsRejectedBeforeTheUpgrade()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // A non-numeric cursor never reaches the sequencer: the handshake itself fails, so the
        // caller sees a connect error rather than an apparently healthy but empty stream.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _host.GetTestServer().CreateWebSocketClient()
                .ConnectAsync(new Uri("ws://test.local/xrpc/com.atproto.sync.subscribeRepos?cursor=abc"), cts.Token));
    }

    private static async Task<byte[]> ReceiveFrameAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var result = await socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("The firehose closed before delivering a frame.");

            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) return buffer.ToArray();
        }
    }
}
