using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The <c>kid</c> a space token may carry, as the reference implementation's
/// <c>resolveSpaceKey</c> reads it: required, one of <c>#atproto</c> / <c>#atproto_space</c>
/// (with or without the <c>#</c>), and resolved to exactly that entry with no fallback. A
/// delegation token, which proposal 0016 says MUST name <c>#atproto</c>, may name nothing else.
/// </summary>
public class SpaceTokenKeyIdTests
{
    private static readonly Did UserDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private const string Url = "https://pds.example.com/xrpc/com.atproto.space.getRecord";

    private static SpaceUri Space => SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");

    // ── Delegation tokens ───────────────────────────────────────

    [Theory]
    [InlineData("#atproto")]
    [InlineData("atproto")]
    public async Task Delegation_KidNamingTheAccountKey_IsAccepted(string kid)
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var verifier = DelegationVerifier(userKey);

        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid.Value, Space.Value, userKey, audience: Space.HostAudience, keyId: kid);

        var verified = await verifier.VerifyAsync(jwt, Space);

        Assert.Equal(UserDid, verified.UserDid);
    }

    [Theory]
    [InlineData("#atproto_space")]                              // an authority's key, not a user's
    [InlineData("#atproto_label")]
    [InlineData("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa#atproto")]     // DID-qualified
    public async Task Delegation_KidNamingAnotherKey_IsRejected(string kid)
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var verifier = DelegationVerifier(userKey);

        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid.Value, Space.Value, userKey, audience: Space.HostAudience, keyId: kid);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, Space));

        Assert.Equal(SpaceErrors.InvalidDelegationToken, ex.Error);
        Assert.Contains("kid", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delegation_WithoutAKid_IsRejected()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var verifier = DelegationVerifier(userKey);
        var now = DateTimeOffset.UtcNow;

        var jwt = TestJws.Mint(
            new Dictionary<string, object> { ["typ"] = SpaceTokens.DelegationType, ["alg"] = "ES256" },
            new Dictionary<string, object>
            {
                ["iss"] = UserDid.Value,
                ["sub"] = Space.Value,
                ["aud"] = Space.HostAudience,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddSeconds(60).ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            input => userKey.Sign(input));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, Space));

        Assert.Contains("kid", ex.Message, StringComparison.Ordinal);
    }

    // ── Space credentials ───────────────────────────────────────

    [Fact]
    public async Task Credential_WithoutAKid_IsRejected()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CredentialVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));
        var now = DateTimeOffset.UtcNow;

        var credential = TestJws.Mint(
            new Dictionary<string, object> { ["typ"] = SpaceTokens.CredentialType, ["alg"] = "ES256" },
            new Dictionary<string, object>
            {
                ["iss"] = AuthorityDid.Value,
                ["sub"] = Space.Value,
                ["cnf"] = new Dictionary<string, string> { ["jkt"] = dpop.Thumbprint },
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddHours(2).ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            input => authorityKey.Sign(input));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, Space));

        Assert.Contains("kid", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#atproto_label")]
    [InlineData("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb#atproto")]
    public async Task Credential_KidOutsideTheAllowList_IsRejected(string kid)
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CredentialVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));

        var credential = Credential(authorityKey, dpop, kid);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, Space));

        Assert.Contains("kid", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credential_NamingASpaceKeyTheAuthorityDoesNotPublish_DoesNotFallBackToItsAccountKey()
    {
        // Signed with the #atproto key but labelled #atproto_space: the reference resolves
        // exactly the entry the kid names, so the account key is never tried.
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CredentialVerifier(new FakeDidDocumentResolver().PublishAccount(AuthorityDid.Value, authorityKey));

        var credential = Credential(authorityKey, dpop, SpaceAuthority.SigningKeyId);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, Space));

        Assert.Contains("#atproto_space", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credential_NamingTheAccountKey_VerifiesAgainstItEvenWhenASpaceKeyIsPublished()
    {
        using var accountKey = AtProtoCrypto.GenerateP256Key();
        using var spaceKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CredentialVerifier(new FakeDidDocumentResolver().Publish(
            AuthorityDid.Value, TwoKeyDocument(accountKey.ToMultikey(), spaceKey.ToMultikey())));

        var credential = Credential(accountKey, dpop, "#atproto");

        var verified = await verifier.VerifyAsync(
            credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, Space);

        Assert.Equal(Space, verified.Space);
    }

    [Fact]
    public async Task Credential_NamingAMalformedSpaceKey_IsRejected()
    {
        using var accountKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var verifier = CredentialVerifier(new FakeDidDocumentResolver().Publish(
            AuthorityDid.Value, TwoKeyDocument(accountKey.ToMultikey(), spaceMultibase: null)));

        var credential = Credential(accountKey, dpop, SpaceAuthority.SigningKeyId);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, Space));

        Assert.Contains("malformed", ex.Message, StringComparison.Ordinal);
    }

    private static SpaceDelegationTokenVerifier DelegationVerifier(AtProtoKey userKey) =>
        new(new FakeDidDocumentResolver().PublishAccount(UserDid.Value, userKey), new InMemoryJtiReplayStore());

    private static SpaceCredentialVerifier CredentialVerifier(IDidResolver resolver) =>
        new(resolver, new DPoPProofValidator(new InMemoryJtiReplayStore()));

    private static string Credential(AtProtoKey key, TestDPoPKey dpop, string kid) =>
        SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid.Value, Space.Value, key, dpopThumbprint: dpop.Thumbprint, keyId: kid);

    private static DidDocument TwoKeyDocument(string accountMultibase, string? spaceMultibase) => new()
    {
        Id = AuthorityDid,
        VerificationMethod =
        [
            new VerificationMethod { Id = $"{AuthorityDid}#atproto", Type = "Multikey", PublicKeyMultibase = accountMultibase },
            new VerificationMethod { Id = $"{AuthorityDid}#atproto_space", Type = "Multikey", PublicKeyMultibase = spaceMultibase },
        ],
    };
}
