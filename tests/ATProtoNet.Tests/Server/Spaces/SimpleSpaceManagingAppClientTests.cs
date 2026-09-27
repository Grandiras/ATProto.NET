using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// Pins the wire shape of the <c>checkUserAccess</c> call a <c>simplespace</c> authority makes to
/// a managing app — the one outbound call the policy tests stub away.
/// </summary>
public class SimpleSpaceManagingAppClientTests
{
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly Did UserDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did AppDid = Did.Parse("did:web:app.example.com");
    private static readonly string ManagingApp = AppDid + "#forum";
    private const string ClientId = "https://app.example.com/client-metadata.json";

    private static SpaceUri Space => SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");

    private readonly HttpStub _handler = new HttpStub().Fallback("""{"authorized":true}""");

    private SimpleSpaceManagingAppClient CreateClient(
        string endpoint = "https://app.example.com", ISpaceAccountSigner? accountSigner = null, Did? serviceDid = null)
    {
        var resolver = new FakeDidDocumentResolver().Publish(AppDid, new DidDocument
        {
            Id = AppDid,
            Service =
            [
                new DidDocumentService { Id = "#forum", Type = "BulletinManagingApp", Endpoint = endpoint },
            ],
        });

        return new SimpleSpaceManagingAppClient(
            resolver,
            new ServiceAuthGenerator(serviceDid ?? AuthorityDid, AtProtoCrypto.GenerateP256Key()),
            new HttpClient(_handler),
            accountSigner);
    }

    [Fact]
    public async Task CheckUserAccessAsync_ReadCheck_SendsUserAccessAndClientId()
    {
        // The Lexicon parameter is `user`; a managing app such as bulletin answers 400 to
        // anything else, which the policy turns into a refusal for everyone.
        Assert.True(await CreateClient().CheckUserAccessAsync(
            ManagingApp, Space, UserDid, SpaceAccessKind.Read, ClientId));

        var query = ParseQuery(_handler.Requests[^1].Uri);
        Assert.Equal("https://app.example.com/xrpc/com.atproto.simplespace.checkUserAccess",
            _handler.Requests[^1].Uri.GetLeftPart(UriPartial.Path));
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

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CheckUserAccessAsync_WriteCheck_OmitsTheClientId()
    {
        await CreateClient().CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Write, ClientId);

        var query = ParseQuery(_handler.Requests[^1].Uri);
        Assert.Equal("write", query["access"]);
        Assert.False(query.ContainsKey("clientId"));
    }

    [Fact]
    public async Task CheckUserAccessAsync_AddressesTheServiceIdentifierWithItsFragment()
    {
        // A managing app verifies `aud` against its own service identifier, fragment and all.
        await CreateClient().CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Read, null);

        var claims = TestJws.DecodeJson(_handler.Requests[^1].Headers.Authorization!.Parameter!, 1);
        Assert.Equal(ManagingApp, claims.GetProperty("aud").GetString());
        Assert.Equal(AuthorityDid, claims.GetProperty("iss").GetString());
        Assert.Equal(SpaceNsids.CheckUserAccess, claims.GetProperty("lxm").GetString());
    }

    [Fact]
    public async Task CheckUserAccessAsync_OnAHostServingTheAuthoritysAccount_SignsAsTheAuthority()
    {
        // A managing app answers checkUserAccess only for the space's authority. A multi-account
        // host whose service DID is not the authority signs with the authority's own key.
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var signer = new TestAccountSigner().Add(AuthorityDid, authorityKey);
        var client = CreateClient(accountSigner: signer, serviceDid: Did.Parse("did:web:pds.example.com"));

        await client.CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Read, null);

        var claims = TestJws.DecodeJson(_handler.Requests[^1].Headers.Authorization!.Parameter!, 1);
        Assert.Equal(AuthorityDid, claims.GetProperty("iss").GetString());
        Assert.Equal([AuthorityDid], signer.Requests);
    }

    [Fact]
    public async Task CheckUserAccessAsync_WithoutTheAuthoritysKey_SignsAsTheService()
    {
        var serviceDid = Did.Parse("did:web:pds.example.com");
        var client = CreateClient(accountSigner: new TestAccountSigner(), serviceDid: serviceDid);

        await client.CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Read, null);

        var claims = TestJws.DecodeJson(_handler.Requests[^1].Headers.Authorization!.Parameter!, 1);
        Assert.Equal(serviceDid, claims.GetProperty("iss").GetString());
    }

    [Fact]
    public async Task CheckUserAccessAsync_WhenTheServiceIsTheAuthority_StillAsksTheSigner()
    {
        // An authority signing credentials with a dedicated #atproto_space key needs the signer to
        // supply its #atproto key for its own DID; the service generator holds the wrong one.
        using var accountKey = AtProtoCrypto.GenerateP256Key();
        var signer = new TestAccountSigner().Add(AuthorityDid, accountKey);

        await CreateClient(accountSigner: signer).CheckUserAccessAsync(ManagingApp, Space, UserDid, SpaceAccessKind.Read, null);

        Assert.Equal([AuthorityDid], signer.Requests);
        var parts = _handler.Requests[^1].Headers.Authorization!.Parameter!.Split('.');
        Assert.True(accountKey.Verify(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), TestJws.Decode(parts[2])));
    }

    [Fact]
    public async Task CheckUserAccessAsync_ErrorResponse_Throws()
    {
        _handler.Fallback(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient().CheckUserAccessAsync(
            ManagingApp, Space, UserDid, SpaceAccessKind.Read, null));
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
}
