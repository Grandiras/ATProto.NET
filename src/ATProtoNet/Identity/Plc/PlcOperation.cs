using ATProtoNet.Crypto;
using ATProtoNet.Data;

namespace ATProtoNet.Identity.Plc;

/// <summary>A <c>did:plc</c> operation, unsigned: an update (or genesis), a tombstone, or a legacy create.</summary>
/// <remarks>Sign one with <see cref="SignedPlcOperation.Sign"/>. See <see href="https://web.plc.directory/spec/v0.1/did-plc"/>.</remarks>
public closed record PlcOperation
{
    /// <summary>The CID of the operation this one follows, or <see langword="null"/> for a genesis operation.</summary>
    public Cid? Prev { get; init; }

    // The map the signature covers; validates the operation for signing.
    internal abstract DataModelMap ToUnsignedMap();

    // A signed operation as the directory serves it, read as strictly as the reference reads it.
    internal static PlcOperation FromSignedMap(DataModelMap map) => Text(map, "type") switch
    {
        "plc_operation" => Exact(map, 7, Update(map, Get(map, "prev") is DataModelNull ? null : Cid.Parse(Text(map, "prev")))),
        "plc_tombstone" => Exact(map, 3, new PlcTombstone { Prev = Cid.Parse(Text(map, "prev")) }),
        "create" => Get(map, "prev") is DataModelNull ? Exact(map, 7, new PlcLegacyCreate
        {
            SigningKey = DidKey.Parse(Text(map, "signingKey")),
            RecoveryKey = DidKey.Parse(Text(map, "recoveryKey")),
            Handle = Text(map, "handle"),
            Service = Text(map, "service"),
        }) : throw new FormatException("A legacy create operation has a null prev."),
        var type => throw new FormatException($"\"{type}\" is not a PLC operation type."),
    };

    // The fields of an update, as an operation and the directory's /data state carry them.
    internal static PlcUpdate Update(DataModelMap map, Cid? prev) => new()
    {
        RotationKeys = [.. Array(map, "rotationKeys").Select(Key)],
        VerificationMethods = Object(map, "verificationMethods").ToDictionary(e => e.Key, e => Key(e.Value)).AsReadOnly(),
        AlsoKnownAs = [.. Array(map, "alsoKnownAs").Select(v => Text(v, "alsoKnownAs"))],
        Services = Object(map, "services").ToDictionary(e => e.Key, e => PlcService.FromMap(AsMap(e.Value, "services"))).AsReadOnly(),
        Prev = prev,
    };

    internal static DataModelString Value(string value) => new(value);

    private static DataModelValue Get(DataModelMap map, string key) =>
        map.TryGetValue(key, out var value) ? value : throw new FormatException($"The operation has no \"{key}\".");

    internal static string Text(DataModelMap map, string key) => Text(Get(map, key), key);

    private static string Text(DataModelValue value, string key) =>
        value is DataModelString text ? text.Value : throw new FormatException($"\"{key}\" is not a string.");

    private static DataModelMap AsMap(DataModelValue value, string key) =>
        value as DataModelMap ?? throw new FormatException($"\"{key}\" is not an object.");

    private static DataModelMap Object(DataModelMap map, string key) => AsMap(Get(map, key), key);

    private static DataModelArray Array(DataModelMap map, string key) =>
        Get(map, key) as DataModelArray ?? throw new FormatException($"\"{key}\" is not an array.");

    private static DidKey Key(DataModelValue value) => DidKey.Parse(Text(value, "did:key"));

    // The reference's schemas are strict: a field it does not know makes the operation invalid.
    internal static T Exact<T>(DataModelMap map, int count, T operation) =>
        map.Count == count ? operation : throw new FormatException("The operation has fields its type does not define.");
}

/// <summary>A <c>plc_tombstone</c> operation: deactivates the DID for good.</summary>
/// <remarks><see cref="PlcOperation.Prev"/> is required.</remarks>
public sealed record PlcTombstone : PlcOperation
{
    internal override DataModelMap ToUnsignedMap() => Prev is null
        ? throw new ArgumentException("A tombstone names the operation it follows (Prev).", "operation")
        : new([new("type", Value("plc_tombstone")), new("prev", Value(Prev.Value))]);
}

/// <summary>A legacy <c>create</c> operation (the first version of did:plc), as old logs still hold them; read-only.</summary>
/// <remarks>The directory reads it as an update with rotation keys <see cref="RecoveryKey"/> and <see cref="SigningKey"/>.</remarks>
public sealed record PlcLegacyCreate : PlcOperation
{
    /// <summary>The account's signing key.</summary>
    public required DidKey SigningKey { get; init; }

    /// <summary>The account's recovery key.</summary>
    public required DidKey RecoveryKey { get; init; }

    /// <summary>The handle, as written (without <c>at://</c>).</summary>
    public required string Handle { get; init; }

    /// <summary>The PDS endpoint, as written.</summary>
    public required string Service { get; init; }

    internal override DataModelMap ToUnsignedMap() =>
        throw new ArgumentException("A legacy create operation is read-only and cannot be signed.", "operation");
}
