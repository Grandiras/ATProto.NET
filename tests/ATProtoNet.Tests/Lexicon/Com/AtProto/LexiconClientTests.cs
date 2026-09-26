using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <c>com.atproto.lexicon.resolveLexicon</c>: Lexicon resolution delegated to a service, directly
/// and as an <see cref="ILexiconResolver"/>.
/// </summary>
public sealed class LexiconClientTests
{
    private static readonly Nsid Post = Nsid.Parse("com.example.lexicon.post");

    private const string ResolvedJson = """
        {
          "uri": "at://did:plc:lexiconvectorlexiconvect/com.atproto.lexicon.schema/com.example.lexicon.post",
          "cid": "bafyreibleucvt34j2gzwyzpamdn4vcqvwoheqhqdhn57qqivqkgfjwvfdy",
          "schema": {
            "$type": "com.atproto.lexicon.schema",
            "lexicon": 1,
            "id": "com.example.lexicon.post",
            "defs": { "main": { "type": "record", "key": "tid", "record": { "type": "object", "properties": {} } } },
            "x-extra": true
          }
        }
        """;

    [Fact]
    public async Task ResolveLexiconAsync_SendsTheNsidAndParsesTheSchema()
    {
        using var client = Create(_ => ScriptedHandler.Json(ResolvedJson), out var handler);

        var resolved = await client.Lexicon.ResolveLexiconAsync(Post);

        Assert.Equal(
            "https://pds.example.com/xrpc/com.atproto.lexicon.resolveLexicon?nsid=com.example.lexicon.post",
            Assert.Single(handler.Requests).AbsoluteUri);
        Assert.Equal(Post, resolved.Schema.Id);
        Assert.Equal("record", resolved.Schema.MainType);
        Assert.Equal(Cid.Parse("bafyreibleucvt34j2gzwyzpamdn4vcqvwoheqhqdhn57qqivqkgfjwvfdy"), resolved.Cid);
        Assert.True(resolved.Schema.ExtensionData!["x-extra"].GetBoolean());
    }

    [Fact]
    public async Task ResolveAsync_LexiconNotFound_IsNotFound()
    {
        using var client = Create(
            _ => ScriptedHandler.Json("{\"error\":\"LexiconNotFound\"}", HttpStatusCode.BadRequest), out _);
        ILexiconResolver resolver = client.Lexicon;

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.NotFound, ex.Kind);
        Assert.True(((XrpcException)ex.InnerException!).Is(XrpcErrors.LexiconNotFound));
    }

    [Fact]
    public async Task ResolveAsync_ServiceAnswersForAnotherNsid_IsInvalidRecord()
    {
        using var client = Create(_ => ScriptedHandler.Json(ResolvedJson), out _);
        ILexiconResolver resolver = client.Lexicon;

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(
            () => resolver.ResolveAsync(Nsid.Parse("com.example.lexicon.other")));

        Assert.Equal(LexiconResolutionErrorKind.InvalidRecord, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_ServiceFails_IsResolutionFailed()
    {
        using var client = Create(_ => ScriptedHandler.Status(HttpStatusCode.BadGateway), out _);
        ILexiconResolver resolver = client.Lexicon;

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
    }

    private static AtProtoClient Create(Func<HttpRequestMessage, HttpResponseMessage> respond, out ScriptedHandler handler)
    {
        handler = new ScriptedHandler(respond);
        return new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            new HttpClient(handler));
    }
}
