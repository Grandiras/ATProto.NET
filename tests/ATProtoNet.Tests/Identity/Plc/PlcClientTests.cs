using System.Net;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Identity.Plc;
using ATProtoNet.Net;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Identity.Plc;

/// <summary>The PLC directory client, against what plc.directory served for atproto.com's DID on 2026-09-25.</summary>
public sealed class PlcClientTests
{
    private const string Route = "plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string DocumentJson = """{"@context":["https://www.w3.org/ns/did/v1"],"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","alsoKnownAs":["at://atproto.com"],"verificationMethod":[{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto","type":"Multikey","controller":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","publicKeyMultibase":"zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w"}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://enoki.us-east.host.bsky.network"}]}""";
    private const string AuditLogJson = """[{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","operation":{"sig":"lza4at_jCtGo_TYgL5PC1ZNP7lhF4DV8H50LWHhvdHcB143x1wEwqZ43xvV36Pws6OOnJLJrkibEUFDFqkhIhg","prev":null,"type":"plc_operation","services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://bsky.social"}},"alsoKnownAs":["at://atprotocol.bsky.social"],"rotationKeys":["did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg","did:key:zQ3shpKnbdPx3g3CmPf5cRVTPe1HtSwVn5ish3wSnDPQCbLJK"],"verificationMethods":{"atproto":"did:key:zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF"}},"cid":"bafyreibfvkh3n6odvdpwj54j4xxdsgnn4zo5utbyf7z7nfbyikhtygzjcq","nullified":false,"createdAt":"2023-04-26T06:19:25.508Z"},{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","operation":{"sig":"lRDLz1RRcauzDos9LZ0Q5bi3YzJXbpgrUpZ51e__tdg89xYgHiWWtnKcrAJanBMkgW0uloD40TYWMVXyZWi4mw","prev":"bafyreibfvkh3n6odvdpwj54j4xxdsgnn4zo5utbyf7z7nfbyikhtygzjcq","type":"plc_operation","services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://bsky.social"}},"alsoKnownAs":["at://atproto.com"],"rotationKeys":["did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg","did:key:zQ3shpKnbdPx3g3CmPf5cRVTPe1HtSwVn5ish3wSnDPQCbLJK"],"verificationMethods":{"atproto":"did:key:zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF"}},"cid":"bafyreihljrd4zlm6egppxzwq52fgzdrh7hv2bkjnbgyjteqasmpjgqmryi","nullified":false,"createdAt":"2023-04-26T17:16:46.827Z"}]""";
    private const string DataJson = """{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","verificationMethods":{"atproto":"did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w"},"rotationKeys":["did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg","did:key:zQ3shpKnbdPx3g3CmPf5cRVTPe1HtSwVn5ish3wSnDPQCbLJK"],"alsoKnownAs":["at://atproto.com"],"services":{"atproto_pds":{"type":"AtprotoPersonalDataServer","endpoint":"https://enoki.us-east.host.bsky.network"}}}""";
    private const string ExportJsonLines = """
        {"did":"did:plc:ragtjsm2j2vknwkz3zp4oxrd","cid":"bafyreieibu2mtgsovktnswo6l7dv4i4ztioutzpsy7wsasmbznzqxkpyje","seq":1,"createdAt":"2022-11-17T00:35:16.391Z","operation":{"sig":"DyaPWDItkJnVkN1izINSW-fdjUzP9BkIKlD7SnzD5axfK_870ZZ-1EYcrQLQtP9VkWcp2cdbyIHprjPfeUs8WQ","prev":null,"type":"create","handle":"paul.bsky.social","service":"https://bsky.social","signingKey":"did:key:zQ3shP5TBe1sQfSttXty15FAEHV1DZgcxRZNxvEWnPfLFwLxJ","recoveryKey":"did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg"},"type":"sequenced_op"}
        {"did":"did:plc:l3rouwludahu3ui3bt66mfvj","cid":"bafyreic64lvfs5ayb5g5cgym7xtbnkpcr6g42reni7n3c7r7i547w7wfoa","seq":2,"createdAt":"2022-11-17T00:39:19.084Z","operation":{"sig":"HIAolK_uyrcg8a6CqBT_taoF0AJ6HAwy70hutFGVM54j20rTe90MH---pm6aoe5CsiIm6khqgbr5_Aj7F3rptA","prev":null,"type":"create","handle":"divy.bsky.social","service":"https://bsky.social","signingKey":"did:key:zQ3shP5TBe1sQfSttXty15FAEHV1DZgcxRZNxvEWnPfLFwLxJ","recoveryKey":"did:key:zQ3shhCGUqDKjStzuDxPkTxN6ujddP4RkEKJJouJGRRkaLGbg"},"type":"sequenced_op"}

        """;

    private static readonly Did TestDid = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");

    private readonly FakeXrpcServer _server = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PlcClient Create(Uri? directory = null, bool allowPrivate = false) => new(new UntrustedHttp(new() { AllowPrivateNetworks = allowPrivate }, handler: _server), directory);

    [Fact]
    [Trait("Spec", "IDN-21")]
    public async Task ResolveAsync_ComposesTheDidOntoTheDirectoryUrl()
    {
        _server.On(Route, DocumentJson);

        var document = await Create().ResolveAsync(TestDid, Ct);

        Assert.Equal(new Uri("https://plc.directory/did:plc:ewvi7nxzyoun6zhxrhs64oiz"), _server.Last.Uri);
        Assert.Equal((TestDid, Handle.Parse("atproto.com")), (document.Id, document.GetHandle()));
    }

    [Fact]
    [Trait("Spec", "IDN-21")]
    public async Task ResolveAsync_DirectoryWithAPathPrefix_KeepsThePrefix()
    {
        _server.On("mirror.example.com/plc/did:plc:ewvi7nxzyoun6zhxrhs64oiz", DocumentJson);

        await Create(new Uri("https://mirror.example.com/plc")).ResolveAsync(TestDid, Ct);

        Assert.Equal(new Uri("https://mirror.example.com/plc/did:plc:ewvi7nxzyoun6zhxrhs64oiz"), _server.Last.Uri);
    }

    [Theory]
    [Trait("Spec", "IDN-21")]
    [InlineData(HttpStatusCode.NotFound, IdentityResolutionError.NotFound)]
    [InlineData(HttpStatusCode.Gone, IdentityResolutionError.Deactivated)]
    [InlineData(HttpStatusCode.ServiceUnavailable, IdentityResolutionError.Unavailable)]
    public async Task ResolveAsync_ErrorStatus_IsReportedByKind(HttpStatusCode status, IdentityResolutionError expected)
    {
        _server.On(Route, status, "{}");

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().ResolveAsync(TestDid, Ct));

        Assert.Equal(expected, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-19")]
    public async Task ResolveAsync_DocumentForAnotherDid_IsRefused()
    {
        _server.On(Route, DocumentJson.Replace("ewvi7nxzyoun6zhxrhs64oiz\",\"alsoKnownAs", "zzzzzzzzzzzzzzzzzzzzzzzz\",\"alsoKnownAs"));

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().ResolveAsync(TestDid, Ct));

        Assert.Equal(IdentityResolutionError.InvalidDocument, ex.Error);
    }

    [Theory]
    [Trait("Spec", "IDN-20")]
    [InlineData("did:web:example.com", IdentityResolutionError.UnsupportedMethod)]
    [InlineData("did:plc:tooshort", IdentityResolutionError.InvalidDid)]
    [InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oiZ", IdentityResolutionError.InvalidDid)]
    public async Task ResolveAsync_RefusedIdentifier_SendsNoRequest(string did, IdentityResolutionError expected)
    {
        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().ResolveAsync(Did.Parse(did), Ct));

        Assert.Equal(expected, ex.Error);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    public async Task GetAuditLogAsync_ParsesEveryEntry()
    {
        _server.On(Route + "/log/audit", AuditLogJson);

        var log = await Create().GetAuditLogAsync(TestDid, Ct);

        Assert.Equal(2, log.Count);
        Assert.Null(log[0].Operation.Operation.Prev);
        Assert.Equal(log[0].Cid, log[1].Operation.Operation.Prev);
        Assert.Equal(log[0].Cid, log[0].Operation.Cid);
        Assert.Equal(TestDid, log[0].Operation.DeriveDid());
        Assert.Equal(["at://atproto.com"], Assert.IsType<PlcUpdate>(log[1].Operation.Operation).AlsoKnownAs);
        Assert.False(log[1].Nullified);
        Assert.Equal(DateTimeOffset.Parse("2023-04-26T17:16:46.827Z"), log[1].CreatedAt);
    }

    [Fact]
    [Trait("Spec", "IDN-54")]
    public async Task GetOperationLogAsync_EachOperationVerifiesAgainstARotationKey()
    {
        _server.On(Route + "/log", $"[{System.Text.Json.JsonDocument.Parse(AuditLogJson).RootElement[0].GetProperty("operation").GetRawText()}]");

        var operation = Assert.Single(await Create().GetOperationLogAsync(TestDid, Ct));

        var update = Assert.IsType<PlcUpdate>(operation.Operation);
        Assert.Contains(update.RotationKeys, operation.Verify);
    }

    [Fact]
    [Trait("Spec", "IDN-47")]
    public async Task GetLastOperationAsync_ReadsTheLatestOperation()
    {
        _server.On(Route + "/log/last", System.Text.Json.JsonDocument.Parse(AuditLogJson).RootElement[1].GetProperty("operation").GetRawText());

        var last = await Create().GetLastOperationAsync(TestDid, Ct);

        Assert.Equal(Cid.Parse("bafyreihljrd4zlm6egppxzwq52fgzdrh7hv2bkjnbgyjteqasmpjgqmryi"), last.Cid);
    }

    [Fact]
    [Trait("Spec", "IDN-47")]
    public async Task GetStateAsync_ReadsTheCurrentState()
    {
        _server.On(Route + "/data", DataJson);

        var state = await Create().GetStateAsync(TestDid, Ct);

        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), state.Services["atproto_pds"].Endpoint);
        Assert.Equal(2, state.RotationKeys.Count);
        Assert.Null(state.Prev);
    }

    [Fact]
    [Trait("Spec", "IDN-21")]
    public async Task GetStateAsync_Tombstoned_IsDeactivated()
    {
        _server.On(Route + "/data", HttpStatusCode.Gone, "{\"message\":\"DID not available\"}");

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().GetStateAsync(TestDid, Ct));

        Assert.Equal(IdentityResolutionError.Deactivated, ex.Error);
    }

    [Theory]
    [Trait("Spec", "IDN-49")]
    [InlineData("""[{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz"}]""")]
    [InlineData("""{}""")]
    public async Task GetAuditLogAsync_Malformed_IsInvalidDocument(string body)
    {
        _server.On(Route + "/log/audit", body);

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().GetAuditLogAsync(TestDid, Ct));

        Assert.Equal(IdentityResolutionError.InvalidDocument, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    public async Task GetAuditLogAsync_UnknownOperationType_IsInvalidDocument()
    {
        _server.On(Route + "/log/audit", AuditLogJson.Replace("\"type\":\"plc_operation\"", "\"type\":\"plc_future\""));

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Create().GetAuditLogAsync(TestDid, Ct));

        Assert.Equal(IdentityResolutionError.InvalidDocument, ex.Error);
    }

    [Theory]
    [Trait("Spec", "IDN-47")]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task IsHealthyAsync_ReflectsTheHealthEndpoint(HttpStatusCode status, bool expected)
    {
        _server.On("plc.directory/_health", status, "{}");

        Assert.Equal(expected, await Create().IsHealthyAsync(Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-47")]
    public async Task IsHealthyAsync_Unreachable_IsFalse()
    {
        _server.On("plc.directory/_health", (_, _) => throw new HttpRequestException("down"));

        Assert.False(await Create().IsHealthyAsync(Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-47")]
    public async Task SubmitAsync_PostsTheOperationToTheDid()
    {
        using var key = SigningKey.Generate(KeyCurve.K256);
        var signed = SignedPlcOperation.Sign(new PlcUpdate { RotationKeys = [key.PublicKey.DidKey], VerificationMethods = new Dictionary<string, DidKey>(), AlsoKnownAs = [], Services = new Dictionary<string, PlcService>() }, key);
        var did = signed.DeriveDid();
        _server.On($"plc.directory/{did}", "{}");

        await Create().SubmitAsync(did, signed, Ct);

        Assert.Equal(HttpMethod.Post, _server.Last.Method);
        Assert.Equal("application/json", _server.Last.ContentType);
        Assert.Equal(signed.ToJson().ToArray(), _server.Last.Body);
    }

    [Fact]
    [Trait("Spec", "IDN-48")]
    public async Task SubmitAsync_Rejected_SurfacesTheDirectorysMessage()
    {
        using var key = SigningKey.Generate(KeyCurve.K256);
        var signed = SignedPlcOperation.Sign(new PlcUpdate { RotationKeys = [key.PublicKey.DidKey], VerificationMethods = new Dictionary<string, DidKey>(), AlsoKnownAs = [], Services = new Dictionary<string, PlcService>() }, key);
        _server.On($"plc.directory/{signed.DeriveDid()}", HttpStatusCode.BadRequest, "{\"message\":\"Invalid signature on op\"}");

        var ex = await Assert.ThrowsAsync<PlcOperationRejectedException>(() => Create().SubmitAsync(signed.DeriveDid(), signed, Ct));

        Assert.Equal("Invalid signature on op", ex.DirectoryMessage);
    }

    [Fact]
    [Trait("Spec", "IDN-47")]
    public async Task ExportAsync_ParsesTheSequencedJsonLines()
    {
        _server.On("plc.directory/export", _ => Responses.Text(ExportJsonLines));

        var page = await Create().ExportAsync(0, 2, Ct);

        Assert.Equal("after=0&count=2", _server.Last.Query);
        Assert.Equal([1L, 2L], page.Select(e => e.Seq));
        var create = Assert.IsType<PlcLegacyCreate>(page[0].Operation.Operation);
        Assert.Equal("paul.bsky.social", create.Handle);
        Assert.Equal(page[0].Did, page[0].Operation.DeriveDid());
    }

    [Theory]
    [Trait("Spec", "IDN-47")]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 1001)]
    public async Task ExportAsync_OutOfRangeArguments_Throw(long after, int count) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Create().ExportAsync(after, count, Ct));

    [Theory]
    [Trait("Spec", "IDN-46")]
    [InlineData("http://plc.directory", false, false)]
    [InlineData("http://localhost:2582", true, true)]
    [InlineData("https://plc.directory/?q=1", false, false)]
    [InlineData("https://plc.example.com:8443/mirror/", false, false)]
    [InlineData("https://plc.example.com:8443/mirror/", true, true)]
    public void Constructor_DirectoryUrl_FollowsThePolicy(string url, bool allowPrivate, bool accepted)
    {
        var create = () => Create(new Uri(url), allowPrivate);
        if (accepted) create();
        else Assert.Throws<ArgumentException>(create);
    }

    [Fact]
    public async Task IdentityResolverFactory_CreateDefault_ResolvesAndVerifiesThroughTheDirectory()
    {
        _server.On(Route, DocumentJson).On("atproto.com/.well-known/atproto-did", _ => Responses.Text(TestDid.Value));
        var resolver = IdentityResolverFactory.CreateDefault(new UntrustedHttp(handler: _server), new() { Dns = new TxtResolver((_, _) => Task.FromResult<IReadOnlyList<string>?>([])) });

        var identity = await resolver.ResolveAsync(TestDid, cancellationToken: Ct);
        await resolver.ResolveAsync(TestDid, cancellationToken: Ct);

        Assert.Equal((Handle.Parse("atproto.com"), true), (identity.Handle, identity.HandleVerified));
        Assert.Single(_server.To(Route));
    }
}
