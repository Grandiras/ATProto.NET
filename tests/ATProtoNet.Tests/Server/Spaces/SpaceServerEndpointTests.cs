using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// End-to-end coverage of the space server: the credential exchange over HTTP, and a repo read
/// authenticated with the credential it produced.
/// </summary>
public class SpaceServerEndpointTests : IAsyncLifetime
{
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string MemberDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string StrangerDid = "did:plc:eeeeeeeeeeeeeeeeeeeeeeee";
    private const string BaseUrl = "http://localhost";

    private readonly AtProtoKey _authorityKey = AtProtoCrypto.GenerateP256Key();
    private readonly AtProtoKey _memberKey = AtProtoCrypto.GenerateP256Key();
    private readonly AtProtoKey _strangerKey = AtProtoCrypto.GenerateP256Key();
    private readonly FakeDidDocumentResolver _resolver = new();
    private readonly InMemorySimpleSpaceStore _simpleSpaceStore = new();
    private readonly StubRepoHost _repoHost = new();

    private IHost _host = null!;
    private HttpClient _client = null!;
    private SpaceUri _space = null!;

    public async ValueTask InitializeAsync()
    {
        _resolver
            .PublishAccount(AuthorityDid, _authorityKey, BaseUrl)
            .PublishAccount(MemberDid, _memberKey, BaseUrl)
            .PublishAccount(StrangerDid, _strangerKey, BaseUrl);

        _space = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");
        await _simpleSpaceStore.CreateSpaceAsync(
            new SimpleSpaceRecord(_space, AuthorityDid, new MemberListPolicy(), new OpenAppAccess()));
        await _simpleSpaceStore.AddMemberAsync(_space, MemberDid);

        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ISpaceDidDocumentResolver>(_resolver);
                    services.AddSingleton<ISimpleSpaceStore>(_simpleSpaceStore);
                    services.AddSingleton<ISpaceRepoHost>(_repoHost);

                    services
                        .AddAtProtoSpaces(options =>
                        {
                            options.ServiceDid = AuthorityDid;
                            options.PublicBaseUrl = BaseUrl;
                        })
                        .AddSpaceAuthority<InMemorySpaceAuthorityStore>(_authorityKey)
                        .AddSimpleSpace<InMemorySimpleSpaceStore>()
                        .AddSpaceRepoHost<StubRepoHost>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapXrpcEndpoints());
                });
            })
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _authorityKey.Dispose();
        _memberKey.Dispose();
        _strangerKey.Dispose();
    }

    // ── The credential exchange ───────────────────────────────

    [Fact]
    public async Task GetSpaceCredential_MemberOfTheSpace_ReceivesACredentialBoundToItsOwnKey()
    {
        using var dpop = new TestDPoPKey();

        var response = await ExchangeAsync(MemberDid, _memberKey, dpop);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GetSpaceCredentialResponse>(AtProtoJsonDefaults.Options);
        var credential = SpaceTokens.Parse(SpaceTokenType.Credential, body!.Credential);

        Assert.Equal(_space.Value, credential.Subject);
        Assert.Equal(AuthorityDid, credential.Issuer);
        Assert.Equal(dpop.Thumbprint, credential.ConfirmationThumbprint);
    }

    [Fact]
    public async Task GetSpaceCredential_AccountNotOnTheMemberList_IsRefused()
    {
        using var dpop = new TestDPoPKey();

        var response = await ExchangeAsync(StrangerDid, _strangerKey, dpop);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SpaceErrors.UserNotAuthorized, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_NoDPoPProof_IsRefused()
    {
        var delegation = MintDelegation(MemberDid, _memberKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/xrpc/{SpaceNsids.GetSpaceCredential}")
        {
            Content = JsonContent.Create(
                new GetSpaceCredentialRequest { Space = _space.Value }, options: AtProtoJsonDefaults.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegation);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSpaceCredential_ReusedDelegationToken_IsRefused()
    {
        using var first = new TestDPoPKey();
        using var second = new TestDPoPKey();
        var delegation = MintDelegation(MemberDid, _memberKey);

        using var accepted = await ExchangeAsync(delegation, first);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var replayed = await ExchangeAsync(delegation, second);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        Assert.Equal(SpaceErrors.InvalidDelegationToken, await ReadErrorAsync(replayed));
    }

    [Fact]
    public async Task GetSpaceCredential_SpaceGatedOnAppIdentity_RefusesAnUnattestedRequest()
    {
        // The AppNotAuthorized refusal is what tells a client holding an attestation to retry
        // with one; nothing else advertises that a space gates on app identity.
        var gated = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/gated");
        await _simpleSpaceStore.CreateSpaceAsync(new SimpleSpaceRecord(
            gated,
            AuthorityDid,
            new PublicPolicy(),
            new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] }));

        using var dpop = new TestDPoPKey();
        using var response = await ExchangeAsync(MintDelegation(MemberDid, _memberKey, gated), dpop, gated);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SpaceErrors.AppNotAuthorized, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_SpaceGatedByAnotherAuthority_AnswersSpaceNotFound()
    {
        var elsewhere = SpaceUri.Parse($"at://{MemberDid}/space/com.atmoboards.forum/default");

        using var dpop = new TestDPoPKey();
        using var response = await ExchangeAsync(MintDelegation(MemberDid, _memberKey, elsewhere), dpop, elsewhere);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SpaceErrors.SpaceNotFound, await ReadErrorAsync(response));
    }

    // ── Reading with the credential ───────────────────────────

    [Fact]
    public async Task GetRecord_WithACredentialFromTheExchange_ReturnsTheRecord()
    {
        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, dpop);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<GetSpaceRecordResponse>(AtProtoJsonDefaults.Options);
        Assert.Equal("bafyreiexample", record!.Cid);
    }

    [Fact]
    public async Task GetRecord_MissingRecord_AnswersRecordNotFound()
    {
        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=nothinghere0";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, dpop);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SpaceErrors.RecordNotFound, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetRecord_CredentialPresentedAsABearerToken_IsRefused()
    {
        // A credential is not a bearer token, and the server does not let a caller pretend it is.
        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Headers.TryAddWithoutValidation("DPoP", dpop.Proof("GET", $"{BaseUrl}/xrpc/{SpaceNsids.GetRecord}", credential));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecord_CredentialForAnotherSpaceOnTheSameHost_IsRefused()
    {
        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var other = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other");
        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(other.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, dpop);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRepo_ServesTheCarAsABinaryBody()
    {
        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var url = $"/xrpc/{SpaceNsids.GetRepo}?space={Uri.EscapeDataString(_space.Value)}&repo={MemberDid}";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, dpop);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GetSpaceRepoEndpoint.CarContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StubRepoHost.CarBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ListRecords_BindsBooleanQueryParameters()
    {
        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var url = $"/xrpc/{SpaceNsids.ListRecords}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&excludeValues=true&limit=7";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, dpop);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_repoHost.LastExcludeValues);
        Assert.Equal(7, _repoHost.LastLimit);
    }

    [Fact]
    public async Task ListRepos_ReflectsWhatNotifyWriteReported()
    {
        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();
        ((InMemorySpaceAuthorityStore)store).DeclareSpace(_space);
        await store.RecordWriteAsync(_space, MemberDid, "3l6oveex3ii2l", [1, 2, 3]);

        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        var url = $"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(_space.Value)}";
        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, dpop);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListSpaceReposResponse>(AtProtoJsonDefaults.Options);
        var repo = Assert.Single(body!.Repos);
        Assert.Equal(MemberDid, repo.Did);
        Assert.Equal("3l6oveex3ii2l", repo.Rev);
    }

    [Fact]
    public async Task RegisterNotify_WithACredential_ReturnsAnExpiry()
    {
        var store = (InMemorySpaceAuthorityStore)_host.Services.GetRequiredService<ISpaceAuthorityStore>();
        store.DeclareSpace(_space);

        using var dpop = new TestDPoPKey();
        var credential = await MintCredentialAsync(dpop);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Post,
            $"/xrpc/{SpaceNsids.RegisterNotify}",
            credential,
            dpop,
            new RegisterNotifyRequest
            {
                Space = _space.Value,
                Service = "did:web:syncer.example.com#atproto_space_syncer",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterNotifyResponse>(AtProtoJsonDefaults.Options);
        Assert.True(DateTimeOffset.Parse(body!.ExpiresAt, System.Globalization.CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow);

        var subscribers = await store.ListSubscribersAsync(_space);
        Assert.Single(subscribers);
    }

    [Fact]
    public async Task AnyRepoRead_WithoutAuthentication_IsRefused()
    {
        var url = $"/xrpc/{SpaceNsids.ListRecords}?space={Uri.EscapeDataString(_space.Value)}&repo={MemberDid}";

        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RepoRead_WithAMalformedSpaceUri_IsARequestError()
    {
        var url = $"/xrpc/{SpaceNsids.ListRecords}?space=not-a-space-uri&repo={MemberDid}";

        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", await ReadErrorAsync(response));
    }

    // ── Helpers ───────────────────────────────────────────────

    private string MintDelegation(string userDid, AtProtoKey userKey, SpaceUri? space = null)
    {
        var target = space ?? _space;
        return SpaceTokens.Create(
            SpaceTokenType.Delegation, userDid, target.Value, userKey, audience: target.HostAudience);
    }

    private Task<HttpResponseMessage> ExchangeAsync(string userDid, AtProtoKey userKey, TestDPoPKey dpop) =>
        ExchangeAsync(MintDelegation(userDid, userKey), dpop);

    private async Task<HttpResponseMessage> ExchangeAsync(
        string delegation, TestDPoPKey dpop, SpaceUri? space = null, string? attestation = null)
    {
        var endpoint = $"/xrpc/{SpaceNsids.GetSpaceCredential}";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(
                new GetSpaceCredentialRequest
                {
                    Space = (space ?? _space).Value,
                    ClientAttestation = attestation,
                },
                options: AtProtoJsonDefaults.Options),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegation);
        request.Headers.TryAddWithoutValidation("DPoP", dpop.Proof("POST", BaseUrl + endpoint));

        return await _client.SendAsync(request);
    }

    private async Task<string> MintCredentialAsync(TestDPoPKey dpop)
    {
        using var response = await ExchangeAsync(MemberDid, _memberKey, dpop);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GetSpaceCredentialResponse>(AtProtoJsonDefaults.Options);
        return body!.Credential;
    }

    private async Task<HttpResponseMessage> SendWithCredentialAsync(
        HttpMethod method, string url, string credential, TestDPoPKey dpop, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: AtProtoJsonDefaults.Options);

        // A proof names the path without its query, which is exactly what a real client sends.
        var path = url.Split('?')[0];
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credential);
        request.Headers.TryAddWithoutValidation(
            "DPoP", dpop.Proof(method.Method, BaseUrl + path, credential));

        return await _client.SendAsync(request);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }

    /// <summary>A repo host holding one record, for exercising the endpoint surface.</summary>
    private sealed class StubRepoHost : ISpaceRepoHost
    {
        public static readonly byte[] CarBytes = [0x0a, 0x01, 0x02, 0x03];

        public bool LastExcludeValues { get; private set; }

        public int LastLimit { get; private set; }

        public Task<GetSpaceRecordResponse?> GetRecordAsync(
            SpaceUri space, string repoDid, string collection, string rkey,
            CancellationToken cancellationToken = default)
        {
            if (rkey != "3l6oveex3ii2l")
                return Task.FromResult<GetSpaceRecordResponse?>(null);

            return Task.FromResult<GetSpaceRecordResponse?>(new GetSpaceRecordResponse
            {
                Uri = space.Record(repoDid, collection, rkey).Value,
                Cid = "bafyreiexample",
                Value = JsonDocument.Parse("""{"$type":"com.atmoboards.thread"}""").RootElement,
            });
        }

        public Task<ListSpaceRecordsResponse> ListRecordsAsync(
            SpaceUri space, string repoDid, string? collection, int limit, string? cursor,
            bool reverse, bool excludeValues, CancellationToken cancellationToken = default)
        {
            LastExcludeValues = excludeValues;
            LastLimit = limit;

            return Task.FromResult(new ListSpaceRecordsResponse { Records = [] });
        }

        public Task<SignedSpaceCommit?> GetLatestCommitAsync(
            SpaceUri space, string repoDid, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignedSpaceCommit?>(null);

        public Task<Stream?> GetRepoAsync(
            SpaceUri space, string repoDid, bool excludeValues, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream(CarBytes, writable: false));

        public Task<ListSpaceRepoOpsResponse?> ListRepoOpsAsync(
            SpaceUri space, string repoDid, string? since, int limit, string? cursor,
            bool excludeValues, CancellationToken cancellationToken = default) =>
            Task.FromResult<ListSpaceRepoOpsResponse?>(new ListSpaceRepoOpsResponse { Ops = [] });

        public Task<ListSpaceBlobsResponse> ListBlobsAsync(
            SpaceUri space, string repoDid, string? since, int limit, string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListSpaceBlobsResponse { Cids = [] });

        public Task<SpaceBlobContent?> GetBlobAsync(
            SpaceUri space, string repoDid, string cid, CancellationToken cancellationToken = default) =>
            Task.FromResult<SpaceBlobContent?>(null);
    }
}
