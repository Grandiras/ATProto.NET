using System.Net;
using System.Text;
using ATProtoNet.Server.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The size ceiling on the client-metadata and JWKS documents
/// <see cref="HttpSpaceClientMetadataResolver"/> fetches. The URL comes from the attestation being
/// verified, so the fetch is attacker-directed and the ceiling has to hold whether or not the
/// server declares a length.
/// </summary>
public class HttpSpaceClientMetadataResolverTests
{
    private const string ClientId = "https://app.example.com/client-metadata.json";
    private const string JwksUri = "https://app.example.com/jwks.json";
    private const int Cap = 2048;

    private const string Key =
        """{"kty":"EC","crv":"P-256","kid":"key-1","x":"l8tFrhx-34tV3hRICRDY9zCkDlpBhF42UQUfWVAWBFs","y":"9VE4jf_Ok_o64zbTTlcuNJajHmt6v9TDVrU0CdvGRDA"}""";

    private static readonly string InlineMetadata = $$$"""{"client_id":"{{{ClientId}}}","jwks":{"keys":[{{{Key}}}]}}""";
    private static readonly string RemoteMetadata = $$"""{"client_id":"{{ClientId}}","jwks_uri":"{{JwksUri}}"}""";
    private static readonly string Jwks = $$"""{"keys":[{{Key}}]}""";

    private static HttpSpaceClientMetadataResolver CreateResolver(Func<Uri, HttpContent> respond) =>
        new(
            new HttpClient(new StubHandler(respond)),
            new SpaceServerOptions { MaxClientMetadataBytes = Cap });

    /// <summary>Pads a JSON document with trailing whitespace to exactly <paramref name="bytes"/> bytes.</summary>
    private static byte[] PadTo(string json, int bytes) =>
        Encoding.UTF8.GetBytes(json + new string(' ', bytes - Encoding.UTF8.GetByteCount(json)));

    [Fact]
    public async Task ResolveKeysAsync_InlineKeys_ReturnsThem()
    {
        var resolver = CreateResolver(_ => new StringContent(InlineMetadata));

        var keys = await resolver.ResolveKeysAsync(ClientId);

        Assert.Equal("key-1", Assert.Single(keys).Kid);
    }

    [Fact]
    public async Task ResolveKeysAsync_UnsizedDocumentExactlyAtTheCeiling_IsAccepted()
    {
        var resolver = CreateResolver(_ => new UnsizedContent(PadTo(InlineMetadata, Cap)));

        var keys = await resolver.ResolveKeysAsync(ClientId);

        Assert.Single(keys);
    }

    [Fact]
    public async Task ResolveKeysAsync_DocumentDeclaredLargerThanTheCeiling_IsRejected()
    {
        var resolver = CreateResolver(_ => new ByteArrayContent(PadTo(InlineMetadata, Cap + 1)));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => resolver.ResolveKeysAsync(ClientId));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task ResolveKeysAsync_UnsizedDocumentOneByteOverTheCeiling_IsRejected()
    {
        var resolver = CreateResolver(_ => new UnsizedContent(PadTo(InlineMetadata, Cap + 1)));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => resolver.ResolveKeysAsync(ClientId));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task ResolveKeysAsync_UnsizedJwksOverTheCeiling_IsRejected()
    {
        var resolver = CreateResolver(uri => uri.AbsoluteUri == JwksUri
            ? new UnsizedContent(PadTo(Jwks, Cap * 4))
            : new StringContent(RemoteMetadata));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => resolver.ResolveKeysAsync(ClientId));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task ResolveKeysAsync_JwksWithinTheCeiling_ReturnsItsKeys()
    {
        var resolver = CreateResolver(uri => uri.AbsoluteUri == JwksUri
            ? new UnsizedContent(Encoding.UTF8.GetBytes(Jwks))
            : new StringContent(RemoteMetadata));

        var keys = await resolver.ResolveKeysAsync(ClientId);

        Assert.Equal("key-1", Assert.Single(keys).Kid);
    }

    private sealed class StubHandler(Func<Uri, HttpContent> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = respond(request.RequestUri!),
                RequestMessage = request,
            });
    }

    /// <summary>A body with no <c>Content-Length</c>, as a chunked response arrives.</summary>
    private sealed class UnsizedContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
