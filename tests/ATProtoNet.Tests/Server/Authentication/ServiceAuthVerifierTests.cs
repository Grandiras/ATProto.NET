using System.Numerics;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.Server.Spaces;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// The general service auth verifier: every check the 2026 spec revision (proposal 0014) asks a
/// receiver to make, and the refresh-once and replay behaviour around them.
/// </summary>
public sealed class ServiceAuthVerifierTests : IDisposable
{
    private const string Caller = "did:plc:callercallercallercaller";
    private const string Audience = "did:web:feed.example.com#bsky_fg";
    private static readonly Nsid GetFeedSkeleton = Nsid.Parse("app.bsky.feed.getFeedSkeleton");
    private static readonly string[] Audiences = [Audience];

    private readonly AtProtoKey _key = AtProtoCrypto.GenerateK256Key();
    private readonly ManualClock _clock = new();
    private readonly FakeDidDocumentResolver _resolver = new();
    private readonly InMemoryJtiReplayStore _replay = new();

    public ServiceAuthVerifierTests() => _resolver.PublishAccount(Caller, _key);

    public void Dispose() => _key.Dispose();

    private ServiceAuthVerifier Verifier(ServiceAuthVerifierOptions? options = null) =>
        new(_resolver, _replay, options, _clock);

    /// <summary>
    /// Mints a token the way <c>@atproto/xrpc-server</c>'s <c>createServiceJwt</c> does, after
    /// letting a test rewrite any header field or claim — including to values the SDK's own
    /// generator refuses to produce.
    /// </summary>
    private string Token(
        Action<Dictionary<string, object>>? payload = null,
        Action<Dictionary<string, object>>? header = null,
        AtProtoKey? signer = null)
    {
        var now = _clock.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["iss"] = Caller,
            ["aud"] = Audience,
            ["exp"] = now.AddSeconds(60).ToUnixTimeSeconds(),
            ["lxm"] = GetFeedSkeleton.Value,
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        payload?.Invoke(claims);

        signer ??= _key;
        var fields = new Dictionary<string, object> { ["typ"] = "JWT", ["alg"] = signer.Curve.JwsAlgorithm() };
        header?.Invoke(fields);

        return TestJws.Mint(fields, claims, input => signer.Sign(input));
    }

    private async Task<ServiceAuthException> RefusedAsync(string token, ServiceAuthVerifier? verifier = null) =>
        await Assert.ThrowsAsync<ServiceAuthException>(
            () => (verifier ?? Verifier()).VerifyAsync(token, Audiences, GetFeedSkeleton));

    // ── Accepted ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsItsClaims()
    {
        var verified = await Verifier().VerifyAsync(
            Token(p => p["jti"] = "0123456789abcdef0123456789abcdef"), Audiences, GetFeedSkeleton);

        Assert.Equal(Caller, verified.Issuer);
        Assert.Equal(Audience, verified.Audience);
        Assert.Equal(GetFeedSkeleton, verified.Method);
        Assert.Equal("#atproto", verified.KeyId);
        Assert.Equal("0123456789abcdef0123456789abcdef", verified.TokenId);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(60).ToUnixTimeSeconds(), verified.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task VerifyAsync_TokenFromServiceAuthGenerator_Verifies()
    {
        // The SDK's own generator and verifier agree, on both curves.
        using var p256 = AtProtoCrypto.GenerateP256Key();
        _resolver.PublishAccount("did:plc:p256p256p256p256p256p256", p256);
        using var generator = new ServiceAuthGenerator(Did.Parse("did:plc:p256p256p256p256p256p256"), p256);

        var verified = await new ServiceAuthVerifier(_resolver, _replay).VerifyAsync(
            generator.CreateToken(Audience, GetFeedSkeleton), Audiences, GetFeedSkeleton);

        Assert.Equal("did:plc:p256p256p256p256p256p256", verified.Issuer);
    }

    [Fact]
    public async Task VerifyAsync_BareDidAudienceWhenAccepted_Verifies()
    {
        // The transition set proposal 0014 recommends: the service-qualified form and the bare
        // DID that PDS proxying still sends.
        var verified = await Verifier().VerifyAsync(
            Token(p => p["aud"] = "did:web:feed.example.com"),
            [Audience, "did:web:feed.example.com"],
            GetFeedSkeleton);

        Assert.Equal("did:web:feed.example.com", verified.Audience);
    }

    [Fact]
    public async Task VerifyAsync_HighSSignature_IsAccepted()
    {
        // Generic JOSE signers do not normalize S, and @atproto/xrpc-server accepts either form
        // on a JWT. A bearer token is single-use by its jti, so malleability buys nothing.
        var token = Token();
        var parts = token.Split('.');
        var signature = TestJws.Decode(parts[2]);
        var order = BigInteger.Parse(
            "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", System.Globalization.NumberStyles.HexNumber);
        var s = new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        var highS = (order - s).ToByteArray(isUnsigned: true, isBigEndian: true);
        var malleated = new byte[64];
        signature.AsSpan(0, 32).CopyTo(malleated);
        highS.CopyTo(malleated, 64 - highS.Length);

        // High-S, so the strict check repo commits get refuses it.
        Assert.False(AtProtoCrypto.IsLowS(malleated, KeyCurve.K256));
        Assert.False(AtProtoCrypto.VerifySignature(
            _key.ToDidKey(), System.Text.Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), malleated));

        var verified = await Verifier().VerifyAsync(
            $"{parts[0]}.{parts[1]}.{TestJws.Encode(malleated)}", Audiences, GetFeedSkeleton);

        Assert.Equal(Caller, verified.Issuer);
    }

    [Fact]
    public async Task VerifyAsync_NoMethodToBind_AcceptsAnyLxmAndReportsIt()
    {
        var verified = await Verifier().VerifyAsync(Token(), Audiences, method: null);

        Assert.Equal(GetFeedSkeleton, verified.Method);
    }

    [Fact]
    public async Task VerifyAsync_WithinTheClockSkew_IsAccepted()
    {
        var token = Token(p =>
        {
            p["iat"] = _clock.GetUtcNow().AddSeconds(20).ToUnixTimeSeconds();
            p["exp"] = _clock.GetUtcNow().AddSeconds(-20).ToUnixTimeSeconds();
        });

        await Verifier().VerifyAsync(token, Audiences, GetFeedSkeleton);
    }

    // ── Structure and header ─────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("not-a-jwt")]
    [InlineData("e30.e30.!")]
    public async Task VerifyAsync_Malformed_IsRefusedAsBadJwt(string token)
    {
        var ex = await RefusedAsync(token);

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS256")]
    public async Task VerifyAsync_UnsupportedAlg_IsRefused(string alg)
    {
        var ex = await RefusedAsync(Token(header: h => h["alg"] = alg));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("alg", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_AlgForTheOtherCurve_IsRefusedOnTheSignature()
    {
        // The key is K-256: an ES256 header lies about how the token was signed.
        var ex = await RefusedAsync(Token(header: h => h["alg"] = "ES256"));

        Assert.Equal(ServiceAuthErrors.BadJwtSignature, ex.Error);
    }

    [Theory]
    [InlineData("at+jwt")]
    [InlineData("refresh+jwt")]
    [InlineData("dpop+jwt")]
    public async Task VerifyAsync_TypOfAnotherKindOfToken_IsRefused(string typ)
    {
        var ex = await RefusedAsync(Token(header: h => h["typ"] = typ));

        Assert.Equal(ServiceAuthErrors.BadJwtType, ex.Error);
    }

    // ── kid ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("#atproto")]
    [InlineData("did:plc:callercallercallercaller#atproto")] // the issuer's own key, as a DID URL
    public async Task VerifyAsync_KidNamingTheAtprotoKey_IsAccepted(string kid)
    {
        var verified = await Verifier().VerifyAsync(Token(header: h => h["kid"] = kid), Audiences, GetFeedSkeleton);

        Assert.Equal("#atproto", verified.KeyId);
    }

    [Theory]
    [InlineData("#atproto_label")]
    [InlineData("atproto")]
    [InlineData("did:plc:someoneelsesomeoneelsesom#atproto")]
    public async Task VerifyAsync_KidNotAllowed_IsRefused(string kid)
    {
        // Proposal 0014: receivers accept only the key types their use case calls for, and by
        // default that is #atproto alone.
        var ex = await RefusedAsync(Token(header: h => h["kid"] = kid));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("does not accept", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_KidThatIsNotAString_IsRefused()
    {
        var ex = await RefusedAsync(Token(header: h => h["kid"] = 1));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_AllowedKidForAnotherKey_VerifiesAgainstThatKey()
    {
        using var labelKey = AtProtoCrypto.GenerateP256Key();
        var document = FakeDidDocumentResolver.AccountDocument(Caller, _key);
        _resolver.Publish(Caller, new DidDocument
        {
            Id = document.Id,
            VerificationMethod =
            [
                .. document.VerificationMethod,
                new VerificationMethod
                {
                    Id = $"{Caller}#atproto_label",
                    Type = "Multikey",
                    Controller = Caller,
                    PublicKeyMultibase = labelKey.ToMultikey(),
                },
            ],
        });

        var options = new ServiceAuthVerifierOptions();
        options.AllowedKeyIds.Add("#atproto_label");

        var verified = await Verifier(options).VerifyAsync(
            Token(header: h => h["kid"] = "#atproto_label", signer: labelKey), Audiences, GetFeedSkeleton);

        Assert.Equal("#atproto_label", verified.KeyId);

        // …and the account key does not stand in for it.
        var ex = await RefusedAsync(Token(header: h => h["kid"] = "#atproto_label"), Verifier(options));
        Assert.Equal(ServiceAuthErrors.BadJwtSignature, ex.Error);
    }

    // ── iss ──────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_IssuerWithAServiceFragment_IsRefused()
    {
        // Allowed until the 2026 revision; the signing key is now named by "kid" instead.
        var ex = await RefusedAsync(Token(p => p["iss"] = $"{Caller}#atproto_labeler"));

        Assert.Equal(ServiceAuthErrors.BadJwtIss, ex.Error);
        Assert.Contains("bare DID", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("host.example.com")]
    [InlineData("https://host.example.com")]
    public async Task VerifyAsync_IssuerThatIsNotADid_IsRefused(string issuer)
    {
        var ex = await RefusedAsync(Token(p => p["iss"] = issuer));

        Assert.Equal(ServiceAuthErrors.BadJwtIss, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_MissingIssuer_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p.Remove("iss")));

        Assert.Equal(ServiceAuthErrors.BadJwtIss, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_IssuerThatDoesNotResolve_IsRefusedWithoutItsReason()
    {
        var ex = await RefusedAsync(Token(p => p["iss"] = "did:plc:unknownunknownunknownunk"));

        Assert.Equal(ServiceAuthErrors.BadJwtIss, ex.Error);
        Assert.IsType<DidResolutionException>(ex.InnerException);
        Assert.DoesNotContain("fixture", ex.ErrorMessage, StringComparison.Ordinal);
    }

    // ── aud ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("did:web:feed.example.com")] // bare, and not accepted here
    [InlineData("did:web:feed.example.com#bsky_labeler")] // the right DID, another service
    [InlineData("did:web:other.example.com#bsky_fg")]
    public async Task VerifyAsync_AudienceNotAccepted_IsRefused(string audience)
    {
        // A bare DID is a distinct audience, not a wildcard for every service the DID runs.
        var ex = await RefusedAsync(Token(p => p["aud"] = audience));

        Assert.Equal(ServiceAuthErrors.BadJwtAudience, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_NoAcceptedAudiences_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Verifier().VerifyAsync(Token(), [], GetFeedSkeleton));
    }

    // ── lxm ──────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_TokenForAnotherMethod_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["lxm"] = "com.atproto.repo.deleteRecord"));

        Assert.Equal(ServiceAuthErrors.BadJwtLexiconMethod, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_MethodDifferingOnlyInCase_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["lxm"] = "app.bsky.feed.getfeedskeleton"));

        Assert.Equal(ServiceAuthErrors.BadJwtLexiconMethod, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_NoLxm_IsRefusedByDefault()
    {
        var ex = await RefusedAsync(Token(p => p.Remove("lxm")));

        Assert.Equal(ServiceAuthErrors.BadJwtLexiconMethod, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_NoLxmWithTheRequirementOff_IsAccepted()
    {
        var verified = await Verifier(new ServiceAuthVerifierOptions { RequireLexiconMethod = false })
            .VerifyAsync(Token(p => p.Remove("lxm")), Audiences, GetFeedSkeleton);

        Assert.Null(verified.Method);
    }

    [Fact]
    public async Task VerifyAsync_WrongLxmWithTheRequirementOff_IsStillRefused()
    {
        var verifier = Verifier(new ServiceAuthVerifierOptions { RequireLexiconMethod = false });

        var ex = await RefusedAsync(Token(p => p["lxm"] = "com.atproto.repo.deleteRecord"), verifier);

        Assert.Equal(ServiceAuthErrors.BadJwtLexiconMethod, ex.Error);
    }

    [Theory]
    [InlineData("not an nsid")]
    [InlineData(42)]
    public async Task VerifyAsync_LxmThatIsNotAnNsid_IsRefused(object lxm)
    {
        var ex = await RefusedAsync(Token(p => p["lxm"] = lxm));

        Assert.Equal(ServiceAuthErrors.BadJwtLexiconMethod, ex.Error);
    }

    // ── exp and iat ──────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_Expired_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["exp"] = _clock.GetUtcNow().AddMinutes(-2).ToUnixTimeSeconds()));

        Assert.Equal(ServiceAuthErrors.JwtExpired, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_ValidForLongerThanTheCeiling_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["exp"] = _clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds()));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_LongLivedTokenWithARaisedCeiling_IsAccepted()
    {
        // A reference PDS mints lxm-scoped tokens of up to an hour through getServiceAuth.
        var verifier = Verifier(new ServiceAuthVerifierOptions { MaxTokenLifetime = TimeSpan.FromHours(1) });

        await verifier.VerifyAsync(
            Token(p => p["exp"] = _clock.GetUtcNow().AddMinutes(59).ToUnixTimeSeconds()), Audiences, GetFeedSkeleton);
    }

    [Fact]
    public async Task VerifyAsync_DatedInTheFuture_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["iat"] = _clock.GetUtcNow().AddMinutes(2).ToUnixTimeSeconds()));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("future", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("iat")]
    public async Task VerifyAsync_MissingTimeClaim_IsRefused(string claim)
    {
        // Both are required by the spec; @atproto/xrpc-server has always minted both.
        var ex = await RefusedAsync(Token(p => p.Remove(claim)));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains($"missing its \"{claim}\"", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("exp", 253402300800L)] // 10000-01-01, one second past what DateTimeOffset holds
    [InlineData("exp", long.MaxValue)]
    [InlineData("exp", -62135596801L)] // one second before 0001-01-01
    [InlineData("exp", long.MinValue)]
    [InlineData("iat", 253402300800L)]
    [InlineData("iat", long.MinValue)]
    public async Task VerifyAsync_TimeClaimOutsideTheRepresentableRange_IsRefusedNotThrown(string claim, long seconds)
    {
        // Regression: DateTimeOffset.FromUnixTimeSeconds threw ArgumentOutOfRangeException, which
        // reached the host as a 500 where a 401 was owed.
        var ex = await RefusedAsync(Token(p => p[claim] = seconds));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains($"\"{claim}\" is not a valid time", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("iat")]
    public async Task VerifyAsync_TimeClaimThatIsNotAWholeNumber_IsRefused(string claim)
    {
        var ex = await RefusedAsync(Token(p => p[claim] = 1.5));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_MaxRepresentableExpiry_IsRefusedAsTooLongRatherThanOverflowing()
    {
        var ex = await RefusedAsync(Token(p => p["exp"] = 253402300799L));

        Assert.Contains("longer than", ex.Message, StringComparison.Ordinal);
    }

    // ── Signature ────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_SignedByAnotherKey_IsRefusedAfterOneRefresh()
    {
        using var other = AtProtoCrypto.GenerateK256Key();

        var ex = await RefusedAsync(Token(signer: other));

        Assert.Equal(ServiceAuthErrors.BadJwtSignature, ex.Error);
        Assert.Equal(1, _resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_KeyRotatedSinceTheCachedDocument_VerifiesAgainstTheRefreshedOne()
    {
        using var rotated = AtProtoCrypto.GenerateK256Key();
        _resolver.Rotate(Caller, FakeDidDocumentResolver.AccountDocument(Caller, rotated));

        var verified = await Verifier().VerifyAsync(Token(signer: rotated), Audiences, GetFeedSkeleton);

        Assert.Equal(Caller, verified.Issuer);
        Assert.Equal(1, _resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_DoesNotRefresh()
    {
        await Verifier().VerifyAsync(Token(), Audiences, GetFeedSkeleton);

        Assert.Equal(0, _resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_IssuerPublishingNoAtprotoKey_IsRefused()
    {
        _resolver.Publish(Caller, new DidDocument { Id = Did.Parse(Caller) });

        var ex = await RefusedAsync(Token());

        Assert.Equal(ServiceAuthErrors.BadJwtSignature, ex.Error);
        Assert.Contains("publishes no usable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_TamperedPayload_IsRefused()
    {
        var parts = Token().Split('.');
        var forged = Token(p => p["iss"] = Caller);
        var tampered = $"{parts[0]}.{forged.Split('.')[1]}.{parts[2]}";

        var ex = await RefusedAsync(tampered);

        Assert.Equal(ServiceAuthErrors.BadJwtSignature, ex.Error);
    }

    // ── jti ──────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_SameTokenTwice_IsRefusedTheSecondTime()
    {
        var token = Token();
        await Verifier().VerifyAsync(token, Audiences, GetFeedSkeleton);

        var ex = await RefusedAsync(token);

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("already been used", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_MissingJti_IsRefused()
    {
        // Required by the spec; the Spaces verifier used to let a token without one through.
        var ex = await RefusedAsync(Token(p => p.Remove("jti")));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("jti", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_OverlongJti_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["jti"] = new string('a', 256)));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_ForgedToken_DoesNotSpendTheGenuineTokensJti()
    {
        // The jti is spent last, so a forgery carrying a genuine token's jti cannot burn it.
        using var attacker = AtProtoCrypto.GenerateK256Key();
        var genuine = Token(p => p["jti"] = "shared");

        await RefusedAsync(Token(p => p["jti"] = "shared", signer: attacker));

        await Verifier().VerifyAsync(genuine, Audiences, GetFeedSkeleton);
        Assert.Equal(1, _replay.Count);
    }

    [Fact]
    public async Task VerifyAsync_RefusedForItsAudience_DoesNotSpendItsJti()
    {
        await RefusedAsync(Token(p => p["aud"] = "did:web:other.example.com#bsky_fg"));

        Assert.Equal(0, _replay.Count);
    }

    // ── Review regressions ───────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_ReplayedAfterExpiryButWithinTheSkew_IsStillRefused()
    {
        // The token is accepted until exp + ClockSkew, so its jti must be kept that long. Kept
        // only until exp, it was swept while the token was still accepted, and went through again.
        var store = new InMemoryJtiReplayStore(_clock);
        var verifier = new ServiceAuthVerifier(
            _resolver, store, new ServiceAuthVerifierOptions { ClockSkew = TimeSpan.FromMinutes(5) }, _clock);
        var token = Token(); // exp: now + 60 s
        await verifier.VerifyAsync(token, Audiences, GetFeedSkeleton);

        // Past exp and the store's sweep interval, still inside the skew.
        _clock.Advance(TimeSpan.FromMinutes(2));

        var ex = await RefusedAsync(token, verifier);
        Assert.Contains("already been used", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141")] // n, on K-256
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task VerifyAsync_SignatureScalarOutOfRange_IsRefusedNotThrown(string s)
    {
        // Regression: an S of n or more threw OverflowException while normalizing to low-S.
        var parts = Token().Split('.');
        var signature = TestJws.Decode(parts[2]);
        Convert.FromHexString(s).CopyTo(signature, 32);

        var ex = await RefusedAsync($"{parts[0]}.{parts[1]}.{TestJws.Encode(signature)}");

        Assert.Equal(ServiceAuthErrors.BadJwtSignature, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_NoKidWhenAtprotoIsNotAllowed_IsRefused()
    {
        // No kid means #atproto, and #atproto is held to the allow-list like any named key.
        var options = new ServiceAuthVerifierOptions();
        options.AllowedKeyIds.Clear();
        options.AllowedKeyIds.Add("#atproto_label");

        var ex = await RefusedAsync(Token(), Verifier(options));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("does not accept", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("abc\u0000def")]
    [InlineData("abc\ndef")]
    [InlineData("abc\u0085")]
    public async Task VerifyAsync_JtiThatCannotBeSpent_IsRefusedNotThrown(string jti)
    {
        // Regression: a whitespace jti passed the check and made the replay store throw.
        var ex = await RefusedAsync(Token(p => p["jti"] = jti));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Equal(0, _replay.Count);
    }

    [Theory]
    [InlineData(SpaceTokens.DelegationType)]
    [InlineData(SpaceTokens.ClientAttestationType)]
    [InlineData("AT+JWT")]
    [InlineData("application/example+jwt")]
    public async Task VerifyAsync_ExplicitlyTypedJwtOfAnotherKind_IsRefusedEvenWithNoMethodToBind(string typ)
    {
        var ex = await Assert.ThrowsAsync<ServiceAuthException>(
            () => Verifier().VerifyAsync(Token(header: h => h["typ"] = typ), Audiences, method: null));

        Assert.Equal(ServiceAuthErrors.BadJwtType, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_SpaceDelegationTokenFromTheSameAccount_IsNotAServiceAuthToken()
    {
        // Same issuer, same #atproto key, an audience this service accepts, and no lxm: without
        // the type check a verifier binding no method (or not requiring one) took it.
        var space = SpaceUri.Parse($"at://{Caller}/space/com.example.forum/main");
        var delegation = SpaceTokens.Create(SpaceTokenType.Delegation, Caller, space.Value, _key, audience: Audience);
        // The system clock, as SpaceTokens.Create stamps the token with it.
        var lenient = new ServiceAuthVerifier(
            _resolver, _replay, new ServiceAuthVerifierOptions { RequireLexiconMethod = false });

        var ex = await Assert.ThrowsAsync<ServiceAuthException>(
            () => lenient.VerifyAsync(delegation, Audiences, GetFeedSkeleton));

        Assert.Equal(ServiceAuthErrors.BadJwtType, ex.Error);
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("jwt")]
    [InlineData(null)]
    public async Task VerifyAsync_TypedJwtOrUntyped_IsAccepted(string? typ)
    {
        var token = Token(header: h =>
        {
            if (typ is null)
                h.Remove("typ");
            else
                h["typ"] = typ;
        });

        await Verifier().VerifyAsync(token, Audiences, GetFeedSkeleton);
    }

    [Fact]
    public async Task VerifyAsync_TypThatIsNotAString_IsRefused()
    {
        var ex = await RefusedAsync(Token(header: h => h["typ"] = 1));

        Assert.Equal(ServiceAuthErrors.BadJwtType, ex.Error);
    }

    [Fact]
    public async Task VerifyAsync_IssuedLongerAgoThanTheLifetimeCeiling_IsRefused()
    {
        // As @atproto/lex-server bounds it, whatever exp the token names.
        var ex = await RefusedAsync(Token(p => p["iat"] = _clock.GetUtcNow().AddMinutes(-10).ToUnixTimeSeconds()));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("issued more than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_IssuedAsLongAgoAsTheCeilingAllows_IsAccepted()
    {
        await Verifier().VerifyAsync(
            Token(p => p["iat"] = _clock.GetUtcNow().AddMinutes(-5).AddSeconds(-30).ToUnixTimeSeconds()),
            Audiences,
            GetFeedSkeleton);
    }

    [Fact]
    public async Task VerifyAsync_NotValidYet_IsRefused()
    {
        var ex = await RefusedAsync(Token(p => p["nbf"] = _clock.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds()));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("not valid yet", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_NotBeforeWithinTheSkew_IsAccepted()
    {
        await Verifier().VerifyAsync(
            Token(p => p["nbf"] = _clock.GetUtcNow().AddSeconds(20).ToUnixTimeSeconds()), Audiences, GetFeedSkeleton);
    }

    [Theory]
    [InlineData(253402300800L)]
    [InlineData(1.5)]
    public async Task VerifyAsync_NotBeforeThatIsNotAValidTime_IsRefused(object nbf)
    {
        var ex = await RefusedAsync(Token(p => p["nbf"] = nbf));

        Assert.Equal(ServiceAuthErrors.BadJwt, ex.Error);
        Assert.Contains("\"nbf\" is not a valid time", ex.Message, StringComparison.Ordinal);
    }

    // ── Options ──────────────────────────────────────────────────

    [Theory]
    [InlineData("atproto")]
    [InlineData("did:plc:x#atproto")]
    [InlineData("#")]
    public void Constructor_AllowedKeyIdThatIsNotAFragment_Throws(string keyId)
    {
        var options = new ServiceAuthVerifierOptions();
        options.AllowedKeyIds.Add(keyId);

        Assert.Throws<ArgumentException>(() => new ServiceAuthVerifier(_resolver, _replay, options));
    }

    [Fact]
    public void Constructor_NoAllowedKeyIds_Throws()
    {
        var options = new ServiceAuthVerifierOptions();
        options.AllowedKeyIds.Clear();

        Assert.Throws<ArgumentException>(() => new ServiceAuthVerifier(_resolver, _replay, options));
    }

    [Fact]
    public void Constructor_NonPositiveLifetimeOrNegativeSkew_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ServiceAuthVerifier(
            _resolver, _replay, new ServiceAuthVerifierOptions { MaxTokenLifetime = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new ServiceAuthVerifier(
            _resolver, _replay, new ServiceAuthVerifierOptions { ClockSkew = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public async Task Constructor_CopiesTheOptions_SoLaterChangesDoNotApply()
    {
        var options = new ServiceAuthVerifierOptions();
        var verifier = Verifier(options);

        options.AllowedKeyIds.Add("#atproto_label");
        options.RequireLexiconMethod = false;

        await RefusedAsync(Token(header: h => h["kid"] = "#atproto_label"), verifier);
        await RefusedAsync(Token(p => p.Remove("lxm")), verifier);
    }

    [Fact]
    public void ServiceAuthErrors_AreTheReferenceImplementationsNames()
    {
        // @atproto/xrpc-server auth.ts, verifyJwt.
        Assert.Equal(
            ["BadJwt", "BadJwtAudience", "BadJwtIss", "BadJwtLexiconMethod", "BadJwtSignature", "BadJwtType", "JwtExpired"],
            typeof(ServiceAuthErrors).GetFields().Select(f => (string)f.GetValue(null)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Message_IsTheCallerFacingDescription()
    {
        var ex = new ServiceAuthException(ServiceAuthErrors.BadJwt, "Nope.");

        Assert.Equal("Nope.", ex.ErrorMessage);
        Assert.Contains("401 BadJwt", ex.Message, StringComparison.Ordinal);
    }
}
