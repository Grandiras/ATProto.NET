using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// The credential exchange, against a live space host.
/// </summary>
/// <remarks>
/// <para>Unit tests pin the SDK's tokens and signatures against the reference implementation's own
/// outputs. What they cannot check is whether a server accepts them — whether the audience the
/// SDK signs is the one the host expects for the request, whether the delegation token it sends
/// as a bearer grant is honoured as one, whether the key it signs with is the key the issued
/// credential ends up bound to.</para>
/// <para>The refusals matter as much as the acceptance: a credential is whole-space read access,
/// so if the exchange succeeded for the wrong reasons — a replayed token, a signature by
/// someone else's key — the positive tests would still pass.</para>
/// </remarks>
[Collection("Spaces")]
public class SpaceCredentialTests(SpaceNetworkFixture fixture)
{
    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task GetDelegationTokenAsync_MintsATokenAddressedToTheSpaceAuthority()
    {
        var space = await fixture.CreateSpaceAsync("deleg-shape", members: [fixture.Member]);

        var response = await fixture.Member.Client.Space.GetDelegationTokenAsync(space);
        var token = SpaceTokens.Parse(SpaceTokenType.Delegation, response.Token);

        Assert.Equal(fixture.Member.Did, token.Issuer);
        Assert.Equal(space.Value, token.Subject);

        // The audience is the authority acting as the space host, not the PDS the request went to.
        Assert.Equal(space.HostAudience, token.Audience);
        Assert.False(token.IsExpired());
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task GetCredentialAsync_ExchangesADelegationTokenForAKeyBoundCredential()
    {
        var space = await fixture.CreateSpaceAsync("cred-mint", members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);
        var credential = await provider.GetCredentialAsync(space);

        Assert.Equal(space, credential.Space);
        Assert.Equal(space.Value, credential.Token.Subject);
        Assert.Equal(SpaceTokenType.Credential, credential.Token.Type);

        // The whole point of the exchange: the credential is bound to the key that signed the
        // request, so it cannot be replayed by whoever it is presented to.
        Assert.Equal(credential.Key.ToDidKey(), credential.Token.ConfirmationKeyId);
        Assert.False(credential.IsExpired());
        Assert.True(credential.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task GetCredentialAsync_CachesUntilForcedToRenew()
    {
        var space = await fixture.CreateSpaceAsync("cred-cache", members: [fixture.Member]);

        await using var provider = fixture.CreateProvider(fixture.Member);

        var first = await provider.GetCredentialAsync(space);
        var cached = await provider.GetCredentialAsync(space);
        Assert.Same(first, cached);

        // A renewal is a second full exchange — a fresh delegation token and a fresh key — which
        // is what a long-running syncer does every few minutes.
        var renewed = await provider.GetCredentialAsync(space, forceRenew: true);
        Assert.NotSame(first, renewed);
        Assert.NotEqual(first.Raw, renewed.Raw);
        Assert.NotEqual(first.Token.ConfirmationKeyId, renewed.Token.ConfirmationKeyId);
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task CreateReaderForRepoAsync_ReadsAnotherMembersRepo()
    {
        var space = await fixture.CreateSpaceAsync("cred-read", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "members only", rkey: "shared");

        // The authority resolves the member's host from its DID document and reads it with the
        // credential, each request signed for the member whose repo it reads.
        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var reader = await provider.CreateReaderForRepoAsync(space, fixture.Member.Did);

        Assert.Equal(fixture.PdsUrl, reader.HostUrl);

        var record = await reader.Space.GetRecordAsync(
            space, fixture.Member.Did, SpaceNetworkFixture.Collection, RecordKey.Parse("shared"));
        Assert.Equal("members only", record.Value.GetProperty("text").GetString());

        var listed = await reader.Space.ListRecordsAsync(space, fixture.Member.Did);
        Assert.Single(listed.Records);

        // Several requests on one credential, each signed for the same audience.
        var commit = await reader.Space.GetLatestCommitAsync(space, fixture.Member.Did);
        Assert.NotNull(commit.Commit.Rev);
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task GetSpaceCredentialAsync_RefusesAReplayedDelegationToken()
    {
        var space = await fixture.CreateSpaceAsync("deleg-replay", members: [fixture.Member]);

        var delegation = await fixture.Member.Client.Space.GetDelegationTokenAsync(space);

        // A fresh key and signature each time, so the token's own single-use property is what has
        // to refuse the second exchange. A captured token that could be spent twice would mint
        // credentials for anyone who caught it.
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

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task GetSpaceCredentialAsync_RefusesADelegationTokenForAnotherSpace()
    {
        var space = await fixture.CreateSpaceAsync("deleg-sub", members: [fixture.Member]);
        var other = await fixture.CreateSpaceAsync("deleg-sub-other", members: [fixture.Member]);

        var delegation = await fixture.Member.Client.Space.GetDelegationTokenAsync(space);

        using var response = await ExchangeAsync(other, delegation.Token);
        Assert.Equal(SpaceErrors.InvalidDelegationToken, await ErrorOf(response));
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task SpaceCredential_SignedByAnotherKey_IsRefused()
    {
        var space = await fixture.CreateSpaceAsync("cred-rebind", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "not yours to read");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        var credential = await provider.GetCredentialAsync(space);

        // Whoever the credential is presented to could otherwise re-present it elsewhere. The
        // `cnf.kid` is what stops them: a signature by any other key does not verify against it.
        using var attacker = AtProtoCrypto.GenerateP256Key();
        using var response = await GetLatestCommitAsync(space, credential, attacker);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("BadSpaceSignature", await ErrorOf(response));
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task SpaceCredential_SignedForAnotherAudience_IsRefused()
    {
        var space = await fixture.CreateSpaceAsync("cred-aud", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "member repo");

        await using var provider = fixture.CreateProvider(fixture.Authority);
        var credential = await provider.GetCredentialAsync(space);

        // A credential reads every repo in the space, so it is handed to hosts that are not the
        // one that issued it. The signature names the DID it is for; a repo operation signed for
        // anyone but the repo's owner is refused, however well it verifies.
        using var response = await GetLatestCommitAsync(
            space, credential, credential.Key, audience: fixture.Authority.Did);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("BadSpaceAudience", await ErrorOf(response));
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
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

        // The control: the same credential on the same request, presented under its own scheme with
        // a signature, is served — so the refusal is about the scheme and not the credential.
        using var proper = await GetLatestCommitAsync(space, credential, credential.Key, repo: fixture.Authority.Did);
        Assert.Equal(HttpStatusCode.OK, proper.StatusCode);
    }

    [RequiresFact(IntegrationRequirement.Spaces)]
    public async Task GetCredentialAsync_AfterTheSpaceIsDeleted_ReportsSpaceDeleted()
    {
        var space = await fixture.CreateSpaceAsync("cred-deleted", members: [fixture.Member]);
        await fixture.WriteAsync(fixture.Member, space, "about to be unreadable");

        await using var provider = fixture.CreateProvider(fixture.Member);
        await provider.GetCredentialAsync(space);

        await fixture.Authority.Client.SimpleSpace.DeleteSpaceAsync(space);

        // The durable drop signal. A syncer that missed the deletion notification learns here,
        // and can tell it apart from an authority that is merely down.
        var refusal = await Assert.ThrowsAsync<SpaceCredentialException>(
            () => provider.GetCredentialAsync(space, forceRenew: true));

        Assert.Equal(SpaceErrors.SpaceDeleted, refusal.Error);
    }

    /// <summary>
    /// One <c>getSpaceCredential</c> exchange, by hand: a delegation token as a bearer grant, signed
    /// by a fresh key, which is what <see cref="SpaceCredentialProvider"/> does internally. Written
    /// out here so a test can vary one half of it.
    /// </summary>
    private async Task<HttpResponseMessage> ExchangeAsync(SpaceUri space, string delegationToken)
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{fixture.PdsUrl}/xrpc/com.atproto.space.getSpaceCredential")
        {
            Content = JsonContent.Create(new GetSpaceCredentialRequest { Space = space }),
        };

        var authorization = $"Bearer {delegationToken}";
        var signature = SpaceHttpSignature.SignExchange(key, authorization);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("Signature-Input", signature.SignatureInput);
        request.Headers.TryAddWithoutValidation("Signature", signature.Signature);

        return await client.SendAsync(request);
    }

    // Reads a repo's latest commit with the credential, signed by signer for audience (the repo's
    // owner unless the test says otherwise).
    private async Task<HttpResponseMessage> GetLatestCommitAsync(
        SpaceUri space, SpaceCredential credential, AtProtoKey signer, string? repo = null, string? audience = null)
    {
        repo ??= fixture.Member.Did;
        var authorization = $"{SpaceHttpSignature.CredentialScheme} {credential.Raw}";
        var signedFor = Did.Parse(audience ?? repo);
        var signature = SpaceHttpSignature.SignRequest(signer, authorization, signedFor);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestCommitUri(space, repo));
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation(SpaceHttpSignature.AudienceHeader, signedFor.Value);
        request.Headers.TryAddWithoutValidation("Signature-Input", signature.SignatureInput);
        request.Headers.TryAddWithoutValidation("Signature", signature.Signature);

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
