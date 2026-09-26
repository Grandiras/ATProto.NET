using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.identity.resolveIdentity</c>, <c>resolveDid</c> and <c>refreshIdentity</c>:
/// delegating resolution to a service.
/// </summary>
public class IdentityClientTests : IDisposable
{
    private const string DidText = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private static readonly string IdentityInfoJson =
        $$"""{"did":"{{DidText}}","handle":"atproto.com","didDoc":{{DidDocs.AtprotoDotCom}}}""";

    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public IdentityClientTests()
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
    public async Task ResolveIdentityAsync_SendsTheIdentifierAndParsesTheIdentity()
    {
        _handler.Body = IdentityInfoJson;

        var info = await _client.Identity.ResolveIdentityAsync(AtIdentifier.Parse("atproto.com"));

        Assert.Equal(
            "https://pds.example.com/xrpc/com.atproto.identity.resolveIdentity?identifier=atproto.com",
            _handler.LastUri);
        Assert.Equal(Did.Parse(DidText), info.Did);
        Assert.Equal(Handle.Parse("atproto.com"), info.Handle);
        Assert.Equal(Did.Parse(DidText), info.DidDoc.Id);
        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), info.DidDoc.GetPdsEndpoint());
    }

    [Fact]
    public async Task ResolveIdentityAsync_UnverifiedHandle_IsHandleInvalid()
    {
        _handler.Body = IdentityInfoJson.Replace("\"handle\":\"atproto.com\"", "\"handle\":\"handle.invalid\"", StringComparison.Ordinal);

        var info = await _client.Identity.ResolveIdentityAsync(AtIdentifier.Parse(DidText));

        Assert.Equal(Handle.Invalid, info.Handle);
    }

    [Fact]
    public async Task ResolveIdentityAsync_HandleNotFound_SurfacesTheLexiconError()
    {
        _handler.Status = HttpStatusCode.BadRequest;
        _handler.Body = """{"error":"HandleNotFound","message":"Unable to resolve handle"}""";

        var ex = await Assert.ThrowsAnyAsync<XrpcException>(
            () => _client.Identity.ResolveIdentityAsync(AtIdentifier.Parse("nobody.example.com")));

        Assert.True(ex.Is(XrpcErrors.HandleNotFound));
    }

    [Fact]
    public async Task ResolveDidAsync_SendsTheDidAndParsesTheDocument()
    {
        _handler.Body = $$"""{"didDoc":{{DidDocs.AtprotoDotCom}}}""";

        var response = await _client.Identity.ResolveDidAsync(Did.Parse(DidText));

        Assert.Equal(
            $"https://pds.example.com/xrpc/com.atproto.identity.resolveDid?did={DidText}",
            Uri.UnescapeDataString(_handler.LastUri!));
        Assert.Equal("did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", response.DidDoc.GetSigningKey());
    }

    [Fact]
    public async Task RefreshIdentityAsync_PostsTheIdentifier()
    {
        _handler.Body = IdentityInfoJson;

        var info = await _client.Identity.RefreshIdentityAsync(AtIdentifier.Parse(DidText));

        Assert.Equal("https://pds.example.com/xrpc/com.atproto.identity.refreshIdentity", _handler.LastUri);
        using var body = JsonDocument.Parse(_handler.LastBody!);
        Assert.Equal(DidText, body.RootElement.GetProperty("identifier").GetString());
        Assert.Equal(Handle.Parse("atproto.com"), info.Handle);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri!.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
