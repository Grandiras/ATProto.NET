using ATProtoNet.Identity;
using ATProtoNet.Server;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.Server.Spaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server;

/// <summary>
/// <c>AddAtProtoIdentity</c>: one shared cache for every consumer in the container.
/// </summary>
public class IdentityServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAtProtoIdentity_RegistersSharedResolvers()
    {
        using var provider = new ServiceCollection().AddAtProtoIdentity().BuildServiceProvider();

        var didResolver = provider.GetRequiredService<IDidResolver>();

        Assert.IsType<CachingDidResolver>(didResolver);
        Assert.Same(didResolver, provider.GetRequiredService<IDidResolver>());
        Assert.IsType<HandleResolver>(provider.GetRequiredService<IHandleResolver>());
        Assert.IsType<IdentityResolver>(provider.GetRequiredService<IIdentityResolver>());
    }

    [Fact]
    public void AddAtProtoIdentity_FirstCallsOptionsWin()
    {
        var services = new ServiceCollection()
            .AddAtProtoIdentity(o => o.RequestTimeout = TimeSpan.FromSeconds(2))
            .AddAtProtoIdentity(o => o.RequestTimeout = TimeSpan.FromSeconds(9));
        using var provider = services.BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(2), provider.GetRequiredService<IdentityResolverOptions>().RequestTimeout);
    }

    [Fact]
    public async Task AddAtProtoIdentity_WithDistributedCache_BacksTheCacheWithIt()
    {
        var services = new ServiceCollection()
            .AddDistributedMemoryCache()
            .AddAtProtoIdentity(o => o.Cache.UseDistributedCache = true);
        using var provider = services.BuildServiceProvider();

        // A document another instance cached is served without any fetch.
        var did = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
        var shared = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { fetchedAt = DateTimeOffset.UtcNow, document = System.Text.Json.JsonDocument.Parse(Identity.DidDocs.AtprotoDotCom).RootElement });
        await provider.GetRequiredService<IDistributedCache>().SetAsync("atproto:did:" + did.Value, shared);

        var document = await provider.GetRequiredService<IDidResolver>().ResolveAsync(did);

        Assert.Equal(did, document.Id);
    }

    [Fact]
    public void AddAtProtoSpaces_VerifiesThroughItsOwnShortLivedCache()
    {
        var services = new ServiceCollection().AddAtProtoIdentity().AddAtProtoSpaces();
        using var provider = services.BuildServiceProvider();

        var spaceResolver = provider.GetRequiredKeyedService<IDidResolver>(SpaceServerExtensions.DidResolverKey);

        Assert.IsType<CachingDidResolver>(spaceResolver);
        Assert.NotSame(provider.GetRequiredService<IDidResolver>(), spaceResolver);
        Assert.NotNull(provider.GetRequiredService<SpaceDelegationTokenVerifier>());
    }

    [Fact]
    public async Task AddAtProtoSpaces_OutboundClient_ReachesNoPrivateAddress()
    {
        // Every URL the space server's client fetches comes from a DID document or a client ID
        // someone else wrote.
        using var server = new Identity.LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var provider = new ServiceCollection().AddAtProtoSpaces().BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SpaceServerExtensions.HttpClientName);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{server.Port}/"));
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task AddAtProtoSpaces_OutboundClient_FollowsNoRedirects()
    {
        using var target = new Identity.LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var origin = new Identity.LoopbackServer(_ =>
            $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{target.Port}/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var provider = new ServiceCollection()
            .AddAtProtoIdentity(options => options.AllowPrivateNetworks = true)
            .AddAtProtoSpaces()
            .BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SpaceServerExtensions.HttpClientName);

        using var response = await client.GetAsync($"http://127.0.0.1:{origin.Port}/");

        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(0, target.Connections);
    }

    [Fact]
    public void SpaceServerOptions_DidCache_DefaultsToAHardFiveMinuteLifetime()
    {
        // A space server verifies credentials against these keys, and rarely follows #identity:
        // a rotated-out key must never be served past five minutes.
        var cache = new SpaceServerOptions().DidCache;

        Assert.Equal(TimeSpan.FromMinutes(5), cache.StaleAfter);
        Assert.Equal(TimeSpan.FromMinutes(5), cache.ExpireAfter);
        Assert.Equal(TimeSpan.FromHours(1), new DidCacheOptions().StaleAfter);
    }

    [Fact]
    public async Task AddAtProtoSpaces_ACustomResolverUnderTheKey_IsWhatTheVerifiersUse()
    {
        using var userKey = ATProtoNet.Crypto.AtProtoCrypto.GenerateP256Key();
        var userDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
        var space = ATProtoNet.Spaces.SpaceUri.Parse("at://did:plc:bbbbbbbbbbbbbbbbbbbbbbbb/space/com.example.forum/default");
        var fake = new FakeDidDocumentResolver().PublishAccount(userDid, userKey);

        var services = new ServiceCollection()
            .AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, fake)
            .AddAtProtoSpaces();
        using var provider = services.BuildServiceProvider();

        var token = ATProtoNet.Spaces.SpaceTokens.Create(
            ATProtoNet.Spaces.SpaceTokenType.Delegation, userDid, space.Value, userKey, audience: space.HostAudience);
        await provider.GetRequiredService<SpaceDelegationTokenVerifier>().VerifyAsync(token, space);

        Assert.Equal(1, fake.ResolveCount);
    }
}
