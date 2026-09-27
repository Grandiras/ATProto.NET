using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.identity.resolveIdentity</c>: delegating resolution to a service. <c>resolveDid</c>
/// and <c>refreshIdentity</c> are rows in <see cref="EndpointRequestTests"/>.
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
}
