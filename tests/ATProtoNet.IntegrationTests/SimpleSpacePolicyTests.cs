using ATProtoNet.Http;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Spaces;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// Who a real authority admits, and who it turns away.
/// </summary>
/// <remarks>
/// <para>The permissioned data protocol deliberately says nothing about how a space decides who
/// may read it; <c>simplespace</c> is the baseline implementation every PDS offers. Its
/// decisions are made server-side and are invisible to a stub — the SDK sends the same exchange
/// either way and only the answer differs.</para>
/// <para>What is worth asserting here is that the SDK reports the refusal faithfully enough for
/// an application to act on: a space it may not read, an app that needs an attestation, and a
/// repo boundary that holds even between accounts on the same host.</para>
/// </remarks>
[Collection("Spaces")]
public class SimpleSpacePolicyTests(SpaceNetworkFixture fixture)
{
    [RequiresSpacesFact]
    public async Task MemberListPolicy_RefusesANonMember()
    {
        var space = await fixture.CreateSpaceAsync("policy-members", members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Outsider);

        var refusal = await Assert.ThrowsAsync<SpaceCredentialException>(
            () => provider.GetCredentialAsync(space));

        // Attributed to the user rather than the app, which is what tells an application to stop
        // asking rather than to retry with an attestation.
        Assert.Equal(SpaceErrors.UserNotAuthorized, refusal.Error);
    }

    [RequiresSpacesFact]
    public async Task PublicPolicy_MintsForANonMember()
    {
        // The control for the refusal above: same non-member, same exchange, different policy.
        var space = await fixture.CreateSpaceAsync("policy-public", new PublicPolicy());

        await using var provider = fixture.CreateProvider(fixture.Outsider);
        var credential = await provider.GetCredentialAsync(space);

        Assert.Equal(space.Value, credential.Token.Subject);
    }

    [RequiresSpacesFact]
    public async Task RemovingAMember_StopsTheNextCredentialRenewal()
    {
        var space = await fixture.CreateSpaceAsync("policy-revoke", members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);
        await provider.GetCredentialAsync(space);

        await fixture.Authority.Client.SimpleSpace.RemoveMemberAsync(space.Value, fixture.Member.Did);

        // Membership is checked when a credential is minted, so revocation takes effect at the
        // next renewal rather than mid-credential. A syncer learns it there.
        var refusal = await Assert.ThrowsAsync<SpaceCredentialException>(
            () => provider.GetCredentialAsync(space, forceRenew: true));

        Assert.Equal(SpaceErrors.UserNotAuthorized, refusal.Error);
    }

    [RequiresSpacesFact]
    public async Task AllowListAppAccess_RefusesAnAppThatPresentsNoAttestation()
    {
        // Policy public so the user passes and the refusal can only be about the app.
        var space = await fixture.CreateSpaceAsync(
            "policy-app",
            new PublicPolicy(),
            new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] });

        await using var provider = fixture.CreateProvider(fixture.Member);

        var refusal = await Assert.ThrowsAsync<SpaceCredentialException>(
            () => provider.GetCredentialAsync(space));

        Assert.Equal(SpaceErrors.AppNotAuthorized, refusal.Error);
    }

    [RequiresSpacesFact]
    public async Task AllowListAppAccess_DrivesTheRetryWithAClientAttestation()
    {
        var space = await fixture.CreateSpaceAsync(
            "policy-app-retry",
            new PublicPolicy(),
            new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] });

        var audiences = new List<string>();

        // Whether a space wants a client attestation is not advertised, so the SDK asks without
        // one and retries only when the authority refuses on app grounds. This asserts that
        // discovery-by-refusal actually fires against a real authority — the attestation itself
        // is junk, so the second refusal is about the attestation rather than its absence.
        await using var provider = fixture.CreateProvider(fixture.Member, (audience, _) =>
        {
            audiences.Add(audience);
            return Task.FromResult("not.a.jwt");
        });

        await Assert.ThrowsAsync<SpaceCredentialException>(() => provider.GetCredentialAsync(space));

        // The attestation is addressed to the authority acting as the space host — the same
        // audience a delegation token carries, and not the PDS the request is sent to.
        Assert.Equal(space.HostAudience, Assert.Single(audiences));
    }

    [RequiresSpacesFact]
    public async Task SpaceRecords_AreNotServedToACoLocatedNonMember()
    {
        var space = await fixture.CreateSpaceAsync("policy-boundary", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Authority, space, "members only", "private");

        // The outsider is on the same PDS as the authority, so the host holds the records it is
        // being asked for and has to refuse them on its own — the membership gate lives in the
        // credential mint, and an unauthorized caller must not be assumed never to get this far.
        var refusal = await Assert.ThrowsAsync<AtProtoHttpException>(
            () => fixture.Outsider.Client.Space.GetRecordAsync(
                space.Value, fixture.Authority.Did, SpaceNetworkFixture.Collection, "private"));

        // Deliberately the same error an absent repo gets: whether an account holds a repo in a
        // space the caller may not read is not the caller's business.
        Assert.Equal(SpaceErrors.RepoNotFound, refusal.ErrorType);
    }

    [RequiresSpacesFact]
    public async Task GetSpaceAsync_ServesTheConfigurationToAMemberHoldingACredential()
    {
        var space = await fixture.CreateSpaceAsync(
            "policy-config",
            new MemberListPolicy(),
            new OpenAppAccess(),
            [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);
        using var host = await provider.CreateReaderAsync(space, fixture.PdsUrl);

        var configuration = await host.SimpleSpace.GetSpaceAsync(space.Value);

        Assert.Equal(space.Value, configuration.Uri);
        Assert.IsType<MemberListPolicy>(configuration.Policy);
        Assert.IsType<OpenAppAccess>(configuration.AppAccess);
    }
}
