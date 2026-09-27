using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.identity.resolveIdentity</c>, <c>resolveDid</c> and <c>refreshIdentity</c>:
/// delegating resolution to a service.
/// </summary>
public class IdentityClientTests : IDisposable
{
    private const string DidText = TestIds.ModDid;

    private static readonly string IdentityInfoJson =
        $$"""{"did":"{{DidText}}","handle":"atproto.com","didDoc":{{DidDocs.AtprotoDotCom}}}""";

    private readonly XrpcTestClient _fixture = new();

    public IdentityClientTests() => _fixture.Fallback("{}");

    public void Dispose() => _fixture.Dispose();

    private HttpStub.RecordedRequest Last => _fixture.Last;

    [Fact]
    public async Task ResolveIdentityAsync_SendsTheIdentifierAndParsesTheIdentity()
    {
        _fixture.Fallback(IdentityInfoJson);

        var info = await _fixture.Client.Identity.ResolveIdentityAsync(AtIdentifier.Parse("atproto.com"));

        Assert.Equal(
            "https://pds.example.com/xrpc/com.atproto.identity.resolveIdentity?identifier=atproto.com",
            Last.Uri.ToString());
        Assert.Equal(Did.Parse(DidText), info.Did);
        Assert.Equal(Handle.Parse("atproto.com"), info.Handle);
        Assert.Equal(Did.Parse(DidText), info.DidDoc.Id);
        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), info.DidDoc.GetPdsEndpoint());
    }

    [Fact]
    public async Task ResolveIdentityAsync_UnverifiedHandle_IsHandleInvalid()
    {
        _fixture.Fallback(IdentityInfoJson.Replace("\"handle\":\"atproto.com\"", "\"handle\":\"handle.invalid\"", StringComparison.Ordinal));

        var info = await _fixture.Client.Identity.ResolveIdentityAsync(AtIdentifier.Parse(DidText));

        Assert.Equal(Handle.Invalid, info.Handle);
    }

    [Fact]
    public async Task ResolveIdentityAsync_HandleNotFound_SurfacesTheLexiconError()
    {
        _fixture.Fallback(_ => HttpStub.JsonResponse(
            """{"error":"HandleNotFound","message":"Unable to resolve handle"}""", HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAnyAsync<XrpcException>(
            () => _fixture.Client.Identity.ResolveIdentityAsync(AtIdentifier.Parse("nobody.example.com")));

        Assert.True(ex.Is(XrpcErrors.HandleNotFound));
    }

    [Fact]
    public async Task ResolveDidAsync_SendsTheDidAndParsesTheDocument()
    {
        _fixture.Fallback($$"""{"didDoc":{{DidDocs.AtprotoDotCom}}}""");

        var response = await _fixture.Client.Identity.ResolveDidAsync(Did.Parse(DidText));

        Assert.Equal(
            $"https://pds.example.com/xrpc/com.atproto.identity.resolveDid?did={DidText}",
            Uri.UnescapeDataString(Last.Uri.ToString()));
        Assert.Equal("did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", response.DidDoc.GetSigningKey());
    }

    [Fact]
    public async Task RefreshIdentityAsync_PostsTheIdentifier()
    {
        _fixture.Fallback(IdentityInfoJson);

        var info = await _fixture.Client.Identity.RefreshIdentityAsync(AtIdentifier.Parse(DidText));

        Assert.Equal("https://pds.example.com/xrpc/com.atproto.identity.refreshIdentity", Last.Uri.ToString());
        Assert.Equal(DidText, Last.JsonBody.GetProperty("identifier").GetString());
        Assert.Equal(Handle.Parse("atproto.com"), info.Handle);
    }
}
