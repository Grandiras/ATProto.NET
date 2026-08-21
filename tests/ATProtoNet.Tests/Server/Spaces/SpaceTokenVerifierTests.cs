using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Tests.Server.Spaces;

public class SpaceDelegationTokenVerifierTests
{
    private const string UserDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherAuthorityDid = "did:plc:cccccccccccccccccccccccc";

    private static SpaceUri Space(string authority = AuthorityDid) =>
        SpaceUri.Parse($"at://{authority}/space/com.atmoboards.forum/default");

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsTheDelegatingUser()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, userKey, audience: space.HostAudience);

        var verified = await verifier.VerifyAsync(jwt, space);

        Assert.Equal(UserDid, verified.UserDid);
        Assert.Equal(space, verified.Space);
    }

    [Fact]
    public async Task VerifyAsync_TokenMintedForAnotherAuthority_IsRejected()
    {
        // The property that matters most: an authority handed a token minted for a different
        // authority cannot present it there, because the audience is derived from the token's own
        // subject rather than taken from the request.
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

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
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var minted = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other");
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, minted.Value, userKey, audience: minted.HostAudience);

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, Space()));
    }

    [Fact]
    public async Task VerifyAsync_SameTokenTwice_IsRejectedTheSecondTime()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

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
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, publishedKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var space = Space();
        var jwt = SpaceTokens.Create(
            SpaceTokenType.Delegation, UserDid, space.Value, attackerKey, audience: space.HostAudience);

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(jwt, space));
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_IsRejected()
    {
        using var userKey = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

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
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

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
        var resolver = new FakeDidDocumentResolver().PublishLegacyAccount(UserDid, "#atproto", ecdsa);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

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

        var resolver = new FakeDidDocumentResolver().Publish(UserDid, document);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

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
        var resolver = new FakeDidDocumentResolver().PublishAccount(UserDid, userKey);
        var verifier = new SpaceDelegationTokenVerifier(resolver, new InMemorySpaceReplayStore());

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, space.Value, userKey, dpopThumbprint: "abc");

        await Assert.ThrowsAsync<SpaceVerificationException>(() => verifier.VerifyAsync(credential, space));
    }
}

public class SpaceCredentialVerifierTests
{
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ImpostorDid = "did:plc:dddddddddddddddddddddddd";
    private const string Url = "https://pds.example.com/xrpc/com.atproto.space.getRecord";

    private static SpaceUri Space(string authority = AuthorityDid) =>
        SpaceUri.Parse($"at://{authority}/space/com.atmoboards.forum/default");

    private static SpaceCredentialVerifier CreateVerifier(ISpaceDidDocumentResolver resolver)
    {
        var replayStore = new InMemorySpaceReplayStore();
        return new SpaceCredentialVerifier(resolver, new DPoPProofValidator(replayStore));
    }

    [Fact]
    public async Task VerifyAsync_ValidCredentialAndProof_IsAccepted()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(AuthorityDid, authorityKey);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, space.Value, authorityKey, dpopThumbprint: dpop.Thumbprint);

        var verified = await verifier.VerifyAsync(
            credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, space);

        Assert.Equal(space, verified.Space);
        Assert.Equal(AuthorityDid, verified.AuthorityDid);
        Assert.Equal(dpop.Thumbprint, verified.Proof.KeyThumbprint);
    }

    [Fact]
    public async Task VerifyAsync_CredentialSignedByAnAuthorityThatDoesNotGateTheSpace_IsRejected()
    {
        // A credential's signer is resolved from the space URI, not from the credential's own
        // issuer — so nobody but a space's authority can mint credentials for it.
        using var impostorKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(ImpostorDid, impostorKey);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, ImpostorDid, space.Value, impostorKey, dpopThumbprint: dpop.Thumbprint);

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(
                credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, space));

        Assert.Contains("not by the space's authority", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_CredentialForAnotherSpace_IsRejected()
    {
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(AuthorityDid, authorityKey);
        var verifier = CreateVerifier(resolver);

        var granted = SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/other");
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, granted.Value, authorityKey, dpopThumbprint: dpop.Thumbprint);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(
                credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, Space()));
    }

    [Fact]
    public async Task VerifyAsync_StolenCredentialWithTheThiefsOwnKey_IsRejected()
    {
        // The scenario the DPoP binding exists for: a repo host handed a credential in order to
        // serve its own repo tries to read another host in the space with it.
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var holder = new TestDPoPKey();
        using var thief = new TestDPoPKey();
        var resolver = new FakeDidDocumentResolver().PublishAccount(AuthorityDid, authorityKey);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, AuthorityDid, space.Value, authorityKey, dpopThumbprint: holder.Thumbprint);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(
                credential, thief.Proof("GET", Url, accessToken: credential), "GET", Url, space));
    }

    [Fact]
    public async Task VerifyAsync_AuthorityPublishingADedicatedSpaceKey_VerifiesAgainstIt()
    {
        // #atproto_space takes precedence over #atproto when an authority publishes one.
        using var accountKey = AtProtoCrypto.GenerateP256Key();
        using var spaceKey = AtProtoCrypto.GenerateP256Key();
        using var dpop = new TestDPoPKey();

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

        var resolver = new FakeDidDocumentResolver().Publish(AuthorityDid, document);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential,
            AuthorityDid,
            space.Value,
            spaceKey,
            dpopThumbprint: dpop.Thumbprint,
            keyId: SpaceAuthority.SigningKeyId);

        var verified = await verifier.VerifyAsync(
            credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, space);

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
        using var dpop = new TestDPoPKey();

        var resolver = new FakeDidDocumentResolver()
            .PublishLegacyAccount(AuthorityDid, SpaceAuthority.SigningKeyId, ecdsa);
        var verifier = CreateVerifier(resolver);

        var space = Space();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential,
            AuthorityDid,
            space.Value,
            spaceKey,
            dpopThumbprint: dpop.Thumbprint,
            keyId: SpaceAuthority.SigningKeyId);

        var verified = await verifier.VerifyAsync(
            credential, dpop.Proof("GET", Url, accessToken: credential), "GET", Url, space);

        Assert.Equal(space, verified.Space);
    }
}

public class SpaceClientAttestationVerifierTests
{
    private const string ClientId = "https://app.example.com/client-metadata.json";
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";

    private static string Audience => SpaceAuthority.HostAudience(AuthorityDid);

    private static string Attestation(
        TestDPoPKey key, string? audience = null, string? kid = "key-1", TimeSpan? lifetime = null)
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

    [Fact]
    public async Task VerifyAsync_AttestationSignedByAPublishedKey_ReturnsTheClientId()
    {
        using var key = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var verified = await verifier.VerifyAsync(Attestation(key), Audience);

        Assert.Equal(ClientId, verified.ClientId);
    }

    [Fact]
    public async Task VerifyAsync_SignedByAKeyTheClientDoesNotPublish_IsRejected()
    {
        // This is what makes an allow list of client IDs enforceable rather than advisory.
        using var published = new TestDPoPKey();
        using var attacker = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, published.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(attacker), Audience));

        Assert.Equal("InvalidClientAttestation", ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_KidNamingAnUnpublishedKey_IsRejected()
    {
        using var key = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key, kid: "key-2"), Audience));

        Assert.Contains("kid 'key-2'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_NoKidWithSeveralPublishedKeys_IsRejected()
    {
        // Trying every key would let a client with one compromised key keep attesting under
        // another, so an ambiguous choice is refused rather than resolved.
        using var first = new TestDPoPKey();
        using var second = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver()
            .Publish(ClientId, first.ToJsonWebKey("key-1"), second.ToJsonWebKey("key-2"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(first, kid: null), Audience));
    }

    [Fact]
    public async Task VerifyAsync_NoKidWithOnePublishedKey_IsAccepted()
    {
        using var key = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey());
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var verified = await verifier.VerifyAsync(Attestation(key, kid: null), Audience);

        Assert.Equal(ClientId, verified.ClientId);
    }

    [Fact]
    public async Task VerifyAsync_AttestationForAnotherAuthority_IsRejected()
    {
        using var key = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var other = SpaceAuthority.HostAudience("did:plc:cccccccccccccccccccccccc");

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key, audience: other), Audience));
    }

    [Fact]
    public async Task VerifyAsync_AttestationValidForLongerThanTheCeiling_IsRejected()
    {
        using var key = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(Attestation(key, lifetime: TimeSpan.FromDays(365)), Audience));

        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_SameAttestationTwice_IsRejectedTheSecondTime()
    {
        using var key = new TestDPoPKey();
        var resolver = new FakeClientMetadataResolver().Publish(ClientId, key.ToJsonWebKey("key-1"));
        var verifier = new SpaceClientAttestationVerifier(resolver, new InMemorySpaceReplayStore());

        var attestation = Attestation(key);
        await verifier.VerifyAsync(attestation, Audience);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(attestation, Audience));
    }
}

public class SpaceServiceAuthVerifierTests
{
    private const string HostDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AuthorityDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";

    private static HttpContext ContextWith(string jwt)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {jwt}";
        return context;
    }

    /// <summary>
    /// Mints a service auth token with claims a test chooses, including ones
    /// <see cref="ServiceAuthGenerator"/> would refuse to produce.
    /// </summary>
    private static string ServiceAuth(
        AtProtoKey key, TimeSpan? lifetime = null, TimeSpan? issuedOffset = null, string? jti = null)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object> { ["typ"] = "JWT", ["alg"] = "ES256" };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = HostDid,
            ["aud"] = AuthorityDid,
            ["lxm"] = SpaceNsids.NotifyWrite,
            ["iat"] = now.Add(issuedOffset ?? TimeSpan.Zero).ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime ?? TimeSpan.FromSeconds(60)).ToUnixTimeSeconds(),
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
        };

        var headerB64 = TestDPoPKey.Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = TestDPoPKey.Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = key.Sign(Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}"));

        return $"{headerB64}.{payloadB64}.{TestDPoPKey.Base64Url(signature)}";
    }

    private static SpaceServiceAuthVerifier CreateVerifier(AtProtoKey hostKey) =>
        new(new FakeDidDocumentResolver().PublishAccount(HostDid, hostKey), new InMemorySpaceReplayStore());

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsTheCallingService()
    {
        using var hostKey = AtProtoCrypto.GenerateP256Key();

        var verified = await CreateVerifier(hostKey).VerifyAsync(
            ContextWith(ServiceAuth(hostKey)), AuthorityDid, SpaceNsids.NotifyWrite);

        Assert.Equal(HostDid, verified.Issuer);
        Assert.Equal(SpaceNsids.NotifyWrite, verified.Method);
    }

    [Fact]
    public async Task VerifyAsync_TokenValidForLongerThanTheCeiling_IsRejected()
    {
        // The exp is the issuer's own choice, and the issuer is only checked against the repo it
        // names after this. A century-long token would otherwise stay replayable for a century,
        // and would hold its jti in the replay store for just as long.
        using var hostKey = AtProtoCrypto.GenerateP256Key();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => CreateVerifier(hostKey).VerifyAsync(
                ContextWith(ServiceAuth(hostKey, lifetime: TimeSpan.FromDays(365))),
                AuthorityDid,
                SpaceNsids.NotifyWrite));

        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_TokenDatedInTheFuture_IsRejected()
    {
        using var hostKey = AtProtoCrypto.GenerateP256Key();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => CreateVerifier(hostKey).VerifyAsync(
                ContextWith(ServiceAuth(hostKey, issuedOffset: TimeSpan.FromMinutes(2))),
                AuthorityDid,
                SpaceNsids.NotifyWrite));

        Assert.Contains("future", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_IsRejected()
    {
        using var hostKey = AtProtoCrypto.GenerateP256Key();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => CreateVerifier(hostKey).VerifyAsync(
                ContextWith(ServiceAuth(hostKey, lifetime: TimeSpan.FromMinutes(-2))),
                AuthorityDid,
                SpaceNsids.NotifyWrite));

        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_SameTokenTwice_IsRejectedTheSecondTime()
    {
        using var hostKey = AtProtoCrypto.GenerateP256Key();
        var verifier = CreateVerifier(hostKey);
        var jwt = ServiceAuth(hostKey);

        await verifier.VerifyAsync(ContextWith(jwt), AuthorityDid, SpaceNsids.NotifyWrite);

        await Assert.ThrowsAsync<SpaceVerificationException>(
            () => verifier.VerifyAsync(ContextWith(jwt), AuthorityDid, SpaceNsids.NotifyWrite));
    }
}
