using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
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
/// Covers the <c>com.atproto.simplespace</c> administration surface, which is authenticated with
/// the owner's own session rather than with a space credential.
/// </summary>
public class SimpleSpaceEndpointTests : IAsyncLifetime
{
    private const string Owner = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Other = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BaseUrl = "http://localhost";

    private readonly AtProtoKey _authorityKey = AtProtoCrypto.GenerateP256Key();
    private readonly InMemorySimpleSpaceStore _store = new();
    private readonly StubCallerResolver _caller = new();

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ISpaceDidDocumentResolver>(
                        new FakeDidDocumentResolver().PublishAccount(Owner, _authorityKey, BaseUrl));
                    services.AddSingleton<ISimpleSpaceStore>(_store);
                    services.AddSingleton<ISpaceCallerResolver>(_caller);

                    services
                        .AddAtProtoSpaces(options =>
                        {
                            options.ServiceDid = Owner;
                            options.PublicBaseUrl = BaseUrl;
                        })
                        .AddSpaceAuthority<InMemorySpaceAuthorityStore>(_authorityKey)
                        .AddSimpleSpace<InMemorySimpleSpaceStore>();
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
    }

    [Fact]
    public async Task CreateSpace_AnchorsTheSpaceOnTheCallersOwnDid()
    {
        _caller.Did = Owner;

        using var response = await PostAsync(SpaceNsids.CreateSimpleSpace, new CreateSimpleSpaceRequest
        {
            Type = "com.atmoboards.forum",
            Skey = "default",
            ReadPolicy = new MemberListPolicy(),
            WritePolicy = new MemberListPolicy(),
            AppAccess = new OpenAppAccess(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateSimpleSpaceResponse>(AtProtoJsonDefaults.Options);
        Assert.Equal($"at://{Owner}/space/com.atmoboards.forum/default", body!.Uri);
    }

    [Fact]
    public async Task CreateSpace_WithoutASkey_GeneratesOne()
    {
        _caller.Did = Owner;

        using var response = await PostAsync(SpaceNsids.CreateSimpleSpace, new CreateSimpleSpaceRequest
        {
            Type = "com.atmoboards.forum",
            ReadPolicy = new PublicPolicy(),
            WritePolicy = new PublicPolicy(),
            AppAccess = new OpenAppAccess(),
        });

        var body = await response.Content.ReadFromJsonAsync<CreateSimpleSpaceResponse>(AtProtoJsonDefaults.Options);
        Assert.NotEmpty(body!.ToSpaceUri().Skey);
    }

    [Fact]
    public async Task CreateSpace_Twice_AnswersSpaceAlreadyExists()
    {
        _caller.Did = Owner;
        var request = new CreateSimpleSpaceRequest
        {
            Type = "com.atmoboards.forum",
            Skey = "default",
            ReadPolicy = new MemberListPolicy(),
            WritePolicy = new MemberListPolicy(),
            AppAccess = new OpenAppAccess(),
        };

        using var first = await PostAsync(SpaceNsids.CreateSimpleSpace, request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostAsync(SpaceNsids.CreateSimpleSpace, request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(SimpleSpaceErrors.SpaceAlreadyExists, await ReadErrorAsync(second));
    }

    [Fact]
    public async Task CreateSpace_WithoutASession_IsRefused()
    {
        _caller.Did = null;

        using var response = await PostAsync(SpaceNsids.CreateSimpleSpace, new CreateSimpleSpaceRequest
        {
            Type = "com.atmoboards.forum",
            ReadPolicy = new PublicPolicy(),
            WritePolicy = new PublicPolicy(),
            AppAccess = new OpenAppAccess(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSpace_StoresTheReadAndWritePoliciesSeparately()
    {
        _caller.Did = Owner;

        using var response = await PostAsync(SpaceNsids.CreateSimpleSpace, new CreateSimpleSpaceRequest
        {
            Type = "com.atmoboards.forum",
            Skey = "split",
            ReadPolicy = new MemberListPolicy(),
            WritePolicy = new PublicPolicy(),
            AppAccess = new OpenAppAccess(),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await _store.GetSpaceAsync(SpaceUri.Parse($"at://{Owner}/space/com.atmoboards.forum/split"));
        Assert.IsType<MemberListPolicy>(stored!.ReadPolicy);
        Assert.IsType<PublicPolicy>(stored.WritePolicy);
    }

    [Theory]
    [InlineData("readPolicy")]
    [InlineData("writePolicy")]
    public async Task CreateSpace_WithoutEitherPolicy_IsARequestError(string omitted)
    {
        // Both are required on the wire. Defaulting a missing one server-side would store a policy
        // the caller never asked for.
        _caller.Did = Owner;

        var fields = new Dictionary<string, string>
        {
            ["type"] = "\"com.atmoboards.forum\"",
            ["readPolicy"] = $$"""{"$type":"{{SimpleSpaceTypes.MemberListPolicy}}"}""",
            ["writePolicy"] = $$"""{"$type":"{{SimpleSpaceTypes.MemberListPolicy}}"}""",
            ["appAccess"] = $$"""{"$type":"{{SimpleSpaceTypes.Open}}"}""",
        };
        fields.Remove(omitted);
        var json = "{" + string.Join(",", fields.Select(field => $"\"{field.Key}\":{field.Value}")) + "}";

        using var response = await PostRawAsync(SpaceNsids.CreateSimpleSpace, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", await ReadErrorAsync(response));
    }

    [Fact]
    public async Task PutMember_ByAnAccountThatIsNotTheOwner_AnswersSpaceNotFound()
    {
        // Answering NotSpaceOwner would confirm the space exists to anyone who guessed its URI.
        var space = await SeedSpaceAsync();
        _caller.Did = Other;

        using var response = await PostAsync(
            SpaceNsids.PutSimpleSpaceMember,
            new PutSimpleSpaceMemberRequest { Space = space.Value, Did = Other, Read = true, Write = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SimpleSpaceErrors.SpaceNotFound, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task PutMember_ReplacesBothFlags_AndListMembersReportsThemOnTheWire()
    {
        var space = await SeedSpaceAsync();
        _caller.Did = Owner;

        using var first = await PostAsync(
            SpaceNsids.PutSimpleSpaceMember,
            new PutSimpleSpaceMemberRequest { Space = space.Value, Did = Other, Read = true, Write = false });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostAsync(
            SpaceNsids.PutSimpleSpaceMember,
            new PutSimpleSpaceMemberRequest { Space = space.Value, Did = Other, Read = false, Write = true });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var listed = await _client.GetAsync(
            $"/xrpc/{SpaceNsids.ListSimpleSpaceMembers}?space={Uri.EscapeDataString(space.Value)}");

        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var member = Assert.Single(document.RootElement.GetProperty("members").EnumerateArray());
        Assert.Equal(Other, member.GetProperty("did").GetString());
        Assert.False(member.GetProperty("read").GetBoolean());
        Assert.True(member.GetProperty("write").GetBoolean());
    }

    [Fact]
    public async Task PutMember_WithoutTheAccessFlags_IsARequestError()
    {
        var space = await SeedSpaceAsync();
        _caller.Did = Owner;

        using var response = await PostRawAsync(
            SpaceNsids.PutSimpleSpaceMember,
            JsonSerializer.Serialize(new { space = space.Value, did = Other }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_IsNoLongerServed()
    {
        // Removed upstream in favour of putMember; a route that answers is one that has to be kept.
        var space = await SeedSpaceAsync();
        _caller.Did = Owner;

        using var response = await PostRawAsync(
            "com.atproto.simplespace.addMember", JsonSerializer.Serialize(new { space = space.Value, did = Other }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_ByAnAccountThatIsNotTheOwner_IsRefused()
    {
        // The member list is never enumerated to the network: listRepos returns writers, not
        // readers, and this is the only place readers are visible at all.
        var space = await SeedSpaceAsync();
        _caller.Did = Other;

        using var response = await _client.GetAsync(
            $"/xrpc/{SpaceNsids.ListSimpleSpaceMembers}?space={Uri.EscapeDataString(space.Value)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSpace_ReplacesOnlyTheSuppliedPolicy()
    {
        var space = await SeedSpaceAsync();
        _caller.Did = Owner;

        using var response = await PostAsync(
            SpaceNsids.UpdateSimpleSpace,
            new UpdateSimpleSpaceRequest { Space = space.Value, WritePolicy = new PublicPolicy() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await _store.GetSpaceAsync(space);
        Assert.IsType<MemberListPolicy>(stored!.ReadPolicy);
        Assert.IsType<PublicPolicy>(stored.WritePolicy);
        Assert.IsType<OpenAppAccess>(stored.AppAccess);
    }

    [Fact]
    public async Task GetSpace_DescribesBothPoliciesOnTheWire()
    {
        var space = await SeedSpaceAsync(writePolicy: new PublicPolicy());
        _caller.Did = Owner;

        using var response = await _client.GetAsync(
            $"/xrpc/{SpaceNsids.GetSimpleSpace}?space={Uri.EscapeDataString(space.Value)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(SimpleSpaceTypes.MemberListPolicy, root.GetProperty("readPolicy").GetProperty("$type").GetString());
        Assert.Equal(SimpleSpaceTypes.PublicPolicy, root.GetProperty("writePolicy").GetProperty("$type").GetString());
        Assert.False(root.TryGetProperty("policy", out _));
    }

    [Fact]
    public async Task DeleteSpace_FlagsTheSpaceRatherThanRemovingIt()
    {
        // A deleted space must keep answering SpaceDeleted on renewal, which is how a syncer that
        // missed the notification learns to drop its copy.
        var space = await SeedSpaceAsync();
        _caller.Did = Owner;

        using var response = await PostAsync(
            SpaceNsids.DeleteSimpleSpace, new DeleteSimpleSpaceRequest { Space = space.Value });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await _store.GetSpaceAsync(space);
        Assert.True(stored!.Deleted);
    }

    [Fact]
    public async Task DeleteSpace_Twice_IsIdempotent()
    {
        var space = await SeedSpaceAsync();
        _caller.Did = Owner;

        using var first = await PostAsync(
            SpaceNsids.DeleteSimpleSpace, new DeleteSimpleSpaceRequest { Space = space.Value });
        using var second = await PostAsync(
            SpaceNsids.DeleteSimpleSpace, new DeleteSimpleSpaceRequest { Space = space.Value });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    private async Task<SpaceUri> SeedSpaceAsync(SimpleSpaceUserPolicy? writePolicy = null)
    {
        var space = SpaceUri.Parse($"at://{Owner}/space/com.atmoboards.forum/seeded");
        await _store.CreateSpaceAsync(new SimpleSpaceRecord(
            space, Owner, new MemberListPolicy(), writePolicy ?? new MemberListPolicy(), new OpenAppAccess()));

        return space;
    }

    private Task<HttpResponseMessage> PostAsync<TBody>(string nsid, TBody body) =>
        _client.PostAsync(
            $"/xrpc/{nsid}", JsonContent.Create(body, options: AtProtoJsonDefaults.Options));

    private Task<HttpResponseMessage> PostRawAsync(string nsid, string json) =>
        _client.PostAsync(
            $"/xrpc/{nsid}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}
