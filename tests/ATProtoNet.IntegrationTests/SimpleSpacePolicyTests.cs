using ATProtoNet.Http;
using ATProtoNet.Identity;
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
        var space = await fixture.CreateSpaceAsync("policy-public", readPolicy: new PublicPolicy());

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

        await fixture.Authority.Client.SimpleSpace.RemoveMemberAsync(space, fixture.Member.Did);

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
            readPolicy: new PublicPolicy(),
            appAccess: new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] });

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
            readPolicy: new PublicPolicy(),
            appAccess: new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] });

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
        var refusal = await Assert.ThrowsAnyAsync<XrpcException>(
            () => fixture.Outsider.Client.Space.GetRecordAsync(
                space, fixture.Authority.Did, SpaceNetworkFixture.Collection, RecordKey.Parse("private")));

        // Deliberately the same error an absent repo gets: whether an account holds a repo in a
        // space the caller may not read is not the caller's business.
        Assert.Equal(SpaceErrors.RepoNotFound, refusal.Error);
    }

    [RequiresSpacesFact]
    public async Task GetSpaceAsync_ServesTheConfigurationToAMemberHoldingACredential()
    {
        var space = await fixture.CreateSpaceAsync(
            "policy-config",
            readPolicy: new MemberListPolicy(),
            writePolicy: new PublicPolicy(),
            appAccess: new OpenAppAccess(),
            members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);
        using var host = await provider.CreateReaderAsync(space, fixture.PdsUrl);

        var configuration = await host.SimpleSpace.GetSpaceAsync(space);

        Assert.Equal(space.Value, configuration.Uri);
        Assert.IsType<MemberListPolicy>(configuration.ReadPolicy);
        Assert.IsType<PublicPolicy>(configuration.WritePolicy);
        Assert.IsType<OpenAppAccess>(configuration.AppAccess);
    }

    [RequiresSpacesFact]
    public async Task UpdateSpaceAsync_ReplacesOnlyThePolicyItWasGiven()
    {
        var space = await fixture.CreateSpaceAsync("policy-update");

        await fixture.Authority.Client.SimpleSpace.UpdateSpaceAsync(space, writePolicy: new PublicPolicy());

        var configuration = await fixture.Authority.Client.SimpleSpace.GetSpaceAsync(space);
        Assert.IsType<MemberListPolicy>(configuration.ReadPolicy);
        Assert.IsType<PublicPolicy>(configuration.WritePolicy);
        Assert.IsType<OpenAppAccess>(configuration.AppAccess);
    }

    [RequiresSpacesFact]
    public async Task PutMemberAsync_ReplacesBothFlags_AndListMembersReportsThem()
    {
        var space = await fixture.CreateSpaceAsync("policy-put");
        var simpleSpace = fixture.Authority.Client.SimpleSpace;

        await simpleSpace.PutMemberAsync(space, fixture.Member.Did, read: true, write: false);
        await simpleSpace.PutMemberAsync(space, fixture.Member.Did, read: false, write: true);

        // An upsert that replaces both flags, not a second row and not a merge.
        var member = Assert.Single((await simpleSpace.ListMembersAsync(space)).Members);
        Assert.Equal(fixture.Member.Did, member.Did);
        Assert.False(member.Read);
        Assert.True(member.Write);
    }

    [RequiresSpacesFact]
    public async Task ReadOnlyMember_GetsACredentialButStaysOutOfTheWriterSet()
    {
        var space = await fixture.CreateSpaceAsync("policy-read-only");
        await fixture.Authority.Client.SimpleSpace.PutMemberAsync(
            space, fixture.Member.Did, read: true, write: false);

        await using var memberProvider = fixture.CreateProvider(fixture.Member);
        var credential = await memberProvider.GetCredentialAsync(space);
        Assert.Equal(space.Value, credential.Token.Subject);

        // Nothing stops the member writing to its own repo: the write policy decides only whether
        // the authority tracks the write. The authority's own write, which it always admits, is
        // the control that says the member's notification has had its chance to land.
        await fixture.WriteAsync(fixture.Member, space, "read-only member");
        await fixture.WriteAsync(fixture.Authority, space, "authority");

        var writers = await ReadWriterSetUntilAsync(space, fixture.Authority.Did);

        Assert.Contains(fixture.Authority.Did, writers);
        Assert.DoesNotContain(fixture.Member.Did, writers);
    }

    [RequiresSpacesFact]
    public async Task WriteOnlyMember_IsTrackedButRefusedACredential()
    {
        var space = await fixture.CreateSpaceAsync("policy-write-only");
        await fixture.Authority.Client.SimpleSpace.PutMemberAsync(
            space, fixture.Member.Did, read: false, write: true);

        await using var memberProvider = fixture.CreateProvider(fixture.Member);
        var refusal = await Assert.ThrowsAsync<SpaceCredentialException>(
            () => memberProvider.GetCredentialAsync(space));
        Assert.Equal(SpaceErrors.UserNotAuthorized, refusal.Error);

        await fixture.WriteAsync(fixture.Member, space, "write-only member");

        Assert.Contains(fixture.Member.Did, await ReadWriterSetUntilAsync(space, fixture.Member.Did));
    }

    [RequiresSpacesFact]
    public async Task PublicWritePolicy_TracksAWriterWhoWasNeverAMember()
    {
        var space = await fixture.CreateSpaceAsync("policy-public-write", writePolicy: new PublicPolicy());

        await fixture.WriteAsync(fixture.Outsider, space, "never a member");

        Assert.Contains(fixture.Outsider.Did, await ReadWriterSetUntilAsync(space, fixture.Outsider.Did));
    }

    /// <summary>
    /// Reads the writer set as the authority's own syncer would, until it lists
    /// <paramref name="expected"/> or the attempts run out. The set is maintained from write
    /// notifications the writing PDS sends without awaiting, so it is eventually consistent.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadWriterSetUntilAsync(SpaceUri space, string expected)
    {
        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var host = await provider.CreateReaderAsync(space, fixture.PdsUrl);

        IReadOnlyList<string> writers = [];
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var page = await host.Space.ListReposAsync(space);
            writers = page.Repos.Select(repo => repo.Did.Value).ToList();
            if (writers.Contains(expected))
                break;

            await Task.Delay(100);
        }

        return writers;
    }
}
