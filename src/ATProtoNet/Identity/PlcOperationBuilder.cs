using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Repo;

namespace ATProtoNet.Identity;

/// <summary>
/// Builds and signs <c>did:plc</c> operations — the producer counterpart to
/// <see cref="PlcClient"/>'s read-only resolution methods.
/// <para>
/// A PDS needs this to register real identities: it creates a genesis operation carrying the
/// account's rotation key, ATProto signing key, handle and PDS endpoint, signs it with a
/// rotation key, derives the DID from the signed operation's hash, and submits it to a PLC
/// directory through <see cref="PlcClient.SubmitOperationAsync(PlcSignedOperation, CancellationToken)"/>.
/// </para>
/// </summary>
/// <remarks>
/// See: https://web.plc.directory/spec/v0.1/did-plc
/// </remarks>
public static class PlcOperationBuilder
{
    /// <summary>The service id under which a PDS endpoint is registered in a PLC operation.</summary>
    public const string PdsServiceId = "atproto_pds";

    /// <summary>The service type for a personal data server.</summary>
    public const string PdsServiceType = "AtprotoPersonalDataServer";

    /// <summary>The verification-method id for a repository signing key.</summary>
    public const string AtprotoVerificationMethodId = "atproto";

    /// <summary>
    /// Builds an unsigned <c>plc_operation</c> genesis operation.
    /// </summary>
    /// <param name="rotationKeys">
    /// Ordered <c>did:key</c> rotation keys, highest authority first. At least one is required;
    /// the PLC spec allows up to five.
    /// </param>
    /// <param name="signingKeyDidKey">The repository signing key as a <c>did:key</c>.</param>
    /// <param name="handle">The account handle (without the <c>at://</c> prefix).</param>
    /// <param name="pdsEndpoint">The PDS service endpoint URL.</param>
    /// <returns>The unsigned operation as a JSON object.</returns>
    public static JsonObject CreateGenesisOperation(
        IReadOnlyList<string> rotationKeys,
        string signingKeyDidKey,
        string handle,
        string pdsEndpoint)
    {
        ArgumentNullException.ThrowIfNull(rotationKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKeyDidKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdsEndpoint);

        if (rotationKeys.Count == 0)
            throw new ArgumentException("At least one rotation key is required.", nameof(rotationKeys));
        if (rotationKeys.Count > 5)
            throw new ArgumentException("A PLC operation may carry at most five rotation keys.", nameof(rotationKeys));

        var rotation = new JsonArray();
        foreach (var key in rotationKeys)
            rotation.Add(key);

        return new JsonObject
        {
            ["type"] = "plc_operation",
            ["rotationKeys"] = rotation,
            ["verificationMethods"] = new JsonObject
            {
                [AtprotoVerificationMethodId] = signingKeyDidKey,
            },
            ["alsoKnownAs"] = new JsonArray($"at://{handle}"),
            ["services"] = new JsonObject
            {
                [PdsServiceId] = new JsonObject
                {
                    ["type"] = PdsServiceType,
                    ["endpoint"] = pdsEndpoint.TrimEnd('/'),
                },
            },
            ["prev"] = null,
        };
    }

    /// <summary>
    /// Signs a PLC operation with a rotation key and derives the resulting DID.
    /// </summary>
    /// <param name="unsignedOperation">
    /// The operation without a <c>sig</c> field (as returned by <see cref="CreateGenesisOperation"/>).
    /// </param>
    /// <param name="rotationKey">A rotation key listed in the operation's <c>rotationKeys</c>.</param>
    /// <returns>The signed operation together with its derived DID.</returns>
    /// <remarks>
    /// The DID is derived from the <em>signed</em> operation, so it is only valid for genesis
    /// operations; for updates, keep the DID the operation is being submitted under.
    /// </remarks>
    public static PlcSignedOperation Sign(JsonObject unsignedOperation, AtProtoKey rotationKey)
    {
        ArgumentNullException.ThrowIfNull(unsignedOperation);
        ArgumentNullException.ThrowIfNull(rotationKey);

        if (unsignedOperation.ContainsKey("sig"))
            throw new ArgumentException("The operation already carries a 'sig' field.", nameof(unsignedOperation));

        var unsignedCbor = DagCborEncoder.Encode(ToJsonElement(unsignedOperation));
        var signature = rotationKey.Sign(unsignedCbor);

        var signed = unsignedOperation.DeepClone().AsObject();
        signed["sig"] = Base64Url.Encode(signature);

        var signedCbor = DagCborEncoder.Encode(ToJsonElement(signed));
        return new PlcSignedOperation(DeriveDid(signedCbor), signed, signedCbor);
    }

    /// <summary>
    /// Derives a <c>did:plc</c> identifier from a signed genesis operation.
    /// The DID is <c>did:plc:</c> followed by the first 24 characters of the base32-lower
    /// encoding of the SHA-256 hash of the operation's DAG-CBOR encoding.
    /// </summary>
    /// <param name="signedOperationCbor">DAG-CBOR bytes of the signed genesis operation.</param>
    public static string DeriveDid(ReadOnlySpan<byte> signedOperationCbor)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(signedOperationCbor, hash);
        return "did:plc:" + Base32Lower.Encode(hash)[..24];
    }

    private static JsonElement ToJsonElement(JsonObject node)
        => JsonSerializer.SerializeToElement(node);
}

/// <summary>
/// A signed PLC operation together with the DID derived from it.
/// </summary>
/// <param name="Did">The DID the operation belongs to (derived from a genesis operation).</param>
/// <param name="Operation">The operation JSON including its <c>sig</c> field — the submission body.</param>
/// <param name="Cbor">The DAG-CBOR encoding of the signed operation.</param>
public sealed record PlcSignedOperation(string Did, JsonObject Operation, byte[] Cbor)
{
    /// <summary>Renders the operation as the JSON body a PLC directory expects.</summary>
    public string ToJson() => Operation.ToJsonString();
}

/// <summary>
/// Unpadded base64url encoding, as used for PLC operation signatures.
/// </summary>
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
