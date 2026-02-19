using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Identity;

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.resolveHandle
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from resolveHandle – maps a handle to a DID.
/// </summary>
public sealed class ResolveHandleResponse
{
    /// <summary>The resolved DID.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.updateHandle
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for updateHandle.
/// </summary>
public sealed class UpdateHandleRequest
{
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.getRecommendedDidCredentials
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response with recommended DID credentials for account migration.
/// </summary>
public sealed class GetRecommendedDidCredentialsResponse
{
    [JsonPropertyName("rotationKeys")]
    public List<string>? RotationKeys { get; init; }

    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; init; }

    [JsonPropertyName("verificationMethods")]
    public Dictionary<string, string>? VerificationMethods { get; init; }

    [JsonPropertyName("services")]
    public Dictionary<string, DidService>? Services { get; init; }
}

/// <summary>
/// A DID document service entry.
/// </summary>
public sealed class DidService
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.signPlcOperation
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for signing a PLC operation.
/// </summary>
public sealed class SignPlcOperationRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("rotationKeys")]
    public List<string>? RotationKeys { get; init; }

    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; init; }

    [JsonPropertyName("verificationMethods")]
    public Dictionary<string, string>? VerificationMethods { get; init; }

    [JsonPropertyName("services")]
    public Dictionary<string, DidService>? Services { get; init; }
}

/// <summary>
/// Response with the signed PLC operation.
/// </summary>
public sealed class SignPlcOperationResponse
{
    /// <summary>A signed PLC operation object.</summary>
    [JsonPropertyName("operation")]
    public required Dictionary<string, object> Operation { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.submitPlcOperation
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for submitting a PLC operation.
/// </summary>
public sealed class SubmitPlcOperationRequest
{
    [JsonPropertyName("operation")]
    public required Dictionary<string, object> Operation { get; init; }
}
