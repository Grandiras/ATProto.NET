using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ATProtoNet.Tests.Server.Spaces;

public class SpaceDelegationTokenVerifierTests
{
    private static readonly Did UserDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly Did OtherAuthorityDid = Did.Parse("did:plc:cccccccccccccccccccccccc");

    private static SpaceUri Space(Did? authority = null) =>
        SpaceUri.Parse($"at://{authority ?? AuthorityDid}/space/com.atmoboards.forum/default");

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsTheDelegatingUser()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, userKey, audience: space.HostAudience);

        var verified = await verifier.VerifyAsync(jwt, space);

        Assert.Equal(UserDid, verified.UserDid);
        Assert.Equal(space, verified.Space);
    }

    [Fact]
    public async Task VerifyAsync_IssuerThatIsNotADid_IsRejected()
    {
        // A delegation token is minted in the user's name, and a space's participants are DIDs.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var verifier = new SpaceDelegationTokenVerifier(new StubDidResolver(), new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, "alice.example.com", space.Value, userKey, audience: space.HostAudience);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));

        Assert.Equal("InvalidDelegationToken", ex.Error);
        Assert.Contains("must be a DID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_TokenMintedForAnotherAuthority_IsRejected()
    {
        // The property that matters most: an authority handed a token minted for a different
        // authority cannot present it there, because the audience is derived from the token's own
        // subject rather than taken from the request.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation,
            UserDid,
            space.Value,
            userKey,
            audience: SpaceAuthority.HostAudience(OtherAuthorityDid));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));

        Assert.Equal("InvalidDelegationToken", ex.Error);
        Assert.Contains("addressed to", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_TokenForAnotherSpace_IsRejected()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var minted = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other");
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, minted.Value, userKey, audience: minted.HostAudience);

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, Space()));
    }

    [Fact]
    public async Task VerifyAsync_SameTokenTwice_IsRejectedTheSecondTime()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, userKey, audience: space.HostAudience);

        await verifier.VerifyAsync(jwt, space);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
        Assert.Contains("single-use", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_SignedByAnotherKey_IsRejected()
    {
        using var publishedKey = AtProtoCrypto.GenerateP256Key();
        using var attackerKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, publishedKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, attackerKey, audience: space.HostAudience);

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_IsRejected()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation,
            UserDid,
            space.Value,
            userKey,
            audience: space.HostAudience,
            lifetime: TimeSpan.FromSeconds(-60));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_TokenValidForLongerThanTheCeiling_IsRejected()
    {
        // A delegation token lives 60 seconds, but its issuer chooses the exp it carries. One
        // dated far ahead would stay replayable — and would hold its jti in the replay store —
        // for exactly as long as it claims, so it is refused instead.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation,
            UserDid,
            space.Value,
            userKey,
            audience: space.HostAudience,
            lifetime: TimeSpan.FromDays(365));

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_UserPublishingALegacyVerificationMethod_IsAccepted()
    {
        // plc.directory serves Multikey, but older PLC releases and hand-written did:web
        // documents publish the legacy Ecdsa...VerificationKey2019 form, whose key material is a
        // bare uncompressed point. A key the SDK can read is a key this verifier must accept.
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var userKey = new AtProtoKey(ecdsa, KeyCurve.P256);
        var resolver = new StubDidResolver().PublishLegacyAccount(UserDid, "#atproto", ecdsa);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, userKey, audience: space.HostAudience);

        var verified = await verifier.VerifyAsync(jwt, space);

        Assert.Equal(UserDid, verified.UserDid);
    }

    [Fact]
    public async Task VerifyAsync_UserPublishingMalformedKeyMaterial_IsRejectedNotThrownFrom()
    {
        // The document belongs to the party being verified, so unusable key material in it is a
        // failed verification — a 401 — rather than a FormatException escaping as a 500.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var document = new ATProtoNet.Identity.DidDocument
        {
            Id = UserDid,
            VerificationMethod =
            [
                new ATProtoNet.Identity.VerificationMethod
                {
                    Id = $"{UserDid}#atproto",
                    Type = "EcdsaSecp256r1VerificationKey2019",
                    PublicKeyMultibase = "z" + AtProtoCrypto.Base58Encode(new byte[40]),
                },
            ],
        };

        var resolver = new StubDidResolver().Publish(UserDid, document);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, userKey, audience: space.HostAudience);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));

        // Reported under the caller's own error name, not a generic one.
        Assert.Equal(ATProtoNet.Lexicon.Com.AtProto.Space.SpaceErrors.InvalidDelegationToken, ex.Error);
        Assert.Contains("malformed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_CredentialPresentedAsDelegationToken_IsRejected()
    {
        // The typ header is what keeps the three token classes from being interchangeable.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, space.Value, userKey, confirmationKeyId: TestHolder.KeyId);

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(credential, space));
    }

    [Fact]
    public async Task VerifyAsync_ReplayedAfterExpiryButWithinTheSkew_IsStillRefused()
    {
        // A token is accepted until exp plus the clock skew, so its jti must be kept that long;
        // kept only until exp, the sweep dropped it while the token still verified.
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var clock = new FakeTimeProvider(start);
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore(clock), timeProvider: clock);

        var space = Space();
        var jwt = SpaceTokens.Create(SpaceTokenType.Delegation, UserDid, space.Value, userKey, audience: space.HostAudience);
        await verifier.VerifyAsync(jwt, space);

        // Two to three seconds past exp: inside SpaceTokens.DefaultClockSkew, and past the
        // store's one-minute sweep interval.
        clock.Advance(TimeSpan.FromSeconds(63));

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
    }

    [Fact]
    public async Task VerifyAsync_JtiOfOnlyWhitespace_IsRefusedNotThrown()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemoryJtiReplayStore());
        var space = Space();
        var now = DateTimeOffset.UtcNow;
        var jwt = TestJws.Mint(
            new Dictionary<string, object> { ["typ"] = SpaceTokens.DelegationType, ["alg"] = "ES256", ["kid"] = "#atproto" },
            new Dictionary<string, object>
            {
                ["iss"] = UserDid.Value,
                ["sub"] = space.Value,
                ["aud"] = space.HostAudience,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddSeconds(60).ToUnixTimeSeconds(),
                ["jti"] = " ",
            },
            input => userKey.Sign(input));

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("e30.e30.!!!!")]
    public async Task VerifyAsync_MalformedToken_IsInvalidDelegationToken(string jwt)
    {
        // How each segment decodes is JwtTests'; this pins that a failure is this verifier's error.
        var verifier = new SpaceDelegationTokenVerifier(new StubDidResolver(), new InMemoryJtiReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, Space()));

        Assert.Equal("InvalidDelegationToken", ex.Error);
    }
}

public class SpaceCredentialVerifierTests
{
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly Did ImpostorDid = Did.Parse("did:plc:dddddddddddddddddddddddd");

    private static SpaceUri Space(Did? authority = null) =>
        SpaceUri.Parse($"at://{authority ?? AuthorityDid}/space/com.atmoboards.forum/default");

    private static SpaceCredentialVerifier CreateVerifier(IDidResolver resolver)
    {
        return new SpaceCredentialVerifier(resolver);
    }

    [Fact]
    public async Task VerifyAsync_ValidCredential_IsAccepted()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(AuthorityDid, authorityKey);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, space.Value, authorityKey, confirmationKeyId: TestHolder.KeyId);

        var verified = await verifier.VerifyAsync(
            credential, space);

        Assert.Equal(space, verified.Space);
        Assert.Equal(TestHolder.KeyId, verified.Token.ConfirmationKeyId);
    }

    [Fact]
    public async Task VerifyAsync_CredentialSignedByAnAuthorityThatDoesNotGateTheSpace_IsRejected()
    {
        // A credential's signer is resolved from the space URI, not from the credential's own
        // issuer — so nobody but a space's authority can mint credentials for it.
        using var impostorKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(ImpostorDid, impostorKey);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, ImpostorDid, space.Value, impostorKey, confirmationKeyId: TestHolder.KeyId);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(
                credential, space));

        Assert.Contains("not by the space's authority", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_CredentialForAnotherSpace_IsRejected()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new StubDidResolver().PublishAccount(AuthorityDid, authorityKey);
        var verifier = CreateVerifier(resolver);

        var granted = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other");
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, granted.Value, authorityKey, confirmationKeyId: TestHolder.KeyId);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(
                credential, Space()));
    }

    [Fact]
    public async Task VerifyAsync_AuthorityPublishingADedicatedSpaceKey_VerifiesAgainstIt()
    {
        // #atproto_space takes precedence over #atproto when an authority publishes one.
        using var accountKey = AtProtoCrypto.GenerateP256Key();
        using var spaceKey = AtProtoCrypto.GenerateP256Key();

        var document = new ATProtoNet.Identity.DidDocument
        {
            Id = AuthorityDid,
            VerificationMethod =
            [
                new ATProtoNet.Identity.VerificationMethod
                {
                    Id = $"{AuthorityDid}#atproto",
                    Type = "Multikey",
                    PublicKeyMultibase = accountKey.ToMultikey(),
                },
                new ATProtoNet.Identity.VerificationMethod
                {
                    Id = $"{AuthorityDid}#atproto_space",
                    Type = "Multikey",
                    PublicKeyMultibase = spaceKey.ToMultikey(),
                },
            ],
        };

        var resolver = new StubDidResolver().Publish(AuthorityDid, document);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential,
            AuthorityDid,
            space.Value,
            spaceKey,
            confirmationKeyId: TestHolder.KeyId,
            keyId: SpaceAuthority.SigningKeyId);

        var verified = await verifier.VerifyAsync(
            credential, space);

        Assert.Equal(space, verified.Space);
    }

    [Fact]
    public async Task VerifyAsync_AuthorityPublishingALegacySpaceKey_VerifiesAgainstIt()
    {
        // Named by an explicit kid, which is the path that does not go through
        // SpaceAuthority.GetSigningKey — both must read the same set of key types, or the same
        // document verifies or fails depending only on whether the token carried a kid.
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var spaceKey = new AtProtoKey(ecdsa, KeyCurve.P256);

        var resolver = new StubDidResolver()
            .PublishLegacyAccount(AuthorityDid, SpaceAuthority.SigningKeyId, ecdsa);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential,
            AuthorityDid,
            space.Value,
            spaceKey,
            confirmationKeyId: TestHolder.KeyId,
            keyId: SpaceAuthority.SigningKeyId);

        var verified = await verifier.VerifyAsync(
            credential, space);

        Assert.Equal(space, verified.Space);
    }
}

public class SpaceClientAttestationVerifierTests
{
    private const string ClientId = "https://app.example.com/client-metadata.json";
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");

    private static string Audience => SpaceAuthority.HostAudience(AuthorityDid);

    private static string Attestation(
        TestEcKey key, string? audience = null, string? kid = "key-1", TimeSpan? lifetime = null)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object>
        {
            ["typ"] = SpaceTokens.ClientAttestationType,
            ["alg"] = "ES256",
        };

        if (kid is not null)
            header["kid"] = kid;

        var payload = new Dictionary<string, object>
        {
            ["iss"] = ClientId,
            ["sub"] = ClientId,
            ["aud"] = audience ?? Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime ?? TimeSpan.FromSeconds(60)).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };

        return key.SignJws(header, payload);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("e30.e30.!!!!")]
    public async Task VerifyAsync_MalformedAttestation_IsInvalidClientAttestation(string jwt)
    {
        var verifier = new SpaceClientAttestationVerifier(new FakeClientMetadataResolver(), new InMemoryJtiReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, Audience));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_AttestationSignedByAPublishedKey_ReturnsTheClientId()
    {
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var verified = await verifier.VerifyAsync(Attestation(key), Audience);

        Assert.Equal(ClientId, verified.ClientId);
    }

    [Fact]
    public async Task VerifyAsync_PublishedKeyWithPaddedCoordinates_Verifies()
    {
        using var key = new TestEcKey();
        var jwk = key.ToJsonWebKey("key-1");
        jwk.X += "=";
        jwk.Y += "=";
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, jwk);
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var verified = await verifier.VerifyAsync(Attestation(key), Audience);

        Assert.Equal(ClientId, verified.ClientId);
    }

    [Theory]
    [InlineData("!!!!")]
    [InlineData("AAAAA")]
    public async Task VerifyAsync_PublishedKeyWithUndecodableCoordinates_IsRejected(string x)
    {
        using var key = new TestEcKey();
        var jwk = key.ToJsonWebKey("key-1");
        jwk.X = x;
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, jwk);
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key), Audience));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_SignedByAKeyTheClientDoesNotPublish_IsRejected()
    {
        // This is what makes an allow list of client IDs enforceable rather than advisory.
        using var published = new TestEcKey();
        using var attacker = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, published.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(attacker), Audience));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_KidNamingAnUnpublishedKey_IsRejected()
    {
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key, kid: "key-2"), Audience));

        Assert.Contains("kid 'key-2'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_NoKidWithSeveralPublishedKeys_IsRejected()
    {
        // Trying every key would let a client with one compromised key keep attesting under
        // another, so an ambiguous choice is refused rather than resolved.
        using var first = new TestEcKey();
        using var second = new TestEcKey();
        var resolver = new FakeClientMetadataResolver()
            .Publish(ClientId, first.ToJsonWebKey("key-1"), second.ToJsonWebKey("key-2"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(first, kid: null), Audience));
    }

    [Fact]
    public async Task VerifyAsync_NoKidWithOnePublishedKey_IsAccepted()
    {
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey());
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var verified = await verifier.VerifyAsync(Attestation(key, kid: null), Audience);

        Assert.Equal(ClientId, verified.ClientId);
    }

    [Fact]
    public async Task VerifyAsync_AttestationForAnotherAuthority_IsRejected()
    {
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var other = SpaceAuthority.HostAudience(Did.Parse("did:plc:cccccccccccccccccccccccc"));

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key, audience: other), Audience));
    }

    [Fact]
    public async Task VerifyAsync_AttestationValidForLongerThanTheCeiling_IsRejected()
    {
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key, lifetime: TimeSpan.FromDays(365)), Audience));

        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_SameAttestationTwice_IsRejectedTheSecondTime()
    {
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore());

        var attestation = Attestation(key);
        await verifier.VerifyAsync(attestation, Audience);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(attestation, Audience));
    }

    [Fact]
    public async Task VerifyAsync_ReplayedAfterExpiryButWithinTheSkew_IsStillRefused()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var clock = new FakeTimeProvider(start);
        using var key = new TestEcKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemoryJtiReplayStore(clock), timeProvider: clock);

        var attestation = Attestation(key);
        await verifier.VerifyAsync(attestation, Audience);

        clock.Advance(TimeSpan.FromSeconds(63));

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(attestation, Audience));
    }
}

/// <summary>
/// <c>notifyWrite</c>'s service auth: the general <see cref="ServiceAuthVerifier"/>, whose checks
/// <c>ServiceAuthVerifierTests</c> pins, under the space server's options and error contract.
/// </summary>
public class NotifyWriteServiceAuthTests
{
    private const string WriterDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ServiceDid = "did:web:pds.example.com";
    private static readonly SpaceUri Space = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");
    private static readonly Nsid NotifyWrite = Nsid.Parse(SpaceNsids.NotifyWrite);

    private static NotifyWriteEndpoint Endpoint(AtProtoKey writerKey, SpaceServerOptions? options = null) =>
        new(
            new StubDidResolver().PublishAccount(WriterDid, writerKey),
            new InMemoryJtiReplayStore(),
            Substitute.For<ISpaceAuthorityStore>(),
            Substitute.For<ISpaceAccessPolicy>(),
            options ?? new SpaceServerOptions { ServiceDid = Did.Parse(ServiceDid) },
            NullLogger<NotifyWriteEndpoint>.Instance);

    private static DefaultHttpContext Request(string authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorization;
        return context;
    }

    private static string Token(AtProtoKey key, string audience = AuthorityDid, TimeSpan? expiresIn = null)
    {
        using var generator = new ServiceAuthGenerator(Did.Parse(WriterDid), key);
        return generator.CreateToken(audience, NotifyWrite, expiresIn);
    }

    [Theory]
    [InlineData(AuthorityDid)] // what the reference implementation sends
    [InlineData(AuthorityDid + "#atproto_space_host")]
    [InlineData(ServiceDid)]
    public async Task VerifyCallerAsync_EachWayOfAddressingTheAuthority_ReturnsTheWriter(string audience)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var verified = await Endpoint(key).VerifyCallerAsync(Request($"Bearer {Token(key, audience)}"), Space, default);

        Assert.Equal(WriterDid, verified.Issuer);
        Assert.Equal(audience, verified.Audience);
    }

    [Theory]
    [InlineData("DPoP {0}")]
    [InlineData("Bearer ")]
    [InlineData("")]
    public async Task VerifyCallerAsync_NoBearerToken_IsNotAuthorized(string authorization)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => Endpoint(key).VerifyCallerAsync(Request(string.Format(authorization, Token(key))), Space, default));

        Assert.Equal("NotAuthorized", ex.Error);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("not-a-jwt")]
    [InlineData("e30.e30.AAAAA")]
    public async Task VerifyCallerAsync_MalformedToken_IsNotAuthorizedWithTheServiceAuthCause(string token)
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => Endpoint(key).VerifyCallerAsync(Request($"Bearer {token}"), Space, default));

        Assert.Equal("NotAuthorized", ex.Error);
        Assert.Equal(ServiceAuthErrors.BadJwt, Assert.IsType<ServiceAuthException>(ex.InnerException).Error);
    }

    [Fact]
    public async Task VerifyCallerAsync_SameTokenTwice_IsRefusedTheSecondTime()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var endpoint = Endpoint(key);
        var token = Token(key);

        await endpoint.VerifyCallerAsync(Request($"Bearer {token}"), Space, default);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => endpoint.VerifyCallerAsync(Request($"Bearer {token}"), Space, default));
        Assert.Contains("already been used", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyCallerAsync_TokenLongerThanTheSpaceCeiling_IsRefused()
    {
        // The ceiling is SpaceServerOptions.MaxSingleUseTokenLifetime, not the verifier's default.
        using var key = AtProtoCrypto.GenerateP256Key();
        var options = new SpaceServerOptions { MaxSingleUseTokenLifetime = TimeSpan.FromMinutes(1) };

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => Endpoint(key, options).VerifyCallerAsync(
                Request($"Bearer {Token(key, expiresIn: TimeSpan.FromMinutes(4))}"), Space, default));

        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "expired")] // the default 5-second skew: expired 10 seconds ago by the host's clock
    [InlineData(30, null)]        // SpaceServerOptions.ClockSkew covers it
    public async Task VerifyCallerAsync_FromTheContainer_UsesTheHostsClockAndTheSpaceClockSkew(int? skewSeconds, string? refusal)
    {
        // A 60-second token presented 70 seconds later by the registered TimeProvider.
        using var key = AtProtoCrypto.GenerateP256Key();
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow.AddSeconds(70)));
        services.AddKeyedSingleton<IDidResolver>(
            SpaceServerExtensions.DidResolverKey, new StubDidResolver().PublishAccount(WriterDid, key));
        services
            .AddAtProtoSpaces(o =>
            {
                o.ServiceDid = Did.Parse(AuthorityDid);
                if (skewSeconds is { } seconds)
                    o.ClockSkew = TimeSpan.FromSeconds(seconds);
            })
            .AddSpaceAuthority<InMemorySpaceAuthorityStore>(authorityKey)
            .AddSimpleSpace<InMemorySimpleSpaceStore>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<NotifyWriteEndpoint>();

        var verify = () => endpoint.VerifyCallerAsync(Request($"Bearer {Token(key)}"), Space, default);

        if (refusal is null)
        {
            Assert.Equal(WriterDid, (await verify()).Issuer);
            return;
        }

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(verify);
        Assert.Contains(refusal, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
