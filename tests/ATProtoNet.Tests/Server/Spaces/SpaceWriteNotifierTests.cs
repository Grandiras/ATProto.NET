using System.Net;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

public class SpaceWriteNotifierTests
{
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SyncerDid = "did:web:syncer.example.com";
    private const string MemberDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SyncerEndpoint = "https://syncer.example.com";

    private static SpaceUri Space => SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");

    private static DidDocument SyncerDocument() => new()
    {
        Id = SyncerDid,
        Service =
        [
            new ServiceEndpoint
            {
                Id = "#atproto_space_syncer",
                Type = "AtprotoSpaceSyncer",
                Endpoint = SyncerEndpoint,
            },
        ],
    };

    private static (SpaceWriteNotifier Notifier, InMemorySpaceAuthorityStore Store, RecordingHandler Handler)
        Create(HttpStatusCode status = HttpStatusCode.OK)
    {
        var store = new InMemorySpaceAuthorityStore();
        var resolver = new FakeDidDocumentResolver().Publish(SyncerDid, SyncerDocument());
        var handler = new RecordingHandler(status);
        var serviceAuth = new ServiceAuthGenerator(AuthorityDid, AtProtoCrypto.GenerateP256Key());

        return (new SpaceWriteNotifier(store, resolver, serviceAuth, new HttpClient(handler)), store, handler);
    }

    [Fact]
    public async Task NotifyWriteAsync_DeliversToTheServiceEndpointTheSubscriberNamed()
    {
        var (notifier, store, handler) = Create();
        await store.RegisterNotifyAsync(
            Space, $"{SyncerDid}#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        var delivered = await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1, 2, 3]);

        Assert.Equal(1, delivered);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{SyncerEndpoint}/xrpc/{SpaceNsids.NotifyWrite}", request.Url);
        Assert.Equal("Bearer", request.AuthorizationScheme);
    }

    [Fact]
    public async Task NotifyWriteAsync_NoSubscribers_SendsNothing()
    {
        var (notifier, _, handler) = Create();

        Assert.Equal(0, await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotifyWriteAsync_LapsedRegistration_IsNotDeliveredTo()
    {
        var (notifier, store, handler) = Create();
        await store.RegisterNotifyAsync(
            Space, $"{SyncerDid}#atproto_space_syncer", DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(0, await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotifyWriteAsync_SubscriberThatRefuses_IsCountedAsUndeliveredRatherThanThrowing()
    {
        // Notifications are best-effort: the syncer's periodic sweep is the correctness
        // guarantee, so a failed delivery must not fail the write that triggered it.
        var (notifier, store, _) = Create(HttpStatusCode.InternalServerError);
        await store.RegisterNotifyAsync(
            Space, $"{SyncerDid}#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(0, await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1]));
    }

    [Fact]
    public async Task NotifyWriteAsync_UnresolvableSubscriber_IsSkippedWithoutThrowing()
    {
        var (notifier, store, _) = Create();
        await store.RegisterNotifyAsync(
            Space, "did:web:nowhere.example.com#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(0, await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1]));
    }

    [Fact]
    public async Task NotifySpaceDeletedAsync_ReachesTheSameSubscribers()
    {
        var (notifier, store, handler) = Create();
        await store.RegisterNotifyAsync(
            Space, $"{SyncerDid}#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1, await notifier.NotifySpaceDeletedAsync(Space));
        Assert.Equal($"{SyncerEndpoint}/xrpc/{SpaceNsids.NotifySpaceDeleted}", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task EnsureAuthoritySubscribedAsync_FirstWriteIntoASharedSpace_RegistersTheAuthority()
    {
        // Without this the authority would never learn who holds data in its spaces, and
        // listRepos would stay empty forever.
        var (notifier, store, _) = Create();

        Assert.True(await notifier.EnsureAuthoritySubscribedAsync(Space, MemberDid));

        var subscriber = Assert.Single(await store.ListSubscribersAsync(Space));
        Assert.Equal(SpaceAuthority.HostAudience(AuthorityDid), subscriber.Service);
    }

    [Fact]
    public async Task EnsureAuthoritySubscribedAsync_SecondWrite_DoesNotRegisterAgain()
    {
        var (notifier, store, _) = Create();

        await notifier.EnsureAuthoritySubscribedAsync(Space, MemberDid);

        Assert.False(await notifier.EnsureAuthoritySubscribedAsync(Space, MemberDid));
        Assert.Single(await store.ListSubscribersAsync(Space));
    }

    [Fact]
    public async Task EnsureAuthoritySubscribedAsync_PersonalDataSpace_RegistersNothing()
    {
        // The authority and the repo host are the same service, so there is nobody to notify.
        var (notifier, store, _) = Create();
        var personal = SpaceUri.Parse($"at://{MemberDid}/space/com.example.bookmarks/self");

        Assert.False(await notifier.EnsureAuthoritySubscribedAsync(personal, MemberDid));
        Assert.Empty(await store.ListSubscribersAsync(personal));
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(string Url, string? AuthorizationScheme)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.Scheme));

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
