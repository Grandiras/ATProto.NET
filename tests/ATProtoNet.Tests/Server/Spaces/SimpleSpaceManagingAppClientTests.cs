using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// Pins the wire shape of the <c>checkUserAccess</c> call a <c>simplespace</c> authority makes to
/// a managing app — the one outbound call the policy tests stub away.
/// </summary>
public class SimpleSpaceManagingAppClientTests
{
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string UserDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AppDid = "did:web:app.example.com";
    private const string ManagingApp = AppDid + "#forum";
    private const string ClientId = "https://app.example.com/client-metadata.json";

    private static SpaceUri Space => SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");

    private readonly RecordingHandler _handler = new();

    private SimpleSpaceManagingAppClient CreateClient(string endpoint = "https://app.example.com")
    {
        var resolver = new FakeDidDocumentResolver().Publish(AppDid, new DidDocument
        {
            Id = AppDid,
            Service =
            [
                new ServiceEndpoint { Id = "#forum", Type = "BulletinManagingApp", Endpoint = endpoint },
            ],
        });

        return new SimpleSpaceManagingAppClient(
            resolver,
            new ServiceAuthGenerator(AuthorityDid, AtProtoCrypto.GenerateP256Key()),
            new HttpClient(_handler));
    }

    [Fact]
    public async Task CheckUserAccessAsync_ReadCheck_SendsUserAccessAndClientId()
    {
        // The Lexicon parameter is `user`; a managing app such as bulletin answers 400 to
        // anything else, which the policy turns into a refusal for everyone.
        Assert.True(await CreateClient().CheckUserAccessAsync(
            ManagingApp, Space, UserDid, SpaceAccessKind.Read, ClientId));

        var query = ParseQuery(_handler.LastRequest!.RequestUri!);
        Assert.Equal("https://app.example.com/xrpc/com.atproto.simplespace.checkUserAccess",
            _handler.LastRequest.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal(Space.Value, query["space"]);
        Assert.Equal(UserDid, query["user"]);
        Assert.Equal("read", query["access"]);
        Assert.Equal(ClientId, query["clientId"]);
        Assert.False(query.ContainsKey("did"));
    }

    [Theory]
    [InlineData("https://app.example.com/?tenant=1")]
    [InlineData("https://app.example.com/#frag")]
    [InlineData("app.example.com")]
    public async Task CheckUserAccessAsync_WithAnUnusableEndpoint_FailsAsTheResolutionFailureThePolicyRefusesOn(
        string endpoint)
    {
        // The policy treats InvalidOperationException as "unreachable", which is a refusal.
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateClient(endpoint).CheckUserAccessAsync(
            ManagingApp, Space, UserDid, SpaceAccessKind.Read, ClientId));

        Assert.Null(_handler.LastRequest);
    }

    [Fact]
    public async Task CheckUserAccessAsync_WriteCheck_OmitsTheClientId()
    {
        await CreateClient().CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Write, ClientId);

        var query = ParseQuery(_handler.LastRequest!.RequestUri!);
        Assert.Equal("write", query["access"]);
        Assert.False(query.ContainsKey("clientId"));
    }

    [Fact]
    public async Task CheckUserAccessAsync_AddressesTheServiceIdentifierWithItsFragment()
    {
        // A managing app verifies `aud` against its own service identifier, fragment and all.
        await CreateClient().CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Read, null);

        var claims = DecodePayload(_handler.LastRequest!.Headers.Authorization!.Parameter!);
        Assert.Equal(ManagingApp, claims.GetProperty("aud").GetString());
        Assert.Equal(AuthorityDid, claims.GetProperty("iss").GetString());
        Assert.Equal(SpaceNsids.CheckUserAccess, claims.GetProperty("lxm").GetString());
    }

    [Fact]
    public async Task CheckUserAccessAsync_ErrorResponse_Throws()
    {
        _handler.Status = HttpStatusCode.BadRequest;

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient().CheckUserAccessAsync(
            ManagingApp, Space, UserDid, SpaceAccessKind.Read, null));
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

    private static JsonElement DecodePayload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');
        return JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(part));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("""{"authorized":true}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
