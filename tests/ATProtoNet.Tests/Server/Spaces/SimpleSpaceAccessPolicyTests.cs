using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

public class SimpleSpaceAccessPolicyTests
{
    private const string Owner = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Member = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Stranger = "did:plc:eeeeeeeeeeeeeeeeeeeeeeee";
    private const string ClientId = "https://app.example.com/client-metadata.json";

    private static SpaceUri Space => SpaceUri.Parse($"at://{Owner}/space/com.atmoboards.forum/default");

    private readonly InMemorySimpleSpaceStore _store = new();
    private readonly StubManagingApp _managingApp = new();

    private SimpleSpaceAccessPolicy CreatePolicy() => new(_store, _managingApp);

    private async Task<SimpleSpaceRecord> SeedAsync(
        SimpleSpaceUserPolicy? policy = null, SimpleSpaceAppAccess? appAccess = null)
    {
        var record = new SimpleSpaceRecord(
            Space, Owner, policy ?? new MemberListPolicy(), appAccess ?? new OpenAppAccess());

        await _store.CreateSpaceAsync(record);
        return record;
    }

    [Fact]
    public async Task EvaluateAsync_UnknownSpace_AnswersSpaceNotFound()
    {
        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.Equal(SpaceAccessOutcome.SpaceNotFound, decision.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_DeletedSpace_AnswersSpaceDeleted()
    {
        // The durable signal that a space is gone: a syncer that missed the deletion notification
        // learns it here, on renewal, and drops its copy. SpaceNotFound would say nothing.
        await SeedAsync();
        await _store.DeleteSpaceAsync(Space);

        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.Equal(SpaceAccessOutcome.SpaceDeleted, decision.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_MemberListPolicy_AdmitsAMemberAndRefusesAStranger()
    {
        await SeedAsync(new MemberListPolicy());
        await _store.AddMemberAsync(Space, Member);
        var policy = CreatePolicy();

        Assert.True((await policy.EvaluateAsync(new SpaceAccessRequest(Space, Member, null))).IsGranted);
        Assert.Equal(
            SpaceAccessOutcome.UserNotAuthorized,
            (await policy.EvaluateAsync(new SpaceAccessRequest(Space, Stranger, null))).Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_RemovedMember_IsRefusedFromThenOn()
    {
        await SeedAsync(new MemberListPolicy());
        await _store.AddMemberAsync(Space, Member);
        await _store.RemoveMemberAsync(Space, Member);

        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.Equal(SpaceAccessOutcome.UserNotAuthorized, decision.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_PublicPolicy_AdmitsAnyone()
    {
        await SeedAsync(new PublicPolicy());

        Assert.True((await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Stranger, null))).IsGranted);
    }

    [Fact]
    public async Task EvaluateAsync_Owner_IsAdmittedWithoutBeingOnTheMemberList()
    {
        await SeedAsync(new MemberListPolicy());

        Assert.True((await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Owner, null))).IsGranted);
    }

    [Fact]
    public async Task EvaluateAsync_AllowListAppAccessWithoutAnAttestation_AnswersAppNotAuthorized()
    {
        await SeedAsync(new PublicPolicy(), new AllowListAppAccess { Allowed = [ClientId] });

        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.Equal(SpaceAccessOutcome.AppNotAuthorized, decision.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_AllowListAppAccessWithAnAllowedClient_IsGranted()
    {
        await SeedAsync(new PublicPolicy(), new AllowListAppAccess { Allowed = [ClientId] });

        Assert.True((await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, ClientId))).IsGranted);
    }

    [Fact]
    public async Task EvaluateAsync_AllowListAppAccessWithAnUnlistedClient_AnswersAppNotAuthorized()
    {
        await SeedAsync(new PublicPolicy(), new AllowListAppAccess { Allowed = [ClientId] });

        var decision = await CreatePolicy().EvaluateAsync(
            new SpaceAccessRequest(Space, Member, "https://other.example.com/client-metadata.json"));

        Assert.Equal(SpaceAccessOutcome.AppNotAuthorized, decision.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_AppPerimeterIsEvaluatedBeforeTheUserPerimeter()
    {
        // An unattested client should be told to attest before any managing-app call is made, so
        // an app that was going to be refused never costs an outbound request.
        await SeedAsync(new ManagingAppPolicy { ManagingApp = "did:web:app.example.com#forum" },
            new AllowListAppAccess { Allowed = [ClientId] });

        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.Equal(SpaceAccessOutcome.AppNotAuthorized, decision.Outcome);
        Assert.Equal(0, _managingApp.Calls);
    }

    [Fact]
    public async Task EvaluateAsync_ManagingAppPolicy_DefersToTheApp()
    {
        await SeedAsync(new ManagingAppPolicy { ManagingApp = "did:web:app.example.com#forum" });
        _managingApp.Authorized = true;

        Assert.True((await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, ClientId))).IsGranted);
        Assert.Equal(1, _managingApp.Calls);
        Assert.Equal(ClientId, _managingApp.LastClientId);
    }

    [Fact]
    public async Task EvaluateAsync_ManagingAppDeclines_AnswersUserNotAuthorized()
    {
        await SeedAsync(new ManagingAppPolicy { ManagingApp = "did:web:app.example.com#forum" });
        _managingApp.Authorized = false;

        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.Equal(SpaceAccessOutcome.UserNotAuthorized, decision.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_UnreachableManagingApp_RefusesRatherThanFailingOpen()
    {
        // Failing open here would turn every outage of the managing app into an open space.
        await SeedAsync(new ManagingAppPolicy { ManagingApp = "did:web:app.example.com#forum" });
        _managingApp.Throw = new HttpRequestException("unreachable");

        var decision = await CreatePolicy().EvaluateAsync(new SpaceAccessRequest(Space, Member, null));

        Assert.False(decision.IsGranted);
        Assert.Equal(SpaceAccessOutcome.NotAuthorized, decision.Outcome);
    }

    private sealed class StubManagingApp : ISimpleSpaceManagingAppClient
    {
        public bool Authorized { get; set; }

        public int Calls { get; private set; }

        public string? LastClientId { get; private set; }

        public Exception? Throw { get; set; }

        public Task<bool> CheckUserAccessAsync(
            string managingApp, SpaceUri space, string userDid, string? clientId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastClientId = clientId;

            return Throw is not null ? Task.FromException<bool>(Throw) : Task.FromResult(Authorized);
        }
    }
}

public class InMemorySpaceReplayStoreTests
{
    [Fact]
    public async Task TryConsumeAsync_FirstUse_Succeeds()
    {
        var store = new InMemorySpaceReplayStore();

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task TryConsumeAsync_SecondUse_Fails()
    {
        var store = new InMemorySpaceReplayStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsNotACollision()
    {
        // Entries are keyed on (issuer, jti, expiry): two issuers picking the same nonce are not
        // the same token, and one must not be able to burn the other's.
        var store = new InMemorySpaceReplayStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.True(await store.TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiredEntries_AreSweptOut()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new InMemorySpaceReplayStore(clock);

        await store.TryConsumeAsync("did:plc:a", "short", clock.GetUtcNow().AddSeconds(30));
        Assert.Equal(1, store.Count);

        // Past both the entry's expiry and the sweep interval.
        clock.Advance(TimeSpan.FromMinutes(2));
        await store.TryConsumeAsync("did:plc:a", "fresh", clock.GetUtcNow().AddMinutes(1));

        Assert.Equal(1, store.Count);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
