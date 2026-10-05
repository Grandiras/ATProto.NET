using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

namespace ATProtoNet.Spaces;

/// <summary>The <c>Signature-Input</c> and <c>Signature</c> header values of a signed space request.</summary>
/// <param name="SignatureInput">The <c>Signature-Input</c> header value, <c>atproto-space=(…)</c>.</param>
/// <param name="Signature">The <c>Signature</c> header value, <c>atproto-space=:…:</c>.</param>
public sealed record SpaceSignatureHeaders(string SignatureInput, string Signature);

/// <summary>Signs and verifies the <see href="https://www.rfc-editor.org/rfc/rfc9421">HTTP message signatures</see> that bind space requests to the key their credential names.</summary>
/// <remarks>
/// <para>A space credential reads a whole space and is presented to every repo host in it, so it is
/// bound at issuance to a fresh P-256 key (the credential's <c>cnf.kid</c>, a <c>did:key</c>) and every
/// request is signed by that key. The signature label is <c>atproto-space</c> and the algorithm
/// <c>ecdsa-p256-sha256</c>, with the signature as the 64 bytes <c>r || s</c>.</para>
/// <list type="bullet">
/// <item><description><b>The credential exchange</b> covers <c>"authorization"</c> (the delegation token, as
/// <c>Bearer …</c>) and names the key in the <c>keyid</c> parameter, which the authority copies into the
/// credential it issues.</description></item>
/// <item><description><b>Every request made with a credential</b> covers <c>"authorization"</c>
/// (<c>Atproto-Space …</c>) and <c>"atproto-space-audience"</c>, in that order: the DID of the party the
/// request is addressed to, which is the repo owner for a repo operation and the space authority for a
/// space-host operation. The key is the credential's.</description></item>
/// </list>
/// <para>Nothing binds a signature to the method, the URL or the moment, so one signature serves every request
/// made with the same credential and audience for as long as the credential is valid.</para>
/// </remarks>
public static class SpaceHttpSignature
{
    /// <summary>The signature label, shared by <c>Signature-Input</c> and <c>Signature</c>.</summary>
    public const string Label = "atproto-space";

    /// <summary>The signature algorithm, as the optional <c>alg</c> parameter names it.</summary>
    public const string Algorithm = "ecdsa-p256-sha256";

    /// <summary>The <c>Authorization</c> scheme a space credential is presented under.</summary>
    public const string CredentialScheme = "Atproto-Space";

    /// <summary>The header naming the DID a credential request is addressed to.</summary>
    public const string AudienceHeader = "Atproto-Space-Audience";

    private const string AuthorizationComponent = "authorization";
    private const string AudienceComponent = "atproto-space-audience";

    /// <summary>Signs a credential exchange: the delegation token in <paramref name="authorization"/>, bound to <paramref name="key"/>.</summary>
    /// <param name="key">The credential's key: a fresh P-256 key, whose <c>did:key</c> becomes the <c>keyid</c>.</param>
    /// <param name="authorization">The <c>Authorization</c> header value as it is sent, <c>Bearer &lt;delegation token&gt;</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a P-256 key.</exception>
    public static SpaceSignatureHeaders SignExchange(AtProtoKey key, string authorization) =>
        Sign(key, authorization, audience: null);

    /// <summary>Signs a request made with a space credential.</summary>
    /// <param name="key">The key the credential is bound to.</param>
    /// <param name="authorization">The <c>Authorization</c> header value as it is sent, <c>Atproto-Space &lt;credential&gt;</c>.</param>
    /// <param name="audience">The DID sent in <see cref="AudienceHeader"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a P-256 key.</exception>
    public static SpaceSignatureHeaders SignRequest(AtProtoKey key, string authorization, Did audience)
    {
        ArgumentNullException.ThrowIfNull(audience);
        return Sign(key, authorization, audience.Value);
    }

    /// <summary>Verifies a credential exchange and returns the key it is signed with, which the issued credential must be bound to.</summary>
    /// <param name="authorization">The request's <c>Authorization</c> header, which must have been sent exactly once.</param>
    /// <param name="signatureInput">The <c>Signature-Input</c> header.</param>
    /// <param name="signature">The <c>Signature</c> header.</param>
    /// <returns>The signing key's <c>did:key</c>, from <c>keyid</c>.</returns>
    /// <exception cref="SpaceSignatureException">The signature does not verify, or covers or names the wrong things.</exception>
    public static string VerifyExchange(string authorization, string signatureInput, string signature) =>
        Verify(authorization, audience: null, signatureInput, signature, credentialKeyId: null);

    /// <summary>Verifies a request made with a space credential against the key the credential is bound to.</summary>
    /// <param name="authorization">The request's <c>Authorization</c> header, which must have been sent exactly once.</param>
    /// <param name="audience">The request's <see cref="AudienceHeader"/>, which must have been sent exactly once.</param>
    /// <param name="signatureInput">The <c>Signature-Input</c> header.</param>
    /// <param name="signature">The <c>Signature</c> header.</param>
    /// <param name="credentialKeyId">The credential's <c>cnf.kid</c>, which signed the request.</param>
    /// <returns><paramref name="credentialKeyId"/>, once the signature has verified against it.</returns>
    /// <exception cref="SpaceSignatureException">The signature does not verify, or covers or names the wrong things.</exception>
    /// <remarks>This checks the signature only: that <paramref name="audience"/> is the party the request is for is the caller's to decide.</remarks>
    public static string VerifyRequest(
        string authorization, Did audience, string signatureInput, string signature, string credentialKeyId)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentException.ThrowIfNullOrEmpty(credentialKeyId);
        return Verify(authorization, audience.Value, signatureInput, signature, credentialKeyId);
    }

    private static SpaceSignatureHeaders Sign(AtProtoKey key, string authorization, string? audience)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorization);

        if (key.Curve != KeyCurve.P256)
            throw new ArgumentException("A space signature key must be a P-256 key.", nameof(key));

        var input = audience is null
            ? $"(\"{AuthorizationComponent}\");keyid=\"{key.ToDidKey()}\""
            : $"(\"{AuthorizationComponent}\" \"{AudienceComponent}\")";

        var signature = key.Sign(Base(authorization, input, audience));
        return new SpaceSignatureHeaders($"{Label}={input}", $"{Label}=:{Convert.ToBase64String(signature)}:");
    }

    // credentialKeyId is null on the credential exchange, where the key comes from the keyid parameter.
    private static string Verify(
        string authorization, string? audience, string signatureInput, string signature, string? credentialKeyId)
    {
        if (string.IsNullOrEmpty(authorization))
            throw Invalid("The request requires exactly one \"authorization\" field.");
        if (credentialKeyId is not null && string.IsNullOrEmpty(audience))
            throw Invalid($"The request requires exactly one \"{AudienceComponent}\" field.");

        StructuredFields.InnerList input;
        byte[] signatureBytes;
        try
        {
            var parsedInput = StructuredFields.ParseDictionary(signatureInput ?? string.Empty);
            var parsedSignature = StructuredFields.ParseDictionary(signature ?? string.Empty);

            // A repeated label is ambiguous across field lines, so it is refused rather than resolved by
            // taking the last, as RFC 8941 would.
            if (parsedInput.Duplicated(Label) || parsedSignature.Duplicated(Label))
                throw new FormatException($"The \"{Label}\" signature appears more than once.");

            input = parsedInput.Get(Label) as StructuredFields.InnerList
                ?? throw new FormatException($"The \"{Label}\" signature input is missing or is not an inner list.");
            signatureBytes = parsedSignature.Get(Label) is StructuredFields.Item { Value: byte[] bytes, Parameters.Count: 0 }
                ? bytes
                : throw new FormatException($"The \"{Label}\" signature is missing or is not a bare byte sequence.");
        }
        catch (FormatException ex)
        {
            throw new SpaceSignatureException($"Missing or malformed {Label} signature: {ex.Message}", ex);
        }

        var covered = credentialKeyId is null
            ? new[] { AuthorizationComponent }
            : new[] { AuthorizationComponent, AudienceComponent };
        if (!input.Items.Select(item => item is { Value: string name, Parameters.Count: 0 } ? name : null).SequenceEqual(covered))
            throw Invalid($"The signature must cover exactly {string.Join(", ", covered.Select(c => $"\"{c}\""))}, in order.");

        var alg = Parameter(input, "alg");
        if (alg is not null && alg is not Algorithm)
            throw Invalid($"The signature algorithm must be {Algorithm}.");

        var suppliedKeyId = Parameter(input, "keyid");
        var signingKey = credentialKeyId ?? suppliedKeyId as string;
        if (signingKey is null || !AtProtoCrypto.IsP256DidKey(signingKey))
            throw Invalid("The signature key must be a P-256 did:key.");

        if (credentialKeyId is not null && suppliedKeyId is not null && suppliedKeyId as string != credentialKeyId)
            throw Invalid("The signature keyid does not match the credential key.");

        // The base carries the parameters in their canonical form, whatever spelling the client used.
        var signatureBase = Base(authorization, StructuredFields.Serialize(input), audience);
        if (signatureBytes.Length != 64 || !AtProtoCrypto.TryVerifyJwtSignature(signingKey, "ES256", signatureBase, signatureBytes))
            throw Invalid("Invalid HTTP message signature.");

        return signingKey;
    }

    private static object? Parameter(StructuredFields.InnerList input, string name) =>
        input.Parameters.FirstOrDefault(p => p.Key == name).Value;

    private static byte[] Base(string authorization, string signatureParams, string? audience)
    {
        var sb = new StringBuilder().Append('"').Append(AuthorizationComponent).Append("\": ").Append(authorization.Trim());
        if (audience is not null)
            sb.Append("\n\"").Append(AudienceComponent).Append("\": ").Append(audience.Trim());

        return Encoding.UTF8.GetBytes(sb.Append("\n\"@signature-params\": ").Append(signatureParams).ToString());
    }

    private static SpaceSignatureException Invalid(string message) => new(message);
}

/// <summary>Thrown when the HTTP message signature of a space request is malformed or does not verify.</summary>
public sealed class SpaceSignatureException : AtProtoException
{
    /// <summary>Creates a new exception with the given message.</summary>
    /// <param name="message">A description of what went wrong.</param>
    public SpaceSignatureException(string message) : base(message)
    {
    }

    /// <summary>Creates a new exception with the given message and cause.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SpaceSignatureException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
