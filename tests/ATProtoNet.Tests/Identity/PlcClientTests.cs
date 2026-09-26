using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using ATProtoNet.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// The PLC directory client, against responses plc.directory served for atproto.com's DID on
/// 2026-09-25 (trimmed where noted).
/// </summary>
public class PlcClientTests
{
    private static readonly Did TestDid = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
    private static readonly Uri Directory = new("https://plc.directory/");

    private const string AuditLogJson =
        """[{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","operation":{"sig":"lza4at_jCtGo_TYgL5PC1ZNP7lhF4DV8H50LWHhvdHcB143x1wEwqZ43xvV36Pws6OOnJLJrkibEUFDFqkhIhg","prev":null,"type":"plc_operation","services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://bsky.social"}},"alsoKnownAs":["at://atprotocol.bsky.social"],"rotationKeys":["did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg","did:key:zQ3shpKnbdPx3g3CmPf5cRVTPe1HtSwVn5ish3wSnDPQCbLJK"],"verificationMethods":{"atproto":"did:key:zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF"}},"cid":"bafyreibfvkh3n6odvdpwj54j4xxdsgnn4zo5utbyf7z7nfbyikhtygzjcq","nullified":false,"createdAt":"2023-04-26T06:19:25.508Z"},{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","operation":{"sig":"lRDLz1RRcauzDos9LZ0Q5bi3YzJXbpgrUpZ51e__tdg89xYgHiWWtnKcrAJanBMkgW0uloD40TYWMVXyZWi4mw","prev":"bafyreibfvkh3n6odvdpwj54j4xxdsgnn4zo5utbyf7z7nfbyikhtygzjcq","type":"plc_operation","services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://bsky.social"}},"alsoKnownAs":["at://atproto.com"],"rotationKeys":["did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg","did:key:zQ3shpKnbdPx3g3CmPf5cRVTPe1HtSwVn5ish3wSnDPQCbLJK"],"verificationMethods":{"atproto":"did:key:zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF"}},"cid":"bafyreihljrd4zlm6egppxzwq52fgzdrh7hv2bkjnbgyjteqasmpjgqmryi","nullified":false,"createdAt":"2023-04-26T17:16:46.827Z"}]""";

    private const string DataJson =
        """{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","verificationMethods":{"atproto":"did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w"},"rotationKeys":["did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg","did:key:zQ3shpKnbdPx3g3CmPf5cRVTPe1HtSwVn5ish3wSnDPQCbLJK"],"alsoKnownAs":["at://atproto.com"],"services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://enoki.us-east.host.bsky.network"}}}""";

    // The first two lines of `GET /export?after=0&count=2`: genesis operations in the legacy
    // `create` form, which predates `plc_operation`.
    private const string ExportJsonLines =
        """
        {"did":"did:plc:ragtjsm2j2vknwkz3zp4oxrd","cid":"bafyreieibu2mtgsovktnswo6l7dv4i4ztioutzpsy7wsasmbznzqxkpyje","seq":1,"createdAt":"2022-11-17T00:35:16.391Z","operation":{"sig":"DyaPWDItkJnVkN1izINSW-fdjUzP9BkIKlD7SnzD5axfK_870ZZ-1EYcrQLQtP9VkWcp2cdbyIHprjPfeUs8WQ","prev":null,"type":"create","handle":"paul.bsky.social","service":"https://bsky.social","signingKey":"did:key:zQ3shP5TBe1sQfSttXty15FAEHV1DZgcxRZNxvEWnPfLFwLxJ","recoveryKey":"did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg"},"type":"sequenced_op"}
        {"did":"did:plc:l3rouwludahu3ui3bt66mfvj","cid":"bafyreic64lvfs5ayb5g5cgym7xtbnkpcr6g42reni7n3c7r7i547w7wfoa","seq":2,"createdAt":"2022-11-17T00:39:19.084Z","operation":{"sig":"HIAolK_uyrcg8a6CqBT_taoF0AJ6HAwy70hutFGVM54j20rTe90MH---pm6aoe5CsiIm6khqgbr5_Aj7F3rptA","prev":null,"type":"create","handle":"divy.bsky.social","service":"https://bsky.social","signingKey":"did:key:zQ3shP5TBe1sQfSttXty15FAEHV1DZgcxRZNxvEWnPfLFwLxJ","recoveryKey":"did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg"},"type":"sequenced_op"}

        """;

    private static (PlcClient Client, ScriptedHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new ScriptedHandler(respond);
        return (new PlcClient(new HttpClient(handler), Directory), handler);
    }

    // ── Resolution ───────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ComposesTheDidOntoTheDirectoryUrl()
    {
        // A bare "did:plc:…" parses as an absolute URI with the scheme "did"; it must not escape
        // the directory.
        var (client, handler) = Create(_ => ScriptedHandler.Json(DidDocs.AtprotoDotCom));
        using var _ = client;

        var document = await client.ResolveAsync(TestDid);

        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz"), Assert.Single(handler.Requests));
        Assert.Equal(TestDid, document.Id);
        Assert.Equal(Handle.Parse("atproto.com"), document.GetHandle());
        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), document.GetPdsEndpoint());
    }

    [Fact]
    public async Task ResolveAsync_DirectoryWithAPathPrefix_KeepsThePrefix()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json(DidDocs.AtprotoDotCom));
        using var client = new PlcClient(new HttpClient(handler), new Uri("https://mirror.example.com/plc"));

        await client.ResolveAsync(TestDid);

        Assert.Equal(new Uri("https://mirror.example.com/plc/did:plc:ewvi7nxzyoun6zhxrhs64oiz"), Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, DidResolutionErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Gone, DidResolutionErrorKind.Deactivated)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DidResolutionErrorKind.HttpError)]
    public async Task ResolveAsync_ErrorStatus_IsReportedByKind(HttpStatusCode status, DidResolutionErrorKind expected)
    {
        var (client, _) = Create(_ => ScriptedHandler.Json("{}", status));
        using var __ = client;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => client.ResolveAsync(TestDid));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_DocumentForAnotherDid_IsRefused()
    {
        var (client, _) = Create(_ => ScriptedHandler.Json(DidDocs.Json("did:plc:zzzzzzzzzzzzzzzzzzzzzzzz")));
        using var __ = client;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => client.ResolveAsync(TestDid));

        Assert.Equal(DidResolutionErrorKind.InvalidDocument, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_NotAPlcDid_IsUnsupported()
    {
        var (client, handler) = Create(_ => ScriptedHandler.Json("{}"));
        using var _ = client;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => client.ResolveAsync(Did.Parse("did:web:example.com")));

        Assert.Equal(DidResolutionErrorKind.UnsupportedMethod, ex.Kind);
        Assert.Equal(0, handler.Count);
    }

    // ── Logs and state ───────────────────────────────────────

    [Fact]
    public async Task GetAuditLogAsync_ParsesEveryEntry()
    {
        var (client, handler) = Create(_ => ScriptedHandler.Json(AuditLogJson));
        using var _ = client;

        var entries = await client.GetAuditLogAsync(TestDid);

        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz/log/audit"), Assert.Single(handler.Requests));
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(TestDid, e.Did));
        Assert.Equal(Cid.Parse("bafyreibfvkh3n6odvdpwj54j4xxdsgnn4zo5utbyf7z7nfbyikhtygzjcq"), entries[0].Cid);
        Assert.False(entries[0].Nullified);
        Assert.Null(entries[0].Seq);
        Assert.Null(entries[0].Operation.Prev);
        Assert.Equal(entries[0].Cid, entries[1].Operation.Prev);
        Assert.Equal("plc_operation", entries[1].Operation.Type);
        Assert.Equal(["at://atproto.com"], entries[1].Operation.AlsoKnownAs);
        Assert.Equal("https://bsky.social", entries[1].Operation.Services!["atproto_pds"].Endpoint);
        Assert.Equal(new DateTimeOffset(2023, 4, 26, 17, 16, 46, 827, TimeSpan.Zero), entries[1].CreatedAt);
    }

    [Fact]
    public async Task GetOperationLogAsync_ParsesTheOperations()
    {
        var operations = System.Text.Json.JsonSerializer.Serialize(
            JsonNode.Parse(AuditLogJson)!.AsArray().Select(e => e!["operation"]!.DeepClone()).ToArray());
        var (client, handler) = Create(_ => ScriptedHandler.Json(operations));
        using var _ = client;

        var log = await client.GetOperationLogAsync(TestDid);

        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz/log"), Assert.Single(handler.Requests));
        Assert.Equal(2, log.Count);
        Assert.Equal(2, log[1].RotationKeys!.Count);
        Assert.Equal("did:key:zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF", log[1].VerificationMethods!["atproto"]);
    }

    [Fact]
    public async Task GetLastOperationAsync_ReadsTheLatestOperation()
    {
        var last = JsonNode.Parse(AuditLogJson)!.AsArray()[1]!["operation"]!.ToJsonString();
        var (client, handler) = Create(_ => ScriptedHandler.Json(last));
        using var _ = client;

        var operation = await client.GetLastOperationAsync(TestDid);

        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz/log/last"), Assert.Single(handler.Requests));
        Assert.Equal("lRDLz1RRcauzDos9LZ0Q5bi3YzJXbpgrUpZ51e__tdg89xYgHiWWtnKcrAJanBMkgW0uloD40TYWMVXyZWi4mw", operation.Sig);
    }

    [Fact]
    public async Task GetPlcDataAsync_ReadsTheCurrentState()
    {
        var (client, handler) = Create(_ => ScriptedHandler.Json(DataJson));
        using var _ = client;

        var data = await client.GetPlcDataAsync(TestDid);

        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz/data"), Assert.Single(handler.Requests));
        Assert.Null(data.Type);
        Assert.Null(data.Sig);
        Assert.Equal("did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", data.VerificationMethods!["atproto"]);
        Assert.Equal("https://enoki.us-east.host.bsky.network", data.Services!["atproto_pds"].Endpoint);
    }

    [Fact]
    public async Task GetPlcDataAsync_Tombstoned_IsDeactivated()
    {
        var (client, _) = Create(_ => ScriptedHandler.Json("{\"message\":\"DID not available\"}", HttpStatusCode.Gone));
        using var __ = client;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => client.GetPlcDataAsync(TestDid));

        Assert.Equal(DidResolutionErrorKind.Deactivated, ex.Kind);
    }

    [Fact]
    public async Task GetAuditLogAsync_Malformed_IsInvalidDocument()
    {
        var (client, _) = Create(_ => ScriptedHandler.Json("{\"not\":\"a list\"}"));
        using var __ = client;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => client.GetAuditLogAsync(TestDid));

        Assert.Equal(DidResolutionErrorKind.InvalidDocument, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task IsHealthyAsync_ReflectsTheHealthEndpoint(HttpStatusCode status, bool expected)
    {
        var (client, handler) = Create(_ => ScriptedHandler.Json("{\"version\":\"0.3.0\"}", status));
        using var _ = client;

        Assert.Equal(expected, await client.IsHealthyAsync());
        Assert.Equal(new Uri("https://plc.directory/_health"), Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task IsHealthyAsync_Unreachable_IsFalse()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));
        using var __ = client;

        Assert.False(await client.IsHealthyAsync());
    }

    // ── Submission ───────────────────────────────────────────

    [Fact]
    public async Task SubmitOperationAsync_PostsToTheDid()
    {
        HttpMethod? method = null;
        var (client, handler) = Create(request =>
        {
            method = request.Method;
            return ScriptedHandler.Json("{}");
        });
        using var _ = client;

        var did = await client.SubmitOperationAsync(TestDid, new JsonObject { ["type"] = "plc_operation" });

        Assert.Equal(TestDid, did);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz"), Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task SubmitOperationAsync_Rejected_SurfacesTheDirectorysMessage()
    {
        var (client, _) = Create(_ => ScriptedHandler.Json("{\"message\":\"Invalid signature on op\"}", HttpStatusCode.BadRequest));
        using var __ = client;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(
            () => client.SubmitOperationAsync(TestDid, new JsonObject { ["type"] = "plc_operation" }));

        Assert.Equal(DidResolutionErrorKind.OperationRejected, ex.Kind);
        Assert.Contains("Invalid signature on op", ex.Message);
    }

    // ── Export ───────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_ParsesTheSequencedJsonLines()
    {
        var (client, handler) = Create(_ => ScriptedHandler.Json(ExportJsonLines));
        using var _ = client;

        var entries = await client.ExportAsync(after: 0, count: 2);

        Assert.Equal(new Uri("https://plc.directory/export?after=0&count=2"), Assert.Single(handler.Requests));
        Assert.Equal(2, entries.Count);
        Assert.Equal([1L, 2L], entries.Select(e => e.Seq!.Value));
        Assert.All(entries, e => Assert.Equal("sequenced_op", e.Type));
        Assert.All(entries, e => Assert.Null(e.Nullified));
        Assert.Equal(Did.Parse("did:plc:ragtjsm2j2vknwkz3zp4oxrd"), entries[0].Did);
        Assert.Equal("create", entries[0].Operation.Type);
    }

    [Theory]
    [InlineData(-1L, null)]
    [InlineData(0L, 0)]
    [InlineData(0L, 1001)]
    public async Task ExportAsync_OutOfRangeArguments_Throw(long after, int? count)
    {
        var (client, handler) = Create(_ => ScriptedHandler.Json(""));
        using var _ = client;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ExportAsync(after, count));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task StreamExportAsync_YieldsEachMessageAndResumesFromTheCursor()
    {
        string? query = null;
        await using var server = await StreamServerAsync(async (context, socket) =>
        {
            query = context.Request.QueryString.Value;
            foreach (var line in ExportJsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                await socket.SendAsync(Encoding.UTF8.GetBytes(line.Trim()), WebSocketMessageType.Text, true, default);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, default);
        });
        var client = server.Client;

        var entries = new List<PlcAuditEntry>();
        await foreach (var entry in client.StreamExportAsync(cursor: 41))
            entries.Add(entry);

        Assert.Equal("?cursor=41", query);
        Assert.Equal([1L, 2L], entries.Select(e => e.Seq!.Value));
    }

    [Fact]
    public async Task StreamExportAsync_ClosedWithAReason_Throws()
    {
        await using var server = await StreamServerAsync((_, socket) =>
            socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "OutdatedCursor", default));
        var client = server.Client;

        var ex = await Assert.ThrowsAsync<PlcExportStreamException>(async () =>
        {
            await foreach (var _ in client.StreamExportAsync(cursor: 1))
            {
            }
        });

        Assert.Equal("OutdatedCursor", ex.CloseReason);
    }

    [Fact]
    public void StreamUrl_UsesTheWebSocketSchemeOfTheDirectory()
    {
        Uri? connected = null;
        using var client = new PlcClient(new HttpClient(), new Uri("http://localhost:2582"))
        {
            ConnectWebSocket = (url, _) =>
            {
                connected = url;
                throw new WebSocketException("stop");
            },
        };

        Assert.Throws<WebSocketException>(() => client.StreamExportAsync().GetAsyncEnumerator().MoveNextAsync().AsTask().GetAwaiter().GetResult());

        Assert.Equal(new Uri("ws://localhost:2582/export/stream"), connected);
    }

    // ── Construction ─────────────────────────────────────────

    [Fact]
    public void Constructor_PlainHttpDirectoryWithoutTheOptOut_IsRefused() =>
        Assert.Throws<ArgumentException>(() => new PlcClient(new IdentityResolverOptions
        {
            PlcDirectoryUrl = new Uri("http://localhost:2582"),
        }));

    [Fact]
    public void Constructor_PlainHttpDirectoryWithTheOptOut_IsAccepted()
    {
        using var client = new PlcClient(new IdentityResolverOptions
        {
            PlcDirectoryUrl = new Uri("http://localhost:2582"),
            AllowPrivateNetworks = true,
        });

        Assert.Equal(new Uri("http://localhost:2582/"), client.DirectoryUrl);
    }

    private static async Task<StreamServer> StreamServerAsync(Func<HttpContext, WebSocket, Task> serve)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/export/stream", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await serve(context, socket);
        });
        await app.StartAsync();

        var testServer = app.GetTestServer();
        var client = new PlcClient(new HttpClient(), new Uri("https://plc.test/"))
        {
            ConnectWebSocket = (url, ct) =>
                testServer.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost" + url.PathAndQuery), ct),
        };

        return new StreamServer(app, client);
    }

    private sealed record StreamServer(WebApplication App, PlcClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
