using ATProtoNet.Admin;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Spaces;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// An account on the space network, with a client already signed in as it.
/// </summary>
/// <param name="Client">A client authenticated as this account.</param>
/// <param name="Did">The account's DID.</param>
/// <param name="Handle">The account's handle.</param>
public sealed record SpaceActor(AtProtoClient Client, string Did, string Handle)
{
    public override string ToString() => Handle;
}

/// <summary>
/// Provisions the accounts and spaces the permissioned-data tests run against, and tears them
/// down again.
/// </summary>
/// <remarks>
/// <para>Three accounts, because a space is a three-party arrangement and a stub cannot tell
/// them apart: an <see cref="Authority"/> that owns the space and mints credentials, a
/// <see cref="Member"/> whose repo is read across the repo boundary, and an
/// <see cref="Outsider"/> the authority must refuse. Every test creates its own space so the
/// suite is order-independent, mirroring <c>packages/pds/tests/space/</c> upstream.</para>
/// <para>Accounts are provisioned through the admin API and deleted afterwards, so a run leaves
/// the server as it found it.</para>
/// </remarks>
public sealed class SpaceNetworkFixture : IAsyncLifetime
{
    /// <summary>The space type these tests use. Any NSID works; nothing resolves it.</summary>
    public const string SpaceType = "com.atprotonet.test.group";

    /// <summary>The record collection these tests write. Third-party, so the PDS reports <c>unknown</c> validation.</summary>
    public const string Collection = "com.atprotonet.test.spaceRecord";

    /// <summary>A second collection, so index ordering is exercised across more than one.</summary>
    public const string CollectionAlt = "com.atprotonet.test.spaceNote";

    private const string AccountPassword = "correct-horse-battery-staple";

    private readonly List<SpaceActor> _actors = [];
    private readonly List<(SpaceActor Owner, SpaceUri Space)> _spaces = [];
    private PdsAdminClient _admin = null!;
    private string _handleDomain = ".test";

    /// <summary>Owns every space these tests create, and is the authority that mints credentials for it.</summary>
    public SpaceActor Authority { get; private set; } = null!;

    /// <summary>A member of the spaces created with <see cref="CreateSpaceAsync"/>.</summary>
    public SpaceActor Member { get; private set; } = null!;

    /// <summary>Never a member of anything, and the party the refusal paths are asserted against.</summary>
    public SpaceActor Outsider { get; private set; } = null!;

    /// <summary>Resolves DIDs through the test network's own PLC directory.</summary>
    public DidResolver DidResolver { get; private set; } = null!;

    /// <summary>The base URL of the PDS hosting every account here.</summary>
    public string PdsUrl => TestConfig.SpacesPdsUrl.TrimEnd('/');

    public async ValueTask InitializeAsync()
    {
        _admin = new PdsAdminClient(
            new PdsAdminOptions
            {
                Url = TestConfig.SpacesPdsUrl,
                AdminPassword = TestConfig.AdminPassword,
                AllowInsecureHttp = true,
            },
            null,
            null);

        var server = await _admin.DescribeServerAsync();
        _handleDomain = server.AvailableUserDomains[0];

        DidResolver = new DidResolver(new PlcClient(TestConfig.PlcUrl), new DidWebResolver());

        Authority = await CreateActorAsync("authority");
        Member = await CreateActorAsync("member");
        Outsider = await CreateActorAsync("outsider");
    }

    /// <summary>Provisions a fresh account and signs a client in as it.</summary>
    public async Task<SpaceActor> CreateActorAsync(string name)
    {
        var handle = $"{name}-{Guid.NewGuid():N}"[..16].TrimEnd('-') + _handleDomain;

        var account = await _admin.CreateAccountAsync(new CreatePdsAccountRequest
        {
            Handle = handle,
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = AccountPassword,
        });

        var client = new AtProtoClientBuilder()
            .WithInstanceUrl(TestConfig.SpacesPdsUrl)
            .WithAutoRefreshSession(false)
            .Build();

        await client.LoginAsync(handle, AccountPassword);

        var actor = new SpaceActor(client, account.Did, handle);
        _actors.Add(actor);
        return actor;
    }

    /// <summary>
    /// Creates a space owned by <see cref="Authority"/>, with a key derived from the calling test.
    /// </summary>
    /// <param name="skey">The space key. Give each test its own so they stay order-independent.</param>
    /// <param name="policy">How the authority authorizes requesting users. Defaults to a member list.</param>
    /// <param name="appAccess">How the authority authorizes requesting apps. Defaults to open.</param>
    /// <param name="members">Accounts to add to the member list. The owner is never one of them.</param>
    public async Task<SpaceUri> CreateSpaceAsync(
        string skey,
        SimpleSpaceUserPolicy? policy = null,
        SimpleSpaceAppAccess? appAccess = null,
        SpaceActor[]? members = null)
    {
        var created = await Authority.Client.SimpleSpace.CreateSpaceAsync(
            SpaceType, skey, policy, appAccess);

        var space = SpaceUri.Parse(created.Uri);
        _spaces.Add((Authority, space));

        foreach (var member in members ?? [])
            await Authority.Client.SimpleSpace.AddMemberAsync(space.Value, member.Did);

        return space;
    }

    /// <summary>Writes one record to an actor's own repo in a space.</summary>
    public Task<Lexicon.Com.AtProto.Space.SpaceWriteResult> WriteAsync(
        SpaceActor actor,
        SpaceUri space,
        string text,
        string? rkey = null,
        string collection = Collection)
        => actor.Client.Space.CreateRecordAsync(
            space.Value,
            actor.Did,
            collection,
            new Dictionary<string, object>
            {
                ["$type"] = collection,
                ["text"] = text,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            },
            rkey);

    /// <summary>
    /// A credential provider acting as <paramref name="actor"/>, resolving hosts through the test
    /// network's PLC.
    /// </summary>
    /// <param name="actor">The account whose session the delegation token is minted on.</param>
    /// <param name="clientAttestationFactory">
    /// Supplied when the test exercises the retry an <c>AppNotAuthorized</c> refusal triggers.
    /// </param>
    public SpaceCredentialProvider CreateProvider(
        SpaceActor actor,
        Func<string, CancellationToken, Task<string>>? clientAttestationFactory = null)
        => new(
            actor.Client,
            new SpaceCredentialOptions { ClientAttestationFactory = clientAttestationFactory },
            httpClient: null,
            DidResolver);

    /// <summary>
    /// Resolves an account's repo-signing key to a <c>did:key</c>, for verifying its commits.
    /// </summary>
    /// <remarks>
    /// This is <see cref="SpaceSyncer.ResolveSigningKeyAsync(DidResolver)"/> — the production
    /// path — so the tests assert against what an application would actually resolve. The PLC
    /// pinned by the reference dev network publishes the legacy
    /// <c>EcdsaSecp256k1VerificationKey2019</c> form rather than the <c>Multikey</c> that
    /// plc.directory serves; both are read since #98.
    /// </remarks>
    public Task<string> ResolveSigningKeyAsync(string did, CancellationToken cancellationToken = default)
        => SpaceSyncer.ResolveSigningKeyAsync(DidResolver)(did, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        foreach (var (owner, space) in _spaces)
        {
            try
            {
                await owner.Client.SimpleSpace.DeleteSpaceAsync(space.Value);
            }
            catch
            {
                // Best-effort: a test that deleted its own space must not fail the teardown.
            }
        }

        foreach (var actor in _actors)
        {
            try
            {
                await _admin.DeleteAccountAsync(actor.Did);
            }
            catch
            {
                // Ditto — a failed delete must not mask a test result.
            }

            actor.Client.Dispose();
        }

        DidResolver?.Dispose();
        _admin.Dispose();
    }
}

/// <summary>
/// Collection definition so the space suites share one provisioned network.
/// </summary>
[CollectionDefinition("Spaces")]
public class SpaceCollection : ICollectionFixture<SpaceNetworkFixture>
{
}
