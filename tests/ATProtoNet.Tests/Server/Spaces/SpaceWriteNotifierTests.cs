using System.Net;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using NSubstitute;

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

    private const string AuthorityPds = "https://pds.example.com";
    private const string HostDid = "did:web:host.example.com";

    private static (SpaceWriteNotifier Notifier, InMemorySpaceAuthorityStore Store, RecordingHandler Handler)
        Create(HttpStatusCode status = HttpStatusCode.OK)
    {
        var store = new InMemorySpaceAuthorityStore();

        // The authority is an ordinary account: an #atproto_pds entry and no #atproto_space_host.
        var resolver = new FakeDidDocumentResolver()
            .Publish(SyncerDid, SyncerDocument())
            .PublishAccount(AuthorityDid, AtProtoCrypto.GenerateP256Key(), AuthorityPds);
        var handler = new RecordingHandler(status);
        var serviceAuth = new ServiceAuthGenerator(HostDid, AtProtoCrypto.GenerateP256Key());

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

    // ── Delivery to the authority, and forwarding from it ─────

    [Fact]
    public async Task NotifyWriteAsync_AuthorityOnAnOrdinaryPds_IsReachedAtItsPdsAndAddressedByItsBareDid()
    {
        // An authority on an ordinary PDS publishes no #atproto_space_host entry. Without the
        // #atproto_pds fallback its notifications were dropped and its writer set never filled;
        // and the reference authority checks aud against its bare DID.
        var (notifier, store, handler) = Create();
        await notifier.EnsureAuthoritySubscribedAsync(Space, MemberDid);

        Assert.Equal(1, await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1, 2, 3]));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{AuthorityPds}/xrpc/{SpaceNsids.NotifyWrite}", request.Url);
        Assert.Equal(AuthorityDid, Claim(request.Token, "aud"));
        Assert.Equal(HostDid, Claim(request.Token, "iss"));
        Assert.Equal(SpaceNsids.NotifyWrite, Claim(request.Token, "lxm"));
    }

    [Fact]
    public async Task NotifyWriteAsync_FragmentBearingSubscriber_IsAddressedByItsFullServiceIdentifier()
    {
        // A syncer registered as did#fragment verifies aud against exactly that identifier.
        var (notifier, store, handler) = Create();
        await store.RegisterNotifyAsync(
            Space, $"{SyncerDid}#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        await notifier.NotifyWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1]);

        Assert.Equal($"{SyncerDid}#atproto_space_syncer", Claim(Assert.Single(handler.Requests).Token, "aud"));
    }

    [Fact]
    public async Task ForwardWriteAsync_ReachesTheSyncersButNotTheAuthorityItself()
    {
        // On a service that is both repo host and authority the authority's own subscription sits
        // in the same store, and forwarding to it would only loop the notification back.
        var (notifier, store, handler) = Create();
        await notifier.EnsureAuthoritySubscribedAsync(Space, MemberDid);
        await store.RegisterNotifyAsync(
            Space, $"{SyncerDid}#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1, await notifier.ForwardWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1, 2, 3]));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{SyncerEndpoint}/xrpc/{SpaceNsids.NotifyWrite}", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(MemberDid, body.RootElement.GetProperty("repo").GetString());
        Assert.Equal("3l6oveex3ii2l", body.RootElement.GetProperty("rev").GetString());
    }

    [Fact]
    public async Task NotifySpaceDeletedAsync_SkipsTheAuthoritysOwnRegistration()
    {
        // Only the authority deletes a space; telling itself would be a request to its own host.
        var (notifier, _, handler) = Create();
        await notifier.EnsureAuthoritySubscribedAsync(Space, MemberDid);

        Assert.Equal(0, await notifier.NotifySpaceDeletedAsync(Space));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ForwardWriteAsync_StoreFailure_IsSwallowedRatherThanFaulting()
    {
        // Nobody awaits the forward, so a fault would go unobserved; it is logged and counted as
        // nothing delivered instead.
        var store = Substitute.For<ISpaceAuthorityStore>();
        store.ListSubscribersAsync(Arg.Any<SpaceUri>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SpaceNotifySubscriber>>(_ => throw new InvalidOperationException("down"));

        var notifier = new SpaceWriteNotifier(
            store,
            new FakeDidDocumentResolver(),
            new ServiceAuthGenerator(HostDid, AtProtoCrypto.GenerateP256Key()),
            new HttpClient(new RecordingHandler(HttpStatusCode.OK)));

        Assert.Equal(0, await notifier.ForwardWriteAsync(Space, MemberDid, "3l6oveex3ii2l", [1]));
    }

    private static string? Claim(string? jwt, string name)
    {
        var part = jwt!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');

        using var payload = JsonDocument.Parse(Convert.FromBase64String(part));
        return payload.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(string Url, string? AuthorizationScheme, string? Token, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Requests)
            {
                Requests.Add((
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter,
                    body));
            }

            return new HttpResponseMessage(status);
        }
    }
}
