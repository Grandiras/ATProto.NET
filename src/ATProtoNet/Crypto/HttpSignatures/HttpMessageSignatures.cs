namespace ATProtoNet.Crypto.HttpSignatures;

/// <summary>Signs HTTP messages per RFC 9421 with an ES256 or ES256K key, as a 64-byte <c>r || s</c>.</summary>
/// <remarks>See <see href="https://www.rfc-editor.org/rfc/rfc9421#section-3.1"/>.</remarks>
public static class HttpMessageSigner
{
    /// <summary>Signs exactly the given components and parameters, in the given order.</summary>
    /// <param name="request">The message, carrying every covered field.</param>
    /// <param name="parameters">The label, covered components and parameters.</param>
    /// <param name="key">The signing key.</param>
    /// <returns>The <c>Signature-Input</c> and <c>Signature</c> values to add to the message.</returns>
    /// <exception cref="ArgumentException">A covered field is missing, or the label or a component is not valid.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="key"/> is disposed.</exception>
    public static HttpSignatureFields Sign(HttpSignatureRequest request, HttpSignatureParameters parameters, SigningKey key)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(key);
        var signed = new List<KeyValuePair<string, BareItem>>();
        if (parameters.Created is { } created) signed.Add(new("created", created.ToUnixTimeSeconds()));
        if (parameters.Expires is { } expires) signed.Add(new("expires", expires.ToUnixTimeSeconds()));
        if (parameters.KeyId is { } keyId) signed.Add(new("keyid", keyId));
        var covered = new StructuredInnerList([.. parameters.Components.Select(c => new StructuredItem(c))], signed);
        var signatureBase = request.SignatureBase(covered, out var error) ?? throw new ArgumentException(error, nameof(request));
        var signature = new StructuredItem((ReadOnlyMemory<byte>)key.Sign(signatureBase));
        return new(StructuredFields.Serialize(new StructuredDictionary([new(parameters.Label, covered)])),
            StructuredFields.Serialize(new StructuredDictionary([new(parameters.Label, signature)])));
    }
}

/// <summary>What a verifier requires of an HTTP message signature.</summary>
public sealed record HttpSignatureRequirements
{
    /// <summary>The signature's label.</summary>
    public required string Label { get; init; }

    /// <summary>Field names the signature must cover, at least.</summary>
    public required IReadOnlyList<string> Components { get; init; }

    /// <summary>The clock for <c>created</c> and <c>expires</c>; without one, a signature that expires is refused.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>The tolerance of the time checks.</summary>
    public TimeSpan ClockSkew { get; init; }
}

/// <summary>Verifies RFC 9421 HTTP message signatures made with an ES256 or ES256K key.</summary>
/// <remarks>
/// The signature must be a bare 64-byte <c>r || s</c> (DER is refused); high-S is accepted
/// (<see cref="SignaturePolicy.AcceptHighS"/>). A label that appears more than once in either field is refused rather
/// than resolved, and the base carries the parameters in canonical form whatever spelling the signer used. Never
/// throws for a malformed message. See <see href="https://www.rfc-editor.org/rfc/rfc9421#section-3.2"/>.
/// </remarks>
public static class HttpMessageSignatureVerifier
{
    /// <summary>Verifies a signature against one expected key.</summary>
    /// <param name="request">The message, with its <c>Signature-Input</c> and <c>Signature</c> fields.</param>
    /// <param name="requirements">The label, the components it must cover and the clock.</param>
    /// <param name="key">The expected key; a <c>keyid</c>, when present, must be its <c>did:key</c>.</param>
    /// <returns>The verdict.</returns>
    public static HttpSignatureVerdict Verify(HttpSignatureRequest request, HttpSignatureRequirements requirements, VerificationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Verify(request, requirements, keyId => keyId is null || keyId == key.DidKey.Value ? key : null);
    }

    /// <summary>Verifies a signature against the key its <c>keyid</c> names.</summary>
    /// <param name="request">The message, with its <c>Signature-Input</c> and <c>Signature</c> fields.</param>
    /// <param name="requirements">The label, the components it must cover and the clock.</param>
    /// <param name="keyForId">The key a <c>keyid</c> (or its absence) names, or <see langword="null"/> to refuse it.</param>
    /// <returns>The verdict.</returns>
    public static HttpSignatureVerdict Verify(HttpSignatureRequest request, HttpSignatureRequirements requirements, Func<string?, VerificationKey?> keyForId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(keyForId);
        var (label, clockSkew) = (requirements.Label, requirements.ClockSkew);
        StructuredDictionary inputs, signatures;
        try
        {
            inputs = StructuredFields.ParseDictionary(request.GetField("signature-input") ?? "");
            signatures = StructuredFields.ParseDictionary(request.GetField("signature") ?? "");
        }
        catch (FormatException e)
        {
            return Refuse($"A malformed signature field: {e.Message}");
        }
        if (inputs.IsDuplicated(label) || signatures.IsDuplicated(label)) return Refuse($"The \"{label}\" signature appears more than once.");
        if (!inputs.TryGetValue(label, out var input) || input is not StructuredInnerList covered)
            return Refuse($"The \"{label}\" signature input is missing or not an inner list.");
        if (!signatures.TryGetValue(label, out var member) || member is not StructuredItem { Value: ReadOnlyMemory<byte> signature, Parameters.Count: 0 })
            return Refuse($"The \"{label}\" signature is missing or not a bare byte sequence.");
        if (!TryRead(label, covered, out var parameters, out var algorithm)) return Refuse("A signature parameter has the wrong type.");
        if (requirements.Components.Except(parameters.Components, StringComparer.Ordinal).FirstOrDefault() is { } missing)
            return Refuse($"The signature does not cover \"{missing}\".");
        var now = requirements.Time?.GetUtcNow();
        // Subtracted from the clock, not added to attacker-chosen times, which may sit at DateTimeOffset.MaxValue.
        if (parameters.Expires is { } expires && (now is null || now - clockSkew > expires)) return Refuse("The signature has expired.");
        if (parameters.Created is { } created && now is { } current && created - clockSkew > current) return Refuse("The signature was created in the future.");
        if (keyForId(parameters.KeyId) is not { } key) return Refuse("The keyid does not name an accepted key.");
        if (algorithm is not null && algorithm != (key.Curve == KeyCurve.P256 ? "ecdsa-p256-sha256" : null))
            return Refuse($"The algorithm {algorithm} is not the key's.");
        if (request.SignatureBase(covered, out var error) is not { } signatureBase) return Refuse(error);
        return key.Verify(signatureBase, signature.Span, SignaturePolicy.AcceptHighS)
            ? new HttpSignatureVerdict.Accepted(parameters, key)
            : Refuse("The signature does not verify.");
    }

    private static HttpSignatureVerdict.Refused Refuse(string reason) => new(reason);

    // The parameters the SDK models, each of its RFC 9421 type; others are covered but not interpreted.
    private static bool TryRead(string label, StructuredInnerList covered, out HttpSignatureParameters parameters, out string? algorithm)
    {
        parameters = new() { Label = label, Components = [.. covered.Items.Select(i => i.Value is string name ? name : "")] };
        algorithm = null;
        foreach (var (key, value) in covered.Parameters)
        {
            switch (key, value)
            {
                case ("keyid", string keyId):
                    parameters = parameters with { KeyId = keyId };
                    break;
                case ("alg", string alg):
                    algorithm = alg;
                    break;
                case ("created", long seconds) when TryTime(seconds, out var at):
                    parameters = parameters with { Created = at };
                    break;
                case ("expires", long seconds) when TryTime(seconds, out var at):
                    parameters = parameters with { Expires = at };
                    break;
                case ("keyid" or "alg" or "created" or "expires", _):
                    return false;
            }
        }
        return true;
    }

    private static bool TryTime(long seconds, out DateTimeOffset at)
    {
        var valid = seconds is >= 0 and <= 253402300799;
        at = valid ? DateTimeOffset.FromUnixTimeSeconds(seconds) : default;
        return valid;
    }
}
