using ATProtoNet.Crypto;
using ATProtoNet.Data;

namespace ATProtoNet.Identity.Plc;

/// <summary>A <c>plc_operation</c>: the DID's complete new state; a genesis operation when <see cref="PlcOperation.Prev"/> is <see langword="null"/>.</summary>
/// <remarks>
/// The limits mirror the did:plc spec (rotation keys) and the reference directory's checks on submission
/// (<c>packages/server/src/constraints.ts</c>), so a bad operation fails when it is signed rather than when it is
/// submitted. See <see href="https://web.plc.directory/spec/v0.1/did-plc"/>.
/// </remarks>
public sealed record PlcUpdate : PlcOperation
{
    /// <summary>The most rotation keys an operation carries (the spec's limit; the directory accepts up to 10).</summary>
    public const int MaxRotationKeys = 5;

    /// <summary>The most verification methods an operation carries.</summary>
    public const int MaxVerificationMethods = 10;

    /// <summary>The most <c>alsoKnownAs</c> entries an operation carries.</summary>
    public const int MaxAlsoKnownAs = 10;

    /// <summary>The longest <c>alsoKnownAs</c> entry: a 253-character handle and <c>at://</c>.</summary>
    public const int MaxAlsoKnownAsLength = 258;

    /// <summary>The most services an operation carries.</summary>
    public const int MaxServices = 10;

    /// <summary>The longest verification method or service id.</summary>
    public const int MaxIdLength = 32;

    /// <summary>The keys that control the DID, highest authority first: 1 to <see cref="MaxRotationKeys"/>, no duplicates.</summary>
    public required IReadOnlyList<DidKey> RotationKeys { get; init; }

    /// <summary>The verification methods by id, without <c>#</c> (<c>atproto</c>, <c>atproto_kem</c>, …).</summary>
    public required IReadOnlyDictionary<string, DidKey> VerificationMethods { get; init; }

    /// <summary>The DID's other identifiers (<c>at://</c> handles), no duplicates.</summary>
    public required IReadOnlyList<string> AlsoKnownAs { get; init; }

    /// <summary>The services by id, without <c>#</c> (<c>atproto_pds</c>, …).</summary>
    public required IReadOnlyDictionary<string, PlcService> Services { get; init; }

    internal override DataModelMap ToUnsignedMap()
    {
        Check(RotationKeys.Count is > 0 and <= MaxRotationKeys, $"An operation has 1 to {MaxRotationKeys} rotation keys.");
        Check(RotationKeys.Distinct().Count() == RotationKeys.Count, "Rotation keys are distinct.");
        Check(VerificationMethods.Count <= MaxVerificationMethods, $"An operation has at most {MaxVerificationMethods} verification methods.");
        Check(VerificationMethods.Keys.All(IsId), $"Verification method ids are 1 to {MaxIdLength} characters.");
        Check(AlsoKnownAs.Count <= MaxAlsoKnownAs, $"An operation has at most {MaxAlsoKnownAs} alsoKnownAs entries.");
        Check(AlsoKnownAs.All(a => a.Length <= MaxAlsoKnownAsLength) && AlsoKnownAs.Distinct().Count() == AlsoKnownAs.Count, $"alsoKnownAs entries are distinct and at most {MaxAlsoKnownAsLength} characters.");
        Check(Services.Count <= MaxServices, $"An operation has at most {MaxServices} services.");
        Check(Services.Keys.All(IsId), $"Service ids are 1 to {MaxIdLength} characters.");

        return new(
        [
            new("type", Value("plc_operation")),
            new("rotationKeys", new DataModelArray(RotationKeys.Select(k => Value(k.Value)))),
            new("verificationMethods", new DataModelMap(VerificationMethods.Select(m => KeyValuePair.Create(m.Key, (DataModelValue)Value(m.Value.Value))))),
            new("alsoKnownAs", new DataModelArray(AlsoKnownAs.Select(Value))),
            new("services", new DataModelMap(Services.Select(s => KeyValuePair.Create(s.Key, (DataModelValue)s.Value.ToMap())))),
            new("prev", Prev is null ? DataModelNull.Instance : Value(Prev.Value)),
        ]);
    }

    private static bool IsId(string id) => id.Length is > 0 and <= MaxIdLength;

    internal static void Check(bool valid, string message)
    {
        if (!valid) throw new ArgumentException(message, "operation");
    }
}

/// <summary>A service of a <c>plc_operation</c>: its type and endpoint.</summary>
/// <param name="Type">The service type, such as <c>AtprotoPersonalDataServer</c>; at most <see cref="MaxTypeLength"/> characters.</param>
/// <param name="Endpoint">The absolute endpoint URL (a file path is not one); written without a trailing slash, at most <see cref="MaxEndpointLength"/> characters.</param>
public sealed record PlcService(string Type, Uri Endpoint)
{
    /// <summary>The longest service type.</summary>
    public const int MaxTypeLength = 256;

    /// <summary>The longest endpoint, as written.</summary>
    public const int MaxEndpointLength = 512;

    internal DataModelMap ToMap()
    {
        PlcUpdate.Check(Type.Length <= MaxTypeLength && Endpoint is { IsAbsoluteUri: true, IsFile: false }, $"A service has a type of at most {MaxTypeLength} characters and an absolute URL endpoint.");
        var endpoint = Endpoint.AbsoluteUri.TrimEnd('/');
        PlcUpdate.Check(endpoint.Length <= MaxEndpointLength, $"A service endpoint is at most {MaxEndpointLength} characters.");
        return new([new("type", PlcOperation.Value(Type)), new("endpoint", PlcOperation.Value(endpoint))]);
    }

    internal static PlcService FromMap(DataModelMap map) => PlcOperation.Exact(map, 2, new PlcService(PlcOperation.Text(map, "type"),
        Uri.TryCreate(PlcOperation.Text(map, "endpoint"), UriKind.Absolute, out var endpoint) && !endpoint.IsFile ? endpoint : throw new FormatException("A service endpoint is not an absolute URL.")));
}
