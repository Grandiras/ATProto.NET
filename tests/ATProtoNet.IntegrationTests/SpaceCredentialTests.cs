using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// The credential exchange, against a live space host.
/// </summary>
/// <remarks>
/// <para>Unit tests pin the SDK's tokens and proofs against the reference implementation's own
/// outputs. What they cannot check is whether a server accepts them — whether the <c>htu</c> the
/// SDK puts in a proof is the one the host computes for the request, whether the delegation
/// token it sends as a bearer grant is honoured as one, whether the key it proves possession of
/// is the key the issued credential ends up bound to.</para>
/// <para>The refusals matter as much as the acceptance: a credential is whole-space read access,
/// so if the exchange succeeded for the wrong reasons — a replayed token, a proof signed by
/// someone else's key — the positive tests would still pass.</para>
/// </remarks>
[Collection("Spaces")]
public class SpaceCredentialTests(SpaceNetworkFixture fixture)
{
    [RequiresSpacesFact]
    public async Task GetDelegationTokenAsync_MintsATokenAddressedToTheSpaceAuthority()
    {
        var space = await fixture.CreateSpaceAsync("deleg-shape", members: [fixture.Member]);

        var response = await fixture.Member.Client.Space.GetDelegationTokenAsync(space.Value);
        var token = SpaceTokens.Parse(SpaceTokenType.Delegation, response.Token);

        Assert.Equal(fixture.Member.Did, token.Issuer);
        Assert.Equal(space.Value, token.Subject);

        // The audience is the authority acting as the space host, not the PDS the request went to.
        Assert.Equal(space.HostAudience, token.Audience);
        Assert.False(token.IsExpired());
    }

    [RequiresSpacesFact]
    public async Task GetCredentialAsync_ExchangesADelegationTokenForAKeyBoundCredential()
    {
        var space = await fixture.CreateSpaceAsync("cred-mint", members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);
        var credential = await provider.GetCredentialAsync(space);

        Assert.Equal(space, credential.Space);
        Assert.Equal(space.Value, credential.Token.Subject);
        Assert.Equal(SpaceTokenType.Credential, credential.Token.Type);

        // The whole point of the exchange: the credential is bound to the key that signed the
        // proof, so it cannot be replayed by whoever it is presented to.
        Assert.Equal(credential.Key.KeyThumbprint, credential.Token.ConfirmationThumbprint);
        Assert.False(credential.IsExpired());
        Assert.True(credential.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [RequiresSpacesFact]
    public async Task GetCredentialAsync_CachesUntilForcedToRenew()
    {
        var space = await fixture.CreateSpaceAsync("cred-cache", members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);

        var first = await provider.GetCredentialAsync(space);
        var cached = await provider.GetCredentialAsync(space);
        Assert.Same(first, cached);

        // A renewal is a second full exchange — a fresh delegation token and a fresh key — which
        // is what a long-running syncer does every couple of hours.
        var renewed = await provider.GetCredentialAsync(space, forceRenew: true);
        Assert.NotSame(first, renewed);
        Assert.NotEqual(first.Raw, renewed.Raw);
        Assert.NotEqual(first.Token.ConfirmationThumbprint, renewed.Token.ConfirmationThumbprint);
    }

    [RequiresSpacesFact]
    public async Task CreateReaderForRepoAsync_ReadsAnotherMembersRepo()
    {
        var space = await fixture.CreateSpaceAsync("cred-read", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "members only", rkey: "shared");

        // The authority resolves the member's host from its DID document and reads it with the
        // credential — one proof per request, each naming that request's URL.
        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        Assert.Equal(fixture.PdsUrl, reader.HostUrl);

        var record = await reader.Space.GetRecordAsync(
            space.Value, fixture.Member.Did, SpaceNetworkFixture.Collection, "shared");
        Assert.Equal("members only", record.Value.GetProperty("text").GetString());

        var listed = await reader.Space.ListRecordsAsync(space.Value, fixture.Member.Did);
        Assert.Single(listed.Records);

        // Several requests on one credential, each with its own proof: a replayed proof would
        // fail here, which is the check the SDK's per-request `jti` exists for.
        var commit = await reader.Space.GetLatestCommitAsync(space.Value, fixture.Member.Did);
        Assert.NotEmpty(commit.Commit.Rev);
    }

    [RequiresSpacesFact]
    public async Task GetSpaceCredentialAsync_RefusesAReplayedDelegationToken()
    {
        var space = await fixture.CreateSpaceAsync("deleg-replay", members: [fixture.Member]);

        var delegation = await fixture.Member.Client.Space.GetDelegationTokenAsync(space.Value);

        // A fresh proof each time, so the DPoP replay check is satisfied and the token's own
        // single-use property is what has to refuse the second exchange. A captured token that
        // could be spent twice would mint credentials for anyone who caught it.
        using var first = await ExchangeAsync(space, delegation.Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replayed = await ExchangeAsync(space, delegation.Token);
        Assert.NotEqual(HttpStatusCode.OK, replayed.StatusCode);

        // The protocol requires the refusal, not a particular name for it: the reference
        // implementation reports a spent token as `JwtReplayed` rather than folding it into
        // `InvalidDelegationToken`. Either is a refusal an application handles the same way.
        Assert.Contains(
            await ErrorOf(replayed),
            new[] { "JwtReplayed", SpaceErrors.InvalidDelegationToken });
    }

    [RequiresSpacesFact]
    public async Task GetSpaceCredentialAsync_RefusesADelegationTokenForAnotherSpace()
    {
        var space = await fixture.CreateSpaceAsync("deleg-sub", members: [fixture.Member]);
        var other = await fixture.CreateSpaceAsync("deleg-sub-other", members: [fixture.Member]);

        var delegation = await fixture.Member.Client.Space.GetDelegationTokenAsync(space.Value);

        using var response = await ExchangeAsync(other, delegation.Token);
        Assert.Equal(SpaceErrors.InvalidDelegationToken, await ErrorOf(response));
    }

    [RequiresSpacesFact]
    public async Task SpaceCredential_PresentedWithAProofSignedByAnotherKey_IsRefused()
    {
        var space = await fixture.CreateSpaceAsync("cred-rebind", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "not yours to read");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        var credential = await provider.GetCredentialAsync(space);

        // Whoever the credential is presented to could otherwise re-present it elsewhere. The
        // `cnf` thumbprint is what stops them: a proof from any other key does not match it.
        using var attacker = new DPoPProofGenerator();
        using var response = await GetLatestCommitAsync(space, credential, attacker);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [RequiresSpacesFact]
    public async Task SpaceCredential_PresentedWithAProofForAnotherHost_IsRefused()
    {
        var space = await fixture.CreateSpaceAsync("cred-htu", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "authority repo");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        var credential = await provider.GetCredentialAsync(space);

        // A credential reads every repo in the space, so it is handed to hosts that are not the
        // one that issued it. The proof names the host it is for; presented anywhere else the
        // `htu` no longer matches the request.
        var elsewhere = $"https://other-host.invalid/xrpc/com.atproto.space.getLatestCommit";
        var proof = credential.Key.GenerateProofWithAccessToken("GET", elsewhere, null, credential.Raw);

        using var response = await SendAsync(LatestCommitUri(space, fixture.Member.Did), credential.Raw, proof);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [RequiresSpacesFact]
    public async Task SpaceCredential_PresentedAsABearerToken_IsRefused()
    {
        var space = await fixture.CreateSpaceAsync("cred-bearer", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Authority, space, "bearer bait");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        var credential = await provider.GetCredentialAsync(space);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestCommitUri(space, fixture.Authority.Did));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Raw);

        using var response = await client.SendAsync(request);
        Assert.False(response.IsSuccessStatusCode);

        // The control: the same credential on the same request, presented under the DPoP scheme
        // with a proof, is served — so the refusal is about the scheme and not the credential.
        using var proper = await GetLatestCommitAsync(space, credential, credential.Key, fixture.Authority.Did);
        Assert.Equal(HttpStatusCode.OK, proper.StatusCode);
    }

    [RequiresSpacesFact]
    public async Task GetCredentialAsync_AfterTheSpaceIsDeleted_ReportsSpaceDeleted()
    {
        var space = await fixture.CreateSpaceAsync("cred-deleted", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "about to be unreadable");

        await using var provider = fixture.CreateProvider(fixture.Member);
        await provider.GetCredentialAsync(space);

        await fixture.Authority.Client.SimpleSpace.DeleteSpaceAsync(space.Value);

        // The durable drop signal. A syncer that missed the deletion notification learns here,
        // and can tell it apart from an authority that is merely down.
        var refusal = await Assert.ThrowsAsync<SpaceCredentialException>(
            () => provider.GetCredentialAsync(space, forceRenew: true));

        Assert.Equal(SpaceErrors.SpaceDeleted, refusal.Error);
    }

    /// <summary>
    /// One <c>getSpaceCredential</c> exchange, by hand: a delegation token as a bearer grant and
    /// a fresh proof over a fresh key, which is what <see cref="SpaceCredentialProvider"/> does
    /// internally. Written out here so a test can vary one half of it.
    /// </summary>
    private async Task<HttpResponseMessage> ExchangeAsync(SpaceUri space, string delegationToken)
    {
        var endpoint = $"{fixture.PdsUrl}/xrpc/com.atproto.space.getSpaceCredential";

        using var key = new DPoPProofGenerator();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new GetSpaceCredentialRequest { Space = space.Value }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegationToken);
        request.Headers.TryAddWithoutValidation("DPoP", key.GenerateProof("POST", endpoint));

        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetLatestCommitAsync(
        SpaceUri space, SpaceCredential credential, DPoPProofGenerator signer, string? repo = null)
    {
        var uri = LatestCommitUri(space, repo ?? fixture.Member.Did);
        var proof = signer.GenerateProofWithAccessToken("GET", uri, null, credential.Raw);

        return SendAsync(uri, credential.Raw, proof);
    }

    private static async Task<HttpResponseMessage> SendAsync(string uri, string credential, string proof)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", $"DPoP {credential}");
        request.Headers.TryAddWithoutValidation("DPoP", proof);

        return await client.SendAsync(request);
    }

    private string LatestCommitUri(SpaceUri space, string repo) =>
        $"{fixture.PdsUrl}/xrpc/com.atproto.space.getLatestCommit" +
        $"?space={Uri.EscapeDataString(space.Value)}&repo={repo}";

    private static async Task<string?> ErrorOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<XrpcError>();
        return body?.Error;
    }

    private sealed record XrpcError(string? Error, string? Message);
}
