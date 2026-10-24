using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Data;

namespace ATProtoNet.Identity.Plc;

/// <summary>A signed <c>did:plc</c> operation: what the directory accepts and serves.</summary>
/// <remarks>
/// The signature covers the canonical DRISL of the operation without <c>sig</c> and is written as unpadded
/// base64url; the operation's CID and a genesis operation's DID are computed over the signed encoding.
/// See <see href="https://web.plc.directory/spec/v0.1/did-plc"/>.
/// </remarks>
public sealed class SignedPlcOperation
{
    /// <summary>The largest signed operation the directory accepts, in DRISL bytes.</summary>
    public const int MaxOperationBytes = 4000;

    private readonly byte[] _drisl;

    private SignedPlcOperation(PlcOperation operation, string signature, byte[] drisl)
    {
        Operation = operation;
        Signature = StrictBase64.TryDecode(signature.AsSpan(), url: true, allowPadding: false, out var bytes) ? bytes : throw new FormatException("The signature is not unpadded base64url.");
        Cid = Cid.Compute(CidCodec.DagCbor, drisl);
        _drisl = drisl;
    }

    /// <summary>The operation.</summary>
    public PlcOperation Operation { get; }

    /// <summary>The raw signature, <c>r‖s</c>.</summary>
    public ReadOnlyMemory<byte> Signature { get; }

    /// <summary>The CID of the signed operation: what the next operation's <see cref="PlcOperation.Prev"/> names.</summary>
    public Cid Cid { get; }

    /// <summary>Signs an operation with a rotation key; the operation is not changed.</summary>
    /// <param name="operation">An update, genesis or tombstone.</param>
    /// <param name="rotationKey">A rotation key of the previous operation, or of this one for a genesis operation.</param>
    /// <returns>The signed operation.</returns>
    /// <exception cref="ArgumentException">The operation is a legacy create, breaks a limit, is a tombstone without <see cref="PlcOperation.Prev"/>, or is a genesis operation whose rotation keys do not include <paramref name="rotationKey"/>.</exception>
    public static SignedPlcOperation Sign(PlcOperation operation, SigningKey rotationKey)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(rotationKey);
        var unsigned = operation.ToUnsignedMap();
        PlcUpdate.Check(operation is not PlcUpdate { Prev: null } genesis || genesis.RotationKeys.Contains(rotationKey.PublicKey.DidKey), "A genesis operation is signed by one of its own rotation keys.");
        var drisl = SignedObject.Sign(unsigned, rotationKey, SignatureEncoding.Base64Url);
        PlcUpdate.Check(drisl.Length <= MaxOperationBytes, $"A signed operation is at most {MaxOperationBytes} bytes of DRISL.");
        SignedDrisl.GetUnsignedBytes(drisl, out var signature);
        return new(operation, ((DataModelString)signature!).Value, drisl);
    }

    /// <summary>Reads a signed operation in the JSON the directory serves and accepts.</summary>
    /// <param name="json">One operation object, as UTF-8.</param>
    /// <returns>The operation.</returns>
    /// <exception cref="FormatException">The JSON is malformed, its <c>type</c> is unknown, or it does not have exactly its type's fields.</exception>
    public static SignedPlcOperation Parse(ReadOnlySpan<byte> json)
    {
        DataModelValue value;
        try
        {
            value = DataModelValue.FromJson(json);
        }
        catch (JsonException e)
        {
            throw new FormatException("The PLC operation is not data-model JSON.", e);
        }
        if (value is not DataModelMap map) throw new FormatException("The PLC operation is not an object.");
        return new(PlcOperation.FromSignedMap(map), PlcOperation.Text(map, "sig"), map.ToDrisl());
    }

    /// <summary>The DID a genesis operation creates: <c>did:plc:</c> and the first 24 characters of the base32 SHA-256 of the signed operation.</summary>
    /// <returns>The DID.</returns>
    /// <exception cref="InvalidOperationException">The operation is not a genesis operation.</exception>
    public Did DeriveDid()
    {
        if (Operation.Prev is not null) throw new InvalidOperationException("Only a genesis operation creates a DID.");
        return Did.Parse("did:plc:" + Base32.Encode(SHA256.HashData(_drisl))[..24]);
    }

    /// <summary>Whether the signature is a valid low-S signature by <paramref name="rotationKey"/>.</summary>
    /// <param name="rotationKey">The rotation key to check.</param>
    /// <returns>Whether it verifies.</returns>
    public bool Verify(DidKey rotationKey)
    {
        ArgumentNullException.ThrowIfNull(rotationKey);
        return SignedObject.Verify(_drisl, rotationKey.ToVerificationKey(), SignatureEncoding.Base64Url);
    }

    /// <summary>The operation as JSON, the body to submit to the directory.</summary>
    /// <returns>UTF-8 JSON.</returns>
    public ReadOnlyMemory<byte> ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) DrislJson.WriteJson(_drisl, writer);
        return buffer.WrittenMemory;
    }
}
