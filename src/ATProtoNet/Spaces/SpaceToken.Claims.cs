using System.Buffers;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.Jose;

namespace ATProtoNet.Spaces;

/// <summary>What a space authority puts in a credential it issues.</summary>
public sealed record SpaceCredentialClaims
{
    /// <summary>The space authority: the credential's issuer.</summary>
    public required Did Authority { get; init; }

    /// <summary>The space the credential grants access to: its subject.</summary>
    public required SpaceUri Space { get; init; }

    /// <summary>The P-256 key the credential is bound to (<c>cnf.kid</c>).</summary>
    public required DidKey BoundKey { get; init; }

    /// <summary>When it is issued, read from the issuer's clock.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>How long it lasts, at most <see cref="SpaceToken.MaxSpaceCredentialLifetime"/>.</summary>
    public TimeSpan Lifetime { get; init; } = SpaceToken.DefaultSpaceCredentialLifetime;
}

/// <summary>What an OAuth client puts in an attestation of itself.</summary>
public sealed record ClientAttestationClaims
{
    /// <summary>The client ID: both issuer and subject.</summary>
    public required string ClientId { get; init; }

    /// <summary>The space authority's host, <c>{authority}#atproto_pds</c>.</summary>
    public required string Audience { get; init; }

    /// <summary>When it is issued, read from the client's clock.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>How long it lasts.</summary>
    public TimeSpan Lifetime { get; init; } = SpaceToken.DefaultShortTokenLifetime;

    /// <summary>The <c>kid</c> of the client's key in its JWKS, if any.</summary>
    public string? KeyId { get; init; }
}

public sealed partial class SpaceToken
{
    /// <summary>Issues a credential, signed with the authority's <c>#atproto</c> key.</summary>
    /// <param name="claims">The credential's claims.</param>
    /// <param name="authorityKey">The authority's signing key.</param>
    /// <returns>The compact JWS.</returns>
    /// <exception cref="ArgumentException">The bound key is not P-256, or the lifetime is under a second or over <see cref="MaxSpaceCredentialLifetime"/>.</exception>
    public static string CreateCredential(SpaceCredentialClaims claims, SigningKey authorityKey)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.BoundKey.Curve != KeyCurve.P256) throw new ArgumentException("A credential is bound to a P-256 key.", nameof(claims));
        // The token counts whole seconds: a shorter lifetime would be written as exp = iat, which Parse refuses.
        if (claims.Lifetime < TimeSpan.FromSeconds(1) || claims.Lifetime > MaxSpaceCredentialLifetime)
            throw new ArgumentException($"A credential lasts at least a second and at most {MaxSpaceCredentialLifetime}.", nameof(claims));
        return Create(SpaceTokenType.Credential, SigningKeyId, claims.Authority.Value, claims.Space.Value, null, claims.BoundKey, claims.IssuedAt, claims.Lifetime, authorityKey);
    }

    /// <summary>Issues a client attestation, signed with the client's key.</summary>
    /// <param name="claims">The attestation's claims.</param>
    /// <param name="clientKey">The client's signing key.</param>
    /// <returns>The compact JWS.</returns>
    /// <exception cref="ArgumentException">The lifetime is under a second.</exception>
    public static string CreateClientAttestation(ClientAttestationClaims claims, SigningKey clientKey)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.Lifetime < TimeSpan.FromSeconds(1)) throw new ArgumentException("An attestation lasts at least a second.", nameof(claims));
        return Create(SpaceTokenType.ClientAttestation, claims.KeyId, claims.ClientId, claims.ClientId, claims.Audience, null, claims.IssuedAt, claims.Lifetime, clientKey);
    }

    // The payload in the reference's order: iss, sub, aud, cnf, iat, exp, jti.
    private static string Create(SpaceTokenType type, string? kid, string iss, string sub, string? aud, DidKey? cnf, DateTimeOffset issuedAt, TimeSpan lifetime, SigningKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var iat = issuedAt.ToUnixTimeSeconds();
        var payload = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", iss);
            writer.WriteString("sub", sub);
            if (aud is not null) writer.WriteString("aud", aud);
            if (cnf is not null)
            {
                writer.WriteStartObject("cnf");
                writer.WriteString("kid", cnf.Value);
                writer.WriteEndObject();
            }
            writer.WriteNumber("iat", iat);
            writer.WriteNumber("exp", iat + (long)lifetime.TotalSeconds);
            writer.WriteString("jti", NewJti());
            writer.WriteEndObject();
        }
        return Jws.Sign(new JwsHeader { Algorithm = Jws.AlgorithmFor(key.Curve), Type = TypeName(type), KeyId = kid }, payload.WrittenSpan, key);
    }

    private sealed record Claims(string Issuer, string Subject, string? Audience, DidKey? Cnf, DateTimeOffset? IssuedAt, DateTimeOffset ExpiresAt, string Jti)
    {
        // The reference's checks, in its order, plus a P-256 cnf.kid (SPC-19) and representable times (SPC-20).
        public static Claims Read(JsonElement payload, SpaceTokenType type)
        {
            var iss = Text(payload, "iss") ?? throw Refuse(SpaceTokenError.BadJwtIss, "The token has no iss.");
            var sub = Text(payload, "sub") ?? throw Refuse(SpaceTokenError.BadJwtSub, "The token has no sub.");
            var exp = Time(payload, "exp") ?? throw Refuse(SpaceTokenError.BadJwt, "The token has no valid exp.");
            var aud = Text(payload, "aud");
            if (type is not SpaceTokenType.Credential && aud is null) throw Refuse(SpaceTokenError.BadJwtAudience, "The token has no aud.");
            DidKey? cnf = null;
            if (type is SpaceTokenType.Credential
                && !(payload.TryGetProperty("cnf", out var c) && c.ValueKind == JsonValueKind.Object && DidKey.TryParse(Text(c, "kid"), out cnf) && cnf.Curve == KeyCurve.P256))
                throw Refuse(SpaceTokenError.BadJwtCnf, "The credential's cnf.kid is not a P-256 did:key.");
            var jti = Text(payload, "jti") is { Length: <= MaxJtiLength } id && !id.AsSpan().ContainsAnyExceptInRange('!', '~')
                ? id
                : throw Refuse(SpaceTokenError.BadJwt, $"The token has no jti of 1 to {MaxJtiLength} visible ASCII characters.");
            var iat = Time(payload, "iat");
            if (iat is null && payload.TryGetProperty("iat", out _)) throw Refuse(SpaceTokenError.BadJwt, "The token's iat is not a valid time.");
            if (type is SpaceTokenType.Credential && (iat is not { } issued || exp <= issued || exp - issued > MaxSpaceCredentialLifetime))
                throw Refuse(SpaceTokenError.BadJwt, "The credential's lifetime is invalid.");
            if (type is SpaceTokenType.ClientAttestation && iss != sub) throw Refuse(SpaceTokenError.BadJwtIss, "An attestation's iss and sub must both be the client ID.");
            return new(iss, sub, aud, cnf, iat, exp, jti);
        }

        private static string? Text(JsonElement json, string name) =>
            json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

        private static DateTimeOffset? Time(JsonElement json, string name) =>
            json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds is >= 0 and <= 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }
}
