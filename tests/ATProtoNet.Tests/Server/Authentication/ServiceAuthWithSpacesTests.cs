using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Server.Spaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// Service auth next to a space server: the scheme, registered as the only one and so the
/// default, must leave the space endpoints' own authentication alone — including the
/// <c>notifyWrite</c> token it would otherwise verify and spend first when the audiences overlap.
/// </summary>
public sealed class ServiceAuthWithSpacesTests : IAsyncDisposable
{
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string MemberDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly AtProtoKey _authorityKey = AtProtoCrypto.GenerateP256Key();
    private readonly FakeDidDocumentResolver _resolver = new();
    private readonly InMemoryJtiReplayStore _replay = new();
    private readonly InMemorySimpleSpaceStore _spaces = new();
    private readonly ServiceAuthGenerator _member;
    private readonly SpaceUri _space = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");
    private IHost? _host;

    public ServiceAuthWithSpacesTests()
    {
        var memberKey = AtProtoCrypto.GenerateP256Key();
        _resolver
            .PublishAccount(AuthorityDid, _authorityKey, "http://localhost")
            .PublishAccount(MemberDid, memberKey, "http://localhost");
        _member = new ServiceAuthGenerator(Did.Parse(MemberDid), memberKey);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        _member.Dispose();
        _authorityKey.Dispose();
    }

    private async Task<HttpClient> StartAsync(Action<RouteGroupBuilder>? configureGroup = null)
    {
        await _spaces.CreateSpaceAsync(new SimpleSpaceRecord(
            _space, Did.Parse(AuthorityDid), new MemberListPolicy(), new MemberListPolicy(), new OpenAppAccess()));
        await _spaces.PutMemberAsync(_space, Did.Parse(MemberDid), read: true, write: true);

        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IDidResolver>(_resolver);
                    services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, _resolver);
                    services.AddSingleton<IJtiReplayStore>(_replay);
                    services.AddSingleton<ISimpleSpaceStore>(_spaces);

                    services
                        .AddAtProtoSpaces(options =>
                        {
                            options.ServiceDid = Did.Parse(AuthorityDid);
                            options.WarnOnInMemoryStores = false;
                        })
                        .AddSpaceAuthority<InMemorySpaceAuthorityStore>(_authorityKey)
                        .AddSimpleSpace<InMemorySimpleSpaceStore>();

                    // The bare authority DID is what the reference sends notifyWrite to, and what a
                    // service mid-transition accepts too: the two verifiers' audiences overlap.
                    services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add(AuthorityDid));
                    services.AddAuthorization();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        var group = endpoints.MapXrpcEndpoints();
                        configureGroup?.Invoke(group);
                    });
                });
            })
            .StartAsync();

        return _host.GetTestClient();
    }

    private Task<HttpResponseMessage> NotifyWriteAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/xrpc/{SpaceNsids.NotifyWrite}")
        {
            Content = JsonContent.Create(
                new NotifyWriteRequest { Space = _space, Repo = Did.Parse(MemberDid), Rev = Tid.Parse("3l6oveex3ii2l"), Hash = [1, 2, 3] },
                options: AtProtoJsonDefaults.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _member.CreateToken(AuthorityDid, Nsid.Parse(SpaceNsids.NotifyWrite)));

        return client.SendAsync(request);
    }

    [Fact]
    public async Task NotifyWrite_WithTheSchemeAsTheDefault_IsVerifiedOnceByTheSpaceServer()
    {
        var client = await StartAsync();

        using var response = await NotifyWriteAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _replay.Count);
    }

    [Fact]
    public async Task NotifyWrite_UnderAGroupRequiringServiceAuth_StillAnswersToItsOwnCheck()
    {
        var client = await StartAsync(group => group.RequireServiceAuth());

        using var response = await NotifyWriteAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _replay.Count);
    }

    [Fact]
    public async Task CredentialAuthenticatedEndpoint_UnderAGroupRequiringServiceAuth_IsNotChallengedForIt()
    {
        // listRepos takes a DPoP-bound space credential, which a service auth challenge would
        // stand in front of; without one it answers with its own refusal.
        var client = await StartAsync(group => group.RequireServiceAuth());

        using var response = await client.GetAsync($"/xrpc/{SpaceNsids.ListRepos}?space={Uri.EscapeDataString(_space.Value)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SpaceErrors.NotAuthorized, (await XrpcTestHost.ReadErrorAsync(response)).Error);
    }
}
