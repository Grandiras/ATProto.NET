using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.Server;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// Lexicon resolution over the network: the non-hierarchical <c>_lexicon</c> DNS lookup, and the
/// schema record fetched from the authority's PDS with its proof, against the reference
/// implementation's vector (<c>Lexicon/TestData/lexicon-resolution.json</c>).
/// </summary>
public sealed class LexiconResolverTests
{
    private static readonly JsonElement Vector = LoadVector();
    private static readonly Did Authority = Did.Parse(Vector.GetProperty("did").GetString()!);
    private static readonly DidDocument AuthorityDocument =
        Vector.GetProperty("didDocument").Deserialize<DidDocument>(ATProtoNet.Serialization.AtProtoJsonDefaults.Options)!;

    private static readonly Nsid Post = Nsid.Parse("com.example.lexicon.post");
    private static readonly Nsid AuthBasic = Nsid.Parse("com.example.lexicon.authBasic");

    private const string DnsName = "_lexicon.lexicon.example.com";

    private readonly IDidResolver _didResolver = Substitute.For<IDidResolver>();

    public LexiconResolverTests()
    {
        _didResolver.ResolveAsync(Authority, Arg.Any<CancellationToken>()).Returns(AuthorityDocument);
    }

    // ──────────────────────────────────────────────────────────
    //  The DNS name
    // ──────────────────────────────────────────────────────────

    [Theory]
    // The spec's own examples.
    [InlineData("app.toy.record", "_lexicon.toy.app")]
    [InlineData("edu.university.dept.lab.blogging.getBlogPost", "_lexicon.blogging.lab.dept.university.edu")]
    [InlineData("com.Example.lexicon.post", "_lexicon.lexicon.example.com")]
    public void GetDnsName_ReversesTheAuthorityAndDropsTheName(string nsid, string expected)
    {
        Assert.Equal(expected, LexiconResolver.GetDnsName(Nsid.Parse(nsid)));
    }

    // ──────────────────────────────────────────────────────────
    //  Reference vector
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ReferenceRecordLexicon_ReturnsTheVerifiedSchema()
    {
        using var resolver = Create(Network(), out var handler);

        var resolved = await resolver.ResolveAsync(Post);

        var reference = Reference(Post);
        Assert.True(reference.GetProperty("ok").GetBoolean());
        Assert.Equal(AtUri.Parse(reference.GetProperty("uri").GetString()!), resolved.Uri);
        Assert.Equal(Cid.Parse(reference.GetProperty("cid").GetString()!), resolved.Cid);
        Assert.Equal(Post, resolved.Schema.Id);
        Assert.Equal(1, resolved.Schema.Lexicon);
        Assert.Equal("A test post record.", resolved.Schema.Description);
        Assert.Equal("record", resolved.Schema.MainType);
        Assert.Null(resolved.Schema.GetPermissionSet());

        Assert.Collection(
            handler.Requests,
            dns => Assert.Equal($"https://dns.google/resolve?name={DnsName}&type=TXT", dns.AbsoluteUri),
            pds => Assert.Equal(
                $"https://pds.example.com/xrpc/com.atproto.sync.getRecord?did={Authority}&collection=com.atproto.lexicon.schema&rkey={Post}",
                Uri.UnescapeDataString(pds.AbsoluteUri)));
    }

    [Fact]
    public async Task ResolvePermissionSetAsync_ReferencePermissionSet_ReadsTheSet()
    {
        using var resolver = Create(Network(), out _);

        var set = await resolver.ResolvePermissionSetAsync(AuthBasic);

        Assert.True(Reference(AuthBasic).GetProperty("ok").GetBoolean());
        Assert.Equal("Basic posting", set.Title);
        Assert.Equal("Einfaches Posten", set.LocalizedTitles!["de"]);
        Assert.Equal("Create and delete posts.", set.Detail);
        Assert.Collection(
            set.Permissions,
            repo =>
            {
                Assert.Equal("repo", repo.Resource);
                Assert.Equal(["com.example.lexicon.post"], repo.Collection);
                Assert.Null(repo.Action);
            },
            rpc =>
            {
                Assert.Equal("rpc", rpc.Resource);
                Assert.Equal(["com.example.lexicon.getPosts"], rpc.Lxm);
                Assert.True(rpc.InheritAud);
                Assert.Null(rpc.Aud);
            });
    }

    [Theory]
    [InlineData("com.example.lexicon.mismatch", LexiconResolutionErrorKind.InvalidRecord, "com.example.lexicon.other")]
    [InlineData("com.example.lexicon.badType", LexiconResolutionErrorKind.InvalidRecord, "com.atproto.lexicon.schema")]
    [InlineData("com.example.lexicon.missing", LexiconResolutionErrorKind.NotFound, "no schema record")]
    public async Task ResolveAsync_ReferenceRejects_RejectsToo(string nsid, LexiconResolutionErrorKind kind, string message)
    {
        using var resolver = Create(Network(), out _);
        var parsed = Nsid.Parse(nsid);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(parsed));

        Assert.False(Reference(parsed).GetProperty("ok").GetBoolean());
        Assert.Equal(kind, ex.Kind);
        Assert.Equal(parsed, ex.Nsid);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_AnotherSigningKeyInTheDidDocument_RefusesTheProof()
    {
        using var other = ATProtoNet.Crypto.AtProtoCrypto.GenerateK256Key();
        _didResolver.ResolveAsync(Authority, Arg.Any<CancellationToken>())
            .Returns(DidDocs.Parse(Authority.Value, signingKey: other.ToDidKey()));
        using var resolver = Create(Network(), out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.InvalidRecord, ex.Kind);
        Assert.IsType<ATProtoNet.Repo.RepoVerificationException>(ex.InnerException);
    }

    [Fact]
    public async Task ResolveAsync_KnownAuthority_SkipsDns()
    {
        using var resolver = Create(Network(), out var handler);

        var resolved = await resolver.ResolveAsync(Post, Authority);

        Assert.Equal(Post, resolved.Schema.Id);
        Assert.DoesNotContain(handler.Requests, uri => uri.Host == "dns.google");
    }

    // ──────────────────────────────────────────────────────────
    //  The authority lookup
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAuthorityAsync_NoRecord_FailsWithoutWalkingTheHierarchy()
    {
        // _lexicon.feed.example.app is the only name asked: not _lexicon.example.app, not the
        // name with the NSID's name segment added.
        using var resolver = Create(_ => ScriptedHandler.Json("{\"Status\":3}"), out var handler);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(
            () => resolver.ResolveAsync(Nsid.Parse("app.example.feed.post")));

        Assert.Equal(LexiconResolutionErrorKind.AuthorityNotFound, ex.Kind);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("name=_lexicon.feed.example.app&", request.Query);
    }

    [Fact]
    public async Task ResolveAuthorityAsync_OtherTxtRecordsAround_TakesTheDidOne()
    {
        using var resolver = Create(_ => ScriptedHandler.TxtAnswer("\"v=spf1 -all\"", $"\"did={Authority}\""), out _);

        Assert.Equal(Authority, await resolver.ResolveAuthorityAsync(Post));
    }

    [Fact]
    public async Task ResolveAuthorityAsync_SplitCharacterStrings_AreJoined()
    {
        var did = Authority.Value;
        using var resolver = Create(_ => ScriptedHandler.TxtAnswer($"\"did={did[..12]}\" \"{did[12..]}\""), out _);

        Assert.Equal(Authority, await resolver.ResolveAuthorityAsync(Post));
    }

    [Theory]
    [InlineData("\"did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\"", "\"did=did:plc:bbbbbbbbbbbbbbbbbbbbbbbb\"")]
    // Even two identical records: the reference takes exactly one.
    [InlineData("\"did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\"", "\"did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("\"did=not-a-did\"")]
    public async Task ResolveAuthorityAsync_NotExactlyOneDid_IsNoAuthority(params string[] records)
    {
        using var resolver = Create(_ => ScriptedHandler.TxtAnswer(records), out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAuthorityAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.AuthorityNotFound, ex.Kind);
    }

    [Fact]
    public async Task ResolveAuthorityAsync_DnsEndpointFails_IsResolutionFailed()
    {
        using var resolver = Create(_ => ScriptedHandler.Status(HttpStatusCode.InternalServerError), out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAuthorityAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
    }

    [Fact]
    public async Task ResolveAuthorityAsync_DnsDisabled_IsResolutionFailed()
    {
        using var resolver = Create(Network(), out var handler, new IdentityResolverOptions { DnsOverHttpsUrl = null });

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAuthorityAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
        Assert.Contains(nameof(IdentityResolverOptions.DnsOverHttpsUrl), ex.Message);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ResolveAuthorityAsync_ConfiguredEndpoint_IsTheOneQueried()
    {
        using var resolver = Create(
            Network(), out var handler,
            new IdentityResolverOptions { DnsOverHttpsUrl = new Uri("https://cloudflare-dns.com/dns-query") });

        await resolver.ResolveAuthorityAsync(Post);

        Assert.Equal("cloudflare-dns.com", Assert.Single(handler.Requests).Host);
    }

    // ──────────────────────────────────────────────────────────
    //  The authority's identity and PDS
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_AuthorityDoesNotResolve_IsResolutionFailed()
    {
        _didResolver.ResolveAsync(Authority, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DidResolutionException("gone", DidResolutionErrorKind.NotFound, Authority));
        using var resolver = Create(Network(), out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
        Assert.IsType<DidResolutionException>(ex.InnerException);
    }

    [Theory]
    [InlineData(null, "zQ3shRAqqLZ7SFzXLeAUcuB4uw3LZ59FjACLagnBLW4u92UBc", "PDS")]
    [InlineData("https://pds.example.com", null, "signing key")]
    [InlineData("http://pds.example.com", "zQ3shRAqqLZ7SFzXLeAUcuB4uw3LZ59FjACLagnBLW4u92UBc", "not HTTPS")]
    public async Task ResolveAsync_UnusableDidDocument_IsResolutionFailed(string? pds, string? key, string message)
    {
        _didResolver.ResolveAsync(Authority, Arg.Any<CancellationToken>())
            .Returns(DidDocs.Parse(Authority.Value, pds: pds, signingKey: key is null ? null : "did:key:" + key));
        using var resolver = Create(Network(), out var handler);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
        Assert.Contains(message, ex.Message);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.StartsWith("/xrpc/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_PdsAnswersRecordNotFound_IsNotFound()
    {
        using var resolver = Create(
            request => request.RequestUri!.Host == "dns.google"
                ? ScriptedHandler.TxtAnswer($"\"did={Authority}\"")
                : ScriptedHandler.Json("{\"error\":\"RecordNotFound\",\"message\":\"Could not locate record\"}", HttpStatusCode.BadRequest),
            out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_PdsFails_IsResolutionFailed()
    {
        using var resolver = Create(
            request => request.RequestUri!.Host == "dns.google"
                ? ScriptedHandler.TxtAnswer($"\"did={Authority}\"")
                : ScriptedHandler.Json("{\"error\":\"RepoTakendown\"}", HttpStatusCode.BadRequest),
            out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
        Assert.Contains("RepoTakendown", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_PdsNeverAnswers_TimesOut()
    {
        using var resolver = new LexiconResolver(
            _didResolver,
            new HttpClient(new ScriptedHandler((request, ct) => request.RequestUri!.Host == "dns.google"
                ? Task.FromResult(ScriptedHandler.TxtAnswer($"\"did={Authority}\""))
                : ScriptedHandler.Never(ct))),
            new IdentityResolverOptions { RequestTimeout = TimeSpan.FromMilliseconds(200) });

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_PdsOnAPrivateAddress_IsRefusedByThePolicy()
    {
        // The SDK's own client: the PDS comes from a DID document anyone can publish.
        _didResolver.ResolveAsync(Authority, Arg.Any<CancellationToken>())
            .Returns(DidDocs.Parse(Authority.Value, pds: "https://127.0.0.1:1", signingKey: Vector.GetProperty("signingKey").GetString()));
        using var resolver = new LexiconResolver(_didResolver);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolveAsync(Post, Authority));

        Assert.Equal(LexiconResolutionErrorKind.ResolutionFailed, ex.Kind);
        Assert.Contains("policy", ex.Message);
    }

    // ──────────────────────────────────────────────────────────
    //  Permission sets and include scopes
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolvePermissionSetAsync_NotAPermissionSet_Throws()
    {
        using var resolver = Create(Network(), out _);

        var ex = await Assert.ThrowsAsync<LexiconResolutionException>(() => resolver.ResolvePermissionSetAsync(Post));

        Assert.Equal(LexiconResolutionErrorKind.NotPermissionSet, ex.Kind);
        Assert.Contains("'record'", ex.Message);
    }

    [Fact]
    public async Task ResolveIncludeScopeAsync_ScopeFromAtProtoScopes_ResolvesItsSet()
    {
        using var resolver = Create(Network(), out _);

        var set = await resolver.ResolveIncludeScopeAsync(
            AtProtoScopes.Include(AuthBasic.Value, "did:web:api.example.com#svc_appview"));

        Assert.Equal("Basic posting", set.Title);
    }

    [Theory]
    [InlineData("include:com.example.authFull", "com.example.authFull")]
    [InlineData("include:com.example.authFull?aud=did:web:api.example.com%23svc", "com.example.authFull")]
    [InlineData("include?nsid=com.example.authFull&aud=*", "com.example.authFull")]
    [InlineData("include:com%2Eexample.authFull", "com.example.authFull")]
    public void ParseIncludeScope_BothForms_ReadTheNsid(string scope, string nsid)
    {
        Assert.Equal(Nsid.Parse(nsid), LexiconResolverExtensions.ParseIncludeScope(scope));
    }

    [Theory]
    [InlineData("repo:com.example.post")]
    [InlineData("include:")]
    [InlineData("include:not an nsid")]
    [InlineData("include?aud=*")]
    [InlineData("includes:com.example.authFull")]
    public async Task ResolveIncludeScopeAsync_NotAnIncludeScope_Throws(string scope)
    {
        using var resolver = Create(Network(), out var handler);

        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveIncludeScopeAsync(scope));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void AddAtProtoIdentity_RegistersACachingLexiconResolver()
    {
        using var provider = new ServiceCollection().AddAtProtoIdentity().BuildServiceProvider();

        var resolver = provider.GetRequiredService<ILexiconResolver>();

        Assert.IsType<CachingLexiconResolver>(resolver);
        Assert.Same(resolver, provider.GetRequiredService<ILexiconResolver>());
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private LexiconResolver Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        out ScriptedHandler handler,
        IdentityResolverOptions? options = null)
    {
        handler = new ScriptedHandler(respond);
        return new LexiconResolver(_didResolver, new HttpClient(handler), options);
    }

    /// <summary>The network of the vector: the authority's DNS record and its PDS serving the proofs.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Network() => request =>
    {
        var uri = request.RequestUri!;
        if (uri.Host is "dns.google" or "cloudflare-dns.com")
        {
            return uri.Query.Contains($"name={DnsName}&", StringComparison.Ordinal)
                ? ScriptedHandler.TxtAnswer($"\"did={Authority}\"")
                : ScriptedHandler.Json("{\"Status\":3}");
        }

        if (uri.Host == "pds.example.com" && uri.AbsolutePath == "/xrpc/com.atproto.sync.getRecord")
        {
            var rkey = System.Web.HttpUtility.ParseQueryString(uri.Query)["rkey"]!;
            var car = new ByteArrayContent(Convert.FromBase64String(
                Vector.GetProperty("lexicons").GetProperty(rkey).GetProperty("car").GetString()!));
            car.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ipld.car");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = car };
        }

        return ScriptedHandler.Status(HttpStatusCode.NotFound);
    };

    private static JsonElement Reference(Nsid nsid) =>
        Vector.GetProperty("lexicons").GetProperty(nsid.Value).GetProperty("reference");

    private static JsonElement LoadVector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Lexicon", "TestData", "lexicon-resolution.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }
}
