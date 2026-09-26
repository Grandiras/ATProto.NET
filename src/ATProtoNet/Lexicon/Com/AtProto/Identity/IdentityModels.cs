using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

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
    public required Did Did { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.defs
// ──────────────────────────────────────────────────────────────

/// <summary>
/// An identity as a service resolved it (<c>com.atproto.identity.defs#identityInfo</c>).
/// </summary>
public sealed class IdentityInfo : LexObject
{
    /// <summary>The account's DID.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>
    /// The account's verified handle, or <c>handle.invalid</c> (<see cref="Handle.Invalid"/>) when
    /// the handle did not bidirectionally match the DID document.
    /// </summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The complete DID document.</summary>
    [JsonPropertyName("didDoc")]
    public required DidDocument DidDoc { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.resolveDid
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from resolveDid.
/// </summary>
public sealed class ResolveDidResponse
{
    /// <summary>The complete DID document.</summary>
    [JsonPropertyName("didDoc")]
    public required DidDocument DidDoc { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.refreshIdentity
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for refreshIdentity.
/// </summary>
internal sealed class RefreshIdentityRequest
{
    /// <summary>The DID or handle to refresh.</summary>
    [JsonPropertyName("identifier")]
    public required AtIdentifier Identifier { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.updateHandle
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for updateHandle.
/// </summary>
internal sealed class UpdateHandleRequest
{
    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.identity.getRecommendedDidCredentials
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response with recommended DID credentials for account migration.
/// </summary>
public sealed class GetRecommendedDidCredentialsResponse
{
    /// <summary>The DID PLC rotation keys, as <c>did:key</c> strings.</summary>
    [JsonPropertyName("rotationKeys")]
    public List<string>? RotationKeys { get; init; }

    /// <summary>The <c>alsoKnownAs</c> entries (handles) for the DID document.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; init; }

    /// <summary>The verification methods for the DID document, keyed by key identifier.</summary>
    [JsonPropertyName("verificationMethods")]
    public Dictionary<string, string>? VerificationMethods { get; init; }

    /// <summary>The services for the DID document, keyed by service identifier.</summary>
    [JsonPropertyName("services")]
    public Dictionary<string, DidService>? Services { get; init; }
}

/// <summary>
/// A DID document service entry.
/// </summary>
public sealed class DidService
{
    /// <summary>The service type (for example <c>AtprotoPersonalDataServer</c>).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The endpoint URL of the service.</summary>
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
    /// <summary>The confirmation token emailed to the account holder.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    /// <summary>The DID PLC rotation keys, as <c>did:key</c> strings.</summary>
    [JsonPropertyName("rotationKeys")]
    public List<string>? RotationKeys { get; init; }

    /// <summary>The <c>alsoKnownAs</c> entries (handles) for the DID document.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; init; }

    /// <summary>The verification methods for the DID document, keyed by key identifier.</summary>
    [JsonPropertyName("verificationMethods")]
    public Dictionary<string, string>? VerificationMethods { get; init; }

    /// <summary>The services for the DID document, keyed by service identifier.</summary>
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
    /// <summary>The signed DID PLC operation to submit.</summary>
    [JsonPropertyName("operation")]
    public required Dictionary<string, object> Operation { get; init; }
}
