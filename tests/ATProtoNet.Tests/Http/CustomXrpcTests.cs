using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Tests.TestSupport;
using NSubstitute;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// Custom XRPC: the typed <c>QueryAsync</c> / <c>ProcedureAsync</c> entry points, and the
/// <see cref="IXrpcTransport"/> seam third-party Lexicon packages build sub-clients on.
/// </summary>
public sealed class CustomXrpcTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private static readonly Nsid ListItems = Nsid.Parse("com.example.todo.listItems");
    private static readonly Nsid UpdateStatus = Nsid.Parse("com.example.todo.updateStatus");

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    // Mutable, like the fixture it replaces: each test points the next answer at whatever
    // it is about to assert on, and the stub's routes read it lazily at call time.
    private HttpStatusCode _status = HttpStatusCode.OK;
    private string _body = "{}";
    private string _contentType = "application/json";

    public CustomXrpcTests()
    {
        _httpClient = new HttpClient(_stub);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            _httpClient);

        foreach (var nsid in new[]
        {
            "com.example.todo.listItems", "com.example.todo.updateStatus",
            "com.example.blob.get", "com.example.blob.put",
            "com.atproto.server.createSession", "com.atproto.server.deleteSession",
        })
        {
            _stub.On(nsid, BuildResponse);
        }
    }

    private HttpResponseMessage BuildResponse(HttpStub.RecordedRequest _)
    {
        var content = new StringContent(_body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
        return new HttpResponseMessage(_status) { Content = content };
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _stub.Dispose();
    }

    // ──────────────────────────────────────────────────────────
    //  AtProtoClient entry points
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_WithXrpcParams_SendsThemInOrderAndReadsTheOutput()
    {
        _body = """{"items":["a","b"],"cursor":"next"}""";

        var result = await _client.QueryAsync<ListItemsOutput>(
            ListItems,
            new XrpcParams { { "limit", 25 }, { "reverse", true }, { "cursor", (string?)null } }.AddAll("tag", ["x", "y"]));

        var request = Assert.Single(_stub.To("com.example.todo.listItems"));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://pds.example.com/xrpc/com.example.todo.listItems?limit=25&reverse=true&tag=x&tag=y",
            request.Uri.AbsoluteUri);
        Assert.Equal(["a", "b"], result.Items);
        Assert.Equal("next", result.Cursor);
    }

    [Fact]
    public async Task QueryAsync_WithAnAnonymousObject_SendsTheSameParameters()
    {
        _body = """{"items":[]}""";

        await _client.QueryAsync<ListItemsOutput>(ListItems, new { limit = 25, reverse = true, tag = new[] { "x", "y" } });

        Assert.EndsWith("?limit=25&reverse=true&tag=x&tag=y", Assert.Single(_stub.To("com.example.todo.listItems")).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ProcedureAsyncWithOutput_PostsTheInputAndReadsTheOutput()
    {
        _body = """{"status":"done"}""";

        var result = await _client.ProcedureAsync<UpdateStatusInput, StatusOutput>(
            UpdateStatus,
            new UpdateStatusInput { Rkey = "abc", Status = "done" },
            new XrpcParams().Add("dryRun", false));

        var request = Assert.Single(_stub.To("com.example.todo.updateStatus"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://pds.example.com/xrpc/com.example.todo.updateStatus?dryRun=false", request.Uri.AbsoluteUri);
        Assert.Equal("""{"rkey":"abc","status":"done"}""", request.BodyText);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("done", result.Status);
    }

    [Fact]
    public async Task ProcedureAsyncWithInput_PostsTheInputAndIgnoresAnyOutput()
    {
        _body = "";

        await _client.ProcedureAsync(UpdateStatus, new UpdateStatusInput { Rkey = "abc", Status = "open" });

        Assert.Equal("""{"rkey":"abc","status":"open"}""", Assert.Single(_stub.To("com.example.todo.updateStatus")).BodyText);
    }

    [Fact]
    public async Task ProcedureAsyncWithoutInput_PostsNoBody()
    {
        await _client.ProcedureAsync(UpdateStatus, new XrpcParams().Add("rkey", "abc"));

        var request = Assert.Single(_stub.To("com.example.todo.updateStatus"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("?rkey=abc", request.Uri.AbsoluteUri);
        Assert.Empty(request.BodyText);
    }

    [Fact]
    public async Task ProcedureAsync_WithANullInput_PostsNoBody()
    {
        await _client.ProcedureAsync<UpdateStatusInput?>(UpdateStatus, null);

        Assert.Empty(Assert.Single(_stub.To("com.example.todo.updateStatus")).BodyText);
    }

    [Fact]
    public async Task QueryAsync_WhenTheOutputDoesNotMatch_ThrowsResponseFormatException()
    {
        _body = """{"items":"not-an-array"}""";

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => _client.QueryAsync<ListItemsOutput>(ListItems));

        Assert.Equal("com.example.todo.listItems", ex.Nsid);
    }

    [Fact]
    public async Task EntryPoints_NullNsid_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.QueryAsync<JsonElement>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ProcedureAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.Transport.DownloadAsync(null!));
        Assert.Empty(_stub.Requests);
    }

    // ──────────────────────────────────────────────────────────
    //  The transport seam
    // ──────────────────────────────────────────────────────────

    /// <summary>A sub-client for a Lexicon the SDK does not ship, the way a package would write it.</summary>
    private sealed class TodoClient(IXrpcTransport transport)
    {
        public Task<ListItemsOutput> ListItemsAsync(int? limit = null, CancellationToken ct = default) =>
            transport.QueryAsync<ListItemsOutput>(ListItems, new XrpcParams().Add("limit", limit), cancellationToken: ct);

        public Task<StatusOutput> UpdateStatusAsync(string rkey, string status, CancellationToken ct = default) =>
            transport.ProcedureAsync<UpdateStatusInput, StatusOutput>(
                UpdateStatus, new UpdateStatusInput { Rkey = rkey, Status = status }, cancellationToken: ct);
    }

    [Fact]
    public async Task Transport_SubClientCalls_CarryTheClientsSessionAndFollowSignOut()
    {
        var todo = new TodoClient(_client.Transport);

        _body = """{"items":[]}""";
        await todo.ListItemsAsync();
        Assert.Null(_stub.To("com.example.todo.listItems").Last().Authorization);

        await SignInAsync();
        _body = """{"status":"done"}""";
        await todo.UpdateStatusAsync("abc", "done");
        Assert.Equal("Bearer access-1", _stub.To("com.example.todo.updateStatus").Last().Authorization);

        await _client.LogoutAsync();
        _body = """{"items":[]}""";
        await todo.ListItemsAsync(limit: 5);
        var last = _stub.To("com.example.todo.listItems").Last();
        Assert.Null(last.Authorization);
        Assert.EndsWith("?limit=5", last.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Transport_ServiceErrors_SurfaceAsXrpcExceptions()
    {
        _status = HttpStatusCode.BadRequest;
        _body = """{"error":"ItemNotFound","message":"No such item"}""";

        var ex = await Assert.ThrowsAsync<XrpcException>(() => new TodoClient(_client.Transport).ListItemsAsync());

        Assert.True(ex.Is("ItemNotFound"));
        Assert.Equal("com.example.todo.listItems", ex.Nsid);
    }

    [Fact]
    public async Task Transport_DownloadAndUpload_MoveBinaryBodies()
    {
        _body = "blob-bytes";
        _contentType = "application/octet-stream";

        await using (var download = await _client.Transport.DownloadAsync(
            Nsid.Parse("com.example.blob.get"), new XrpcParams().Add("cid", "abc")))
        {
            using var reader = new StreamReader(download.Content);
            Assert.Equal("blob-bytes", await reader.ReadToEndAsync());
            Assert.Equal("application/octet-stream", download.ContentType);
        }

        _body = """{"status":"stored"}""";
        _contentType = "application/json";
        using var data = new MemoryStream("PNG"u8.ToArray());

        var stored = await _client.Transport.UploadAsync<StatusOutput>(
            Nsid.Parse("com.example.blob.put"), data, "image/png", new XrpcParams().Add("part", 1));

        var upload = Assert.Single(_stub.To("com.example.blob.put"));
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.EndsWith("/xrpc/com.example.blob.put?part=1", upload.Uri.AbsoluteUri);
        Assert.Equal("PNG", upload.BodyText);
        Assert.Equal("image/png", upload.ContentType);
        Assert.Equal("stored", stored.Status);
    }

    [Fact]
    public async Task Transport_CanBeFakedForASubClientsOwnTests()
    {
        var transport = Substitute.For<IXrpcTransport>();
        transport.QueryAsync<ListItemsOutput>(ListItems, Arg.Any<XrpcParams?>(), Arg.Any<XrpcCallOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ListItemsOutput { Items = ["fake"] });

        var result = await new TodoClient(transport).ListItemsAsync(limit: 3);

        Assert.Equal(["fake"], result.Items);
        await transport.Received(1).QueryAsync<ListItemsOutput>(
            ListItems,
            Arg.Is<XrpcParams?>(p => p != null && p.Single().Key == "limit" && p.Single().Value == "3"),
            null,
            Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private async Task SignInAsync()
    {
        _body = $$"""{"did":"{{DidText}}","handle":"alice.test","accessJwt":"access-1","refreshJwt":"refresh-1"}""";
        await _client.LoginAsync("alice.test", "password");
    }

    private sealed class ListItemsOutput
    {
        [JsonPropertyName("items")]
        public IReadOnlyList<string> Items { get; init; } = [];

        [JsonPropertyName("cursor")]
        public string? Cursor { get; init; }
    }

    private sealed class UpdateStatusInput
    {
        [JsonPropertyName("rkey")]
        public required string Rkey { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }
    }

    private sealed class StatusOutput
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
