using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;
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
    private readonly StubDidResolver _resolver = new();
    private readonly InMemorySimpleSpaceStore _simpleSpaceStore = new();
    private readonly StubCallerResolver _caller = new();
    private readonly StubRepoHost _repoHost = new();
    private readonly OutboundHandler _outbound = new();

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
            new SimpleSpaceRecord(_space, Did.Parse(AuthorityDid), new MemberListPolicy(), new MemberListPolicy(), new OpenAppAccess()));
        await _simpleSpaceStore.PutMemberAsync(_space, Did.Parse(MemberDid), read: true, write: true);

        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, _resolver);
                    services.AddSingleton<ISimpleSpaceStore>(_simpleSpaceStore);
                    services.AddSingleton<ISpaceCallerResolver>(_caller);
                    services.AddSingleton<ISpaceRepoHost>(_repoHost);

                    services
                        .AddAtProtoSpaces(options => options.ServiceDid = Did.Parse(AuthorityDid))
                        .AddSpaceAuthority<InMemorySpaceAuthorityStore>(_authorityKey)
                        .AddSimpleSpace<InMemorySimpleSpaceStore>()
                        .AddSpaceRepoHost<StubRepoHost>();

                    // Every outbound call — a forwarded notification included — lands here.
                    services.AddHttpClient(SpaceServerExtensions.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => _outbound);
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
        using var holder = AtProtoCrypto.GenerateP256Key();

        var response = await ExchangeAsync(MemberDid, _memberKey, holder);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GetSpaceCredentialResponse>(AtProtoJsonDefaults.Options);
        var credential = SpaceTokens.Parse(SpaceTokenType.Credential, body!.Credential);

        Assert.Equal(_space.Value, credential.Subject);
        Assert.Equal(AuthorityDid, credential.Issuer);
        Assert.Equal(holder.ToDidKey(), credential.ConfirmationKeyId);
    }

    [Fact]
    public async Task GetSpaceCredential_AccountNotOnTheMemberList_IsRefused()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();

        var response = await ExchangeAsync(StrangerDid, _strangerKey, holder);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SpaceErrors.UserNotAuthorized, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_NoSignature_IsRefused()
    {
        var delegation = MintDelegation(MemberDid, _memberKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/xrpc/{SpaceNsids.GetSpaceCredential}")
        {
            Content = JsonContent.Create(
                new GetSpaceCredentialRequest { Space = _space }, options: AtProtoJsonDefaults.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegation);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_SignedByAKeyOtherThanTheKeyid_IsRefused()
    {
        // The signature has to verify against the key it names: naming another key's did:key, or
        // signing the delegation token with a different key, cannot bind the credential to either.
        using var holder = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();
        var delegation = MintDelegation(MemberDid, _memberKey);
        var authorization = $"Bearer {delegation}";
        var signed = SpaceHttpSignature.SignExchange(holder, authorization);

        using var response = await ExchangeAsync(
            delegation, holder, tamper: request =>
            {
                request.Headers.Remove("Signature-Input");
                request.Headers.TryAddWithoutValidation(
                    "Signature-Input", signed.SignatureInput.Replace(holder.ToDidKey(), other.ToDidKey(), StringComparison.Ordinal));
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_DuplicateAuthorizationHeader_IsRefused()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var delegation = MintDelegation(MemberDid, _memberKey);

        using var response = await ExchangeAsync(
            delegation, holder, tamper: request => request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {delegation}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_ReusedDelegationToken_IsRefused()
    {
        using var first = AtProtoCrypto.GenerateP256Key();
        using var second = AtProtoCrypto.GenerateP256Key();
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
            Did.Parse(AuthorityDid),
            new PublicPolicy(),
            new PublicPolicy(),
            new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] }));

        using var holder = AtProtoCrypto.GenerateP256Key();
        using var response = await ExchangeAsync(MintDelegation(MemberDid, _memberKey, gated), holder, gated);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SpaceErrors.AppNotAuthorized, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetSpaceCredential_SpaceGatedByAnotherAuthority_AnswersSpaceNotFound()
    {
        var elsewhere = SpaceUri.Parse($"at://{MemberDid}/space/com.atmoboards.forum/default");

        using var holder = AtProtoCrypto.GenerateP256Key();
        using var response = await ExchangeAsync(MintDelegation(MemberDid, _memberKey, elsewhere), holder, elsewhere);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SpaceErrors.SpaceNotFound, await ReadErrorAsync(response));
    }

    // ── Reading with the credential ───────────────────────────

    [Fact]
    public async Task GetRecord_WithACredentialFromTheExchange_ReturnsTheRecord()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<GetSpaceRecordResponse>(AtProtoJsonDefaults.Options);
        Assert.Equal("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", record!.Cid);
    }

    [Fact]
    public async Task GetRecord_MissingRecord_AnswersRecordNotFound()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=nothinghere0";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SpaceErrors.RecordNotFound, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task GetRecord_CredentialPresentedAsABearerToken_IsRefused()
    {
        // A credential is not a bearer token, and the server does not let a caller pretend it is.
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecord_CredentialForAnotherSpaceOnTheSameHost_IsRefused()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var other = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other");
        var url = $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(other.Value)}" +
                  $"&repo={MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRepo_ServesTheCarAsABinaryBody()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var url = $"/xrpc/{SpaceNsids.GetRepo}?space={Uri.EscapeDataString(_space.Value)}&repo={MemberDid}";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GetSpaceRepoEndpoint.CarContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StubRepoHost.CarBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ListRecords_BindsBooleanQueryParameters()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var url = $"/xrpc/{SpaceNsids.ListRecords}?space={Uri.EscapeDataString(_space.Value)}" +
                  $"&repo={MemberDid}&excludeValues=true&limit=7";

        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_repoHost.LastExcludeValues);
        Assert.Equal(7, _repoHost.LastLimit);
    }

    [Fact]
    public async Task ListRepos_ReflectsWhatNotifyWriteReported()
    {
        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();
        await store.RecordWriteAsync(_space, Did.Parse(MemberDid), Tid.Parse("3l6oveex3ii2l"), [1, 2, 3]);

        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        var url = $"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(_space.Value)}";
        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListSpaceReposResponse>(AtProtoJsonDefaults.Options);
        var repo = Assert.Single(body!.Repos);
        Assert.Equal(MemberDid, repo.Did);
        Assert.Equal("3l6oveex3ii2l", repo.RepoRev);
    }

    [Fact]
    public async Task ListRepos_WithACursorThatIsNotARevision_IsARequestError()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        // A DID cursor saved before revisions existed must not read as "nothing changed".
        var url = $"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(_space.Value)}&cursor={Uri.EscapeDataString(MemberDid)}";
        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RegisterNotify_WithACredential_ReturnsAnExpiry()
    {
        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();

        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Post,
            $"/xrpc/{SpaceNsids.RegisterNotify}",
            credential,
            holder,
            new RegisterNotifyRequest
            {
                Space = _space,
                Service = "did:web:syncer.example.com#atproto_space_syncer",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegisterNotifyResponse>(AtProtoJsonDefaults.Options);
        Assert.True(body!.ExpiresAt.Value > DateTimeOffset.UtcNow);

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

    // ── The bridge between the two stores ─────────────────────

    [Fact]
    public async Task ListRepos_ForASpaceCreatedThroughSimpleSpace_ReflectsWhatNotifyWriteReported()
    {
        // The space-management store and the authority store hold separate state, and createSpace
        // writes only the first. An authority that did not read space existence from there would
        // refuse notifyWrite and listRepos for every space it hosts with SpaceNotFound, leaving a
        // writer set that can never be populated — and the writer set is the sync boundary.
        var space = await CreateSpaceThroughSimpleSpaceAsync("bridged");

        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder, space);

        using var notified = await NotifyWriteAsync(space, MemberDid, _memberKey, "3l6oveex3ii2l");
        Assert.Equal(HttpStatusCode.OK, notified.StatusCode);

        var url = $"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(space.Value)}";
        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListSpaceReposResponse>(AtProtoJsonDefaults.Options);
        var repo = Assert.Single(body!.Repos);
        Assert.Equal(MemberDid, repo.Did);
        Assert.Equal("3l6oveex3ii2l", repo.RepoRev);
    }

    [Fact]
    public async Task RegisterNotify_ForASpaceCreatedThroughSimpleSpace_IsAccepted()
    {
        var space = await CreateSpaceThroughSimpleSpaceAsync("bridged-notify");

        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder, space);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Post,
            $"/xrpc/{SpaceNsids.RegisterNotify}",
            credential,
            holder,
            new RegisterNotifyRequest
            {
                Space = space,
                Service = "did:web:syncer.example.com#atproto_space_syncer",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();
        Assert.Single(await store.ListSubscribersAsync(space));
    }

    [Fact]
    public async Task ListRepos_AfterTheSpaceIsDeleted_AnswersSpaceDeleted()
    {
        // A credential outlives the space it was issued for, so this is the answer a syncer
        // holding one actually gets — and SpaceDeleted rather than the writer set is what tells
        // it to drop its copy.
        var space = await CreateSpaceThroughSimpleSpaceAsync("bridged-deleted");

        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder, space);

        using var notified = await NotifyWriteAsync(space, MemberDid, _memberKey, "3l6oveex3ii2l");
        Assert.Equal(HttpStatusCode.OK, notified.StatusCode);

        _caller.Did = AuthorityDid;
        using var deleted = await _client.PostAsync(
            $"/xrpc/{SpaceNsids.DeleteSimpleSpace}",
            JsonContent.Create(
                new DeleteSimpleSpaceRequest { Space = space }, options: AtProtoJsonDefaults.Options));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var url = $"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(space.Value)}";
        using var response = await SendWithCredentialAsync(HttpMethod.Get, url, credential, holder);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SpaceErrors.SpaceDeleted, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task NotifyWrite_ForASpaceNoStoreKnows_AnswersSpaceNotFound()
    {
        var unknown = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/never-created");

        using var response = await NotifyWriteAsync(unknown, MemberDid, _memberKey, "3l6oveex3ii2l");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SpaceErrors.SpaceNotFound, await ReadErrorAsync(response));
    }

    // ── notifyWrite: the write policy ─────────────────────────

    [Fact]
    public async Task NotifyWrite_MemberWithWriteAccess_IsRecorded()
    {
        using var response = await NotifyWriteAsync(_space, MemberDid, _memberKey, "3l6oveex3ii2l");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(MemberDid, await WriterDidsAsync(_space));
    }

    [Fact]
    public async Task NotifyWrite_MemberWithoutWriteAccess_IsRefusedAndNotRecorded()
    {
        // The reference authority refuses "a member without write access" the same way.
        await _simpleSpaceStore.PutMemberAsync(_space, Did.Parse(MemberDid), read: true, write: false);

        using var response = await NotifyWriteAsync(_space, MemberDid, _memberKey, "3l6oveex3ii2l");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SpaceErrors.NotAuthorized, await ReadErrorAsync(response));
        Assert.DoesNotContain(MemberDid, await WriterDidsAsync(_space));
    }

    [Fact]
    public async Task NotifyWrite_NonMemberUnderAMemberListWritePolicy_IsRefused()
    {
        using var response = await NotifyWriteAsync(_space, StrangerDid, _strangerKey, "3l6oveex3ii2l");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(StrangerDid, await WriterDidsAsync(_space));
    }

    [Fact]
    public async Task NotifyWrite_NonMemberUnderAPublicWritePolicy_IsRecorded()
    {
        var space = await CreateSpaceThroughSimpleSpaceAsync("public-write", writePolicy: new PublicPolicy());

        using var response = await NotifyWriteAsync(space, StrangerDid, _strangerKey, "3l6oveex3ii2l");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(StrangerDid, await WriterDidsAsync(space));
    }

    [Fact]
    public async Task NotifyWrite_RepoRevThatIsNotATid_IsARequestErrorBeforeAnyAuthCheck()
    {
        // No Authorization header at all: the malformed repoRev is what gets refused.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/xrpc/{SpaceNsids.NotifyWrite}")
        {
            // Written by hand: the request model cannot carry a malformed repoRev.
            Content = new StringContent(
                $$"""{"space":"{{_space}}","repo":"{{MemberDid}}","repoRev":"not-a-tid","hash":{"$bytes":"AQ"} }""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", await ReadErrorAsync(response));
    }

    [Theory]
    [InlineData(MemberDid)]                             // what the reference repo host sends
    [InlineData(MemberDid + "#atproto_space_host")]     // the authority's space host identifier
    [InlineData(AuthorityDid)]                          // this service's own ServiceDid
    public async Task NotifyWrite_OnAMultiTenantHost_AcceptsEachWayOfAddressingTheAuthority(string audience)
    {
        // A space anchored on an account this host serves, while the host's ServiceDid is its own.
        // Accepting only the ServiceDid refused every notification from a reference PDS.
        var space = await CreateSpaceThroughSimpleSpaceAsync(
            "tenant", writePolicy: new PublicPolicy(), owner: MemberDid);

        using var response = await NotifyWriteAsync(space, StrangerDid, _strangerKey, "3l6oveex3ii2l", audience);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NotifyWrite_AddressedToAnotherService_IsRefused()
    {
        var space = await CreateSpaceThroughSimpleSpaceAsync("misaddressed", writePolicy: new PublicPolicy());

        using var response = await NotifyWriteAsync(
            space, StrangerDid, _strangerKey, "3l6oveex3ii2l", audience: "did:web:elsewhere.example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(StrangerDid, await WriterDidsAsync(space));
    }

    [Fact]
    public async Task NotifyWrite_SignedByAServiceAtTheWritersPdsOrigin_IsRefused()
    {
        // The reference authority requires iss to be the writer. A service whose DID document
        // names an endpoint at the writer's PDS origin is not the writer — anyone can publish that.
        const string hostDid = "did:web:host.example.com";
        using var hostKey = AtProtoCrypto.GenerateP256Key();
        _resolver.PublishAccount(hostDid, hostKey, BaseUrl);
        var space = await CreateSpaceThroughSimpleSpaceAsync("host-signed", writePolicy: new PublicPolicy());

        using var response = await NotifyWriteAsync(space, StrangerDid, hostKey, "3l6oveex3ii2l", signer: hostDid);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(StrangerDid, await WriterDidsAsync(space));
    }

    [Fact]
    public async Task NotifyWrite_Accepted_IsForwardedToARegisteredSyncer()
    {
        // Syncers registered with registerNotify hear about remote writes only because the
        // authority forwards them; the notification is addressed to the identifier they registered.
        const string syncer = "did:web:syncer.example.com";
        _resolver.Publish(syncer, new ATProtoNet.Identity.DidDocument
        {
            Id = ATProtoNet.Identity.Did.Parse(syncer),
            Service =
            [
                new ATProtoNet.Identity.DidDocumentService
                {
                    Id = "#atproto_space_syncer",
                    Type = "AtprotoSpaceSyncer",
                    Endpoint = "https://syncer.example.com",
                },
            ],
        });

        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();
        await store.RegisterNotifyAsync(_space, $"{syncer}#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        using var response = await NotifyWriteAsync(_space, MemberDid, _memberKey, "3l6oveex3ii2l");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var (url, token, body) = await _outbound.FirstRequest.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal($"https://syncer.example.com/xrpc/{SpaceNsids.NotifyWrite}", url.ToString());
        var payload = token!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        Assert.Equal($"{syncer}#atproto_space_syncer", claims.RootElement.GetProperty("aud").GetString());

        using var forwarded = JsonDocument.Parse(body);
        Assert.Equal(MemberDid, forwarded.RootElement.GetProperty("repo").GetString());
        Assert.Equal("3l6oveex3ii2l", forwarded.RootElement.GetProperty("repoRev").GetString());
        Assert.False(forwarded.RootElement.TryGetProperty("rev", out _));

        // The authority's own sequence: the first update of the space has a spaceRev and no predecessor.
        var listed = Assert.Single((await store.ListReposAsync(_space, 10, null)).Repos);
        Assert.Equal(listed.SpaceRev.Value, forwarded.RootElement.GetProperty("spaceRev").GetString());
        Assert.False(forwarded.RootElement.TryGetProperty("prevSpaceRev", out _));
    }

    [Fact]
    public async Task NotifyWrite_RepoRevMoreThanFiveMinutesAhead_AnswersFutureRevAndRecordsNothing()
    {
        var micros = (DateTimeOffset.UtcNow + TimeSpan.FromMinutes(10) - DateTimeOffset.UnixEpoch).Ticks / 10;
        var future = Tid.FromInt64(micros << 10).Value;

        using var response = await NotifyWriteAsync(_space, MemberDid, _memberKey, future);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SpaceErrors.FutureRev, await ReadErrorAsync(response));
        Assert.DoesNotContain(MemberDid, await WriterDidsAsync(_space));
    }

    [Fact]
    public async Task NotifyWrite_RepoRevWithinFiveMinutesAhead_IsAccepted()
    {
        var micros = (DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2) - DateTimeOffset.UnixEpoch).Ticks / 10;

        using var response = await NotifyWriteAsync(_space, MemberDid, _memberKey, Tid.FromInt64(micros << 10).Value);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NotifyWrite_StaleRepoRev_IsAcceptedButIgnored()
    {
        // A repo host's retry of an older revision must not resequence the repo.
        using (await NotifyWriteAsync(_space, MemberDid, _memberKey, "3l6oveex3ii2l")) { }
        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();
        var before = Assert.Single((await store.ListReposAsync(_space, 10, null)).Repos);

        using var response = await NotifyWriteAsync(_space, MemberDid, _memberKey, "3l6oveex3ii2a");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = Assert.Single((await store.ListReposAsync(_space, 10, null)).Repos);
        Assert.Equal(before.SpaceRev, after.SpaceRev);
        Assert.Equal(before.RepoRev, after.RepoRev);
    }

    // ── The request signature and its audience ────────────────

    private string GetRecordUrl(string? repo = null) =>
        $"/xrpc/{SpaceNsids.GetRecord}?space={Uri.EscapeDataString(_space.Value)}" +
        $"&repo={repo ?? MemberDid}&collection=com.atmoboards.thread&rkey=3l6oveex3ii2l";

    private string ListReposUrl => $"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(_space.Value)}";

    [Fact]
    public async Task RepoRead_SignedForAnotherRepoOwner_AnswersBadSpaceAudience()
    {
        // The audience is the owner of the repo being read, even on a host that serves several
        // accounts: a signature made for one account's repo does not read another's.
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, GetRecordUrl(), credential, holder, audience: StrangerDid);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceAudience, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task SpaceHostRead_SignedForARepoOwnerInsteadOfTheAuthority_AnswersBadSpaceAudience()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, ListReposUrl, credential, holder, audience: MemberDid);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceAudience, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RepoRead_SignedForTheAuthorityInsteadOfTheRepoOwner_AnswersBadSpaceAudience()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, GetRecordUrl(), credential, holder, audience: AuthorityDid);

        Assert.Equal(SpaceErrors.BadSpaceAudience, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RepoRead_CredentialSignedByAnotherKey_AnswersBadSpaceSignature()
    {
        // A host handed the credential to serve its own repo cannot read the rest of the space with it.
        using var holder = AtProtoCrypto.GenerateP256Key();
        using var thief = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(HttpMethod.Get, GetRecordUrl(), credential, thief);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RepoRead_AudienceHeaderChangedAfterSigning_AnswersBadSpaceSignature()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, GetRecordUrl(), credential, holder, tamper: request =>
            {
                request.Headers.Remove(SpaceHttpSignature.AudienceHeader);
                request.Headers.TryAddWithoutValidation(SpaceHttpSignature.AudienceHeader, StrangerDid);
            });

        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Atproto-Space-Audience")]
    public async Task RepoRead_DuplicateField_AnswersBadSpaceSignature(string header)
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, GetRecordUrl(), credential, holder, tamper: request =>
                request.Headers.TryAddWithoutValidation(header, request.Headers.GetValues(header).First()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Theory]
    [InlineData(SpaceHttpSignature.AudienceHeader)]
    [InlineData("Signature-Input")]
    [InlineData("Signature")]
    public async Task RepoRead_MissingField_AnswersBadSpaceSignature(string header)
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, GetRecordUrl(), credential, holder, tamper: request => request.Headers.Remove(header));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RepoRead_AudienceThatIsNotADid_AnswersBadSpaceSignature()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var response = await SendWithCredentialAsync(
            HttpMethod.Get, GetRecordUrl(), credential, holder, tamper: request =>
            {
                request.Headers.Remove(SpaceHttpSignature.AudienceHeader);
                request.Headers.TryAddWithoutValidation(SpaceHttpSignature.AudienceHeader, "pds.example.com");
            });

        Assert.Equal(SpaceErrors.BadSpaceSignature, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RepoRead_CredentialUnderTheDPoPScheme_IsRefused()
    {
        // The Spaces alpha dropped DPoP: a credential travels under its own scheme.
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetRecordUrl());
        var authorization = $"DPoP {credential}";
        Sign(request, authorization, SpaceHttpSignature.SignRequest(holder, authorization, Did.Parse(MemberDid)), Did.Parse(MemberDid));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.NotAuthorized, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task RepoRead_SchemeIsCaseInsensitive()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetRecordUrl());
        var authorization = $"atproto-space {credential}";
        Sign(request, authorization, SpaceHttpSignature.SignRequest(holder, authorization, Did.Parse(MemberDid)), Did.Parse(MemberDid));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RepoRead_OneSignatureServesEveryRequestForTheSameCredentialAndAudience()
    {
        // No method, URL, nonce or timestamp is signed, so one signature is reusable until the credential expires.
        using var holder = AtProtoCrypto.GenerateP256Key();
        var credential = await MintCredentialAsync(holder);
        var authorization = $"{SpaceHttpSignature.CredentialScheme} {credential}";
        var signature = SpaceHttpSignature.SignRequest(holder, authorization, Did.Parse(MemberDid));

        foreach (var url in new[] { GetRecordUrl(), GetRecordUrl(), $"/xrpc/{SpaceNsids.ListRecords}?space={Uri.EscapeDataString(_space.Value)}&repo={MemberDid}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            Sign(request, authorization, signature, Did.Parse(MemberDid));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task RepoRead_ExpiredCredential_IsRefused()
    {
        using var holder = AtProtoCrypto.GenerateP256Key();
        var now = DateTimeOffset.UtcNow;
        var credential = TestJws.Mint(
            new Dictionary<string, object> { ["typ"] = SpaceTokens.CredentialType, ["alg"] = "ES256", ["kid"] = "#atproto" },
            new Dictionary<string, object>
            {
                ["iss"] = AuthorityDid,
                ["sub"] = _space.Value,
                ["cnf"] = new Dictionary<string, string> { ["kid"] = holder.ToDidKey() },
                ["iat"] = now.AddMinutes(-20).ToUnixTimeSeconds(),
                ["exp"] = now.AddMinutes(-10).ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            input => _authorityKey.Sign(input));

        using var response = await SendWithCredentialAsync(HttpMethod.Get, GetRecordUrl(), credential, holder);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.NotAuthorized, await ReadErrorAsync(response));
    }

    // ── Helpers ───────────────────────────────────────────────

    private string MintDelegation(string userDid, AtProtoKey userKey, SpaceUri? space = null)
    {
        var target = space ?? _space;
        return SpaceTokens.Create(
            SpaceTokenType.Delegation, userDid, target.Value, userKey, audience: target.HostAudience);
    }

    private Task<HttpResponseMessage> ExchangeAsync(string userDid, AtProtoKey userKey, AtProtoKey holder) =>
        ExchangeAsync(MintDelegation(userDid, userKey), holder);

    private async Task<HttpResponseMessage> ExchangeAsync(
        string delegation,
        AtProtoKey holder,
        SpaceUri? space = null,
        string? attestation = null,
        Action<HttpRequestMessage>? tamper = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/xrpc/{SpaceNsids.GetSpaceCredential}")
        {
            Content = JsonContent.Create(
                new GetSpaceCredentialRequest
                {
                    Space = space ?? _space,
                    ClientAttestation = attestation,
                },
                options: AtProtoJsonDefaults.Options),
        };

        var authorization = $"Bearer {delegation}";
        Sign(request, authorization, SpaceHttpSignature.SignExchange(holder, authorization));
        tamper?.Invoke(request);

        return await _client.SendAsync(request);
    }

    private async Task<string> MintCredentialAsync(AtProtoKey holder, SpaceUri? space = null)
    {
        using var response = await ExchangeAsync(MintDelegation(MemberDid, _memberKey, space), holder, space);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GetSpaceCredentialResponse>(AtProtoJsonDefaults.Options);
        return body!.Credential;
    }

    // Sends a request as a credential holder does. The audience is the repo the URL names, or the
    // authority when it names none, unless the test says otherwise.
    private async Task<HttpResponseMessage> SendWithCredentialAsync(
        HttpMethod method,
        string url,
        string credential,
        AtProtoKey holder,
        object? body = null,
        string? audience = null,
        Action<HttpRequestMessage>? tamper = null)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: AtProtoJsonDefaults.Options);

        var signedAudience = Did.Parse(
            audience ?? System.Web.HttpUtility.ParseQueryString(new Uri(BaseUrl + url).Query)["repo"] ?? AuthorityDid);
        var authorization = $"{SpaceHttpSignature.CredentialScheme} {credential}";
        Sign(request, authorization, SpaceHttpSignature.SignRequest(holder, authorization, signedAudience), signedAudience);
        tamper?.Invoke(request);

        return await _client.SendAsync(request);
    }

    private static void Sign(HttpRequestMessage request, string authorization, SpaceSignatureHeaders signature, Did? audience = null)
    {
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (audience is not null)
            request.Headers.TryAddWithoutValidation(SpaceHttpSignature.AudienceHeader, audience.Value);

        request.Headers.TryAddWithoutValidation("Signature-Input", signature.SignatureInput);
        request.Headers.TryAddWithoutValidation("Signature", signature.Signature);
    }

    /// <summary>Creates a space the way an application does: over the owner's own session.</summary>
    private async Task<SpaceUri> CreateSpaceThroughSimpleSpaceAsync(
        string skey, SimpleSpaceUserPolicy? writePolicy = null, string owner = AuthorityDid)
    {
        _caller.Did = owner;

        using var response = await _client.PostAsync(
            $"/xrpc/{SpaceNsids.CreateSimpleSpace}",
            JsonContent.Create(
                new CreateSimpleSpaceRequest
                {
                    SpaceType = Nsid.Parse("com.atmoboards.forum"),
                    Skey = RecordKey.Parse(skey),
                    // Public, so the exchange turns on the space existing rather than on membership.
                    ReadPolicy = new PublicPolicy(),
                    WritePolicy = writePolicy ?? new PublicPolicy(),
                    AppAccess = new OpenAppAccess(),
                },
                options: AtProtoJsonDefaults.Options));

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<CreateSimpleSpaceResponse>(AtProtoJsonDefaults.Options);
        return body!.Uri;
    }

    /// <summary>
    /// Sends a write notification as the account itself, which is the shape a PDS signing with
    /// its account's key produces.
    /// </summary>
    private async Task<HttpResponseMessage> NotifyWriteAsync(
        SpaceUri space, string repoDid, AtProtoKey repoKey, string rev, string audience = AuthorityDid, string? signer = null)
    {
        // Not disposed: that would dispose repoKey, which a test may use for a second notification.
        var generator = new ServiceAuthGenerator(Did.Parse(signer ?? repoDid), repoKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/xrpc/{SpaceNsids.NotifyWrite}")
        {
            Content = JsonContent.Create(
                new NotifyWriteRequest { Space = space, Repo = Did.Parse(repoDid), RepoRev = Tid.Parse(rev), Hash = [1, 2, 3] },
                options: AtProtoJsonDefaults.Options),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", generator.CreateToken(audience, Nsid.Parse(SpaceNsids.NotifyWrite)));

        return await _client.SendAsync(request);
    }

    private async Task<IReadOnlyList<string>> WriterDidsAsync(SpaceUri space)
    {
        var store = _host.Services.GetRequiredService<ISpaceAuthorityStore>();
        var page = await store.ListReposAsync(space, 100, null);
        return page.Repos.Select(repo => repo.Did.Value).ToList();
    }

    /// <summary>
    /// Stands in for every service this host calls out to, and lets a test wait for a delivery
    /// that happens off the request path.
    /// </summary>
    private sealed class OutboundHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<(Uri Url, string? Token, string Body)> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(Uri Url, string? Token, string Body)> FirstRequest => _first.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            _first.TrySetResult((request.RequestUri!, request.Headers.Authorization?.Parameter, body));

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
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
            SpaceUri space, Did repoDid, Nsid collection, RecordKey rkey,
            CancellationToken cancellationToken = default)
        {
            if (rkey.Value != "3l6oveex3ii2l")
                return Task.FromResult<GetSpaceRecordResponse?>(null);

            return Task.FromResult<GetSpaceRecordResponse?>(new GetSpaceRecordResponse
            {
                Uri = space.Record(repoDid, collection, rkey),
                Cid = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"),
                Value = JsonDocument.Parse("""{"$type":"com.atmoboards.thread"}""").RootElement,
            });
        }

        public Task<ListSpaceRecordsResponse> ListRecordsAsync(
            SpaceUri space, Did repoDid, Nsid? collection, bool reverse, bool excludeValues, int limit,
            string? cursor, CancellationToken cancellationToken = default)
        {
            LastExcludeValues = excludeValues;
            LastLimit = limit;

            return Task.FromResult(new ListSpaceRecordsResponse { Records = [] });
        }

        public Task<SignedSpaceCommit?> GetLatestCommitAsync(
            SpaceUri space, Did repoDid, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignedSpaceCommit?>(null);

        public Task<Stream?> GetRepoAsync(
            SpaceUri space, Did repoDid, bool excludeValues, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream(CarBytes, writable: false));

        public Task<ListSpaceRepoOpsResponse?> ListRepoOpsAsync(
            SpaceUri space, Did repoDid, Tid? since, bool excludeValues, int limit, string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ListSpaceRepoOpsResponse?>(new ListSpaceRepoOpsResponse { Ops = [] });

        public Task<ListSpaceBlobsResponse> ListBlobsAsync(
            SpaceUri space, Did repoDid, Tid? since, int limit, string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListSpaceBlobsResponse { Cids = [] });

        public Task<SpaceBlobContent?> GetBlobAsync(
            SpaceUri space, Did repoDid, Cid cid, CancellationToken cancellationToken = default) =>
            Task.FromResult<SpaceBlobContent?>(null);
    }
}
