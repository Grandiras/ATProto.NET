using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Temp;

// ── com.atproto.temp.checkHandleAvailability ─────────────────

/// <summary>Response from checkHandleAvailability: whether a handle is free, and if not, suggestions.</summary>
public sealed class CheckHandleAvailabilityResponse
{
    /// <summary>The handle that was checked.</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary><see cref="HandleAvailable"/>, or <see cref="HandleUnavailable"/> with suggestions.</summary>
    [JsonPropertyName("result")]
    public required HandleAvailabilityResult Result { get; init; }

    /// <summary>Whether the handle is available.</summary>
    [JsonIgnore]
    public bool IsAvailable => Result is HandleAvailable;
}

/// <summary>
/// Whether a handle is available: <see cref="HandleAvailable"/> or <see cref="HandleUnavailable"/>.
/// A result this SDK does not model reads as <see cref="UnknownHandleAvailabilityResult"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownHandleAvailabilityResult))]
[JsonDerivedType(typeof(HandleAvailable), "com.atproto.temp.checkHandleAvailability#resultAvailable")]
[JsonDerivedType(typeof(HandleUnavailable), "com.atproto.temp.checkHandleAvailability#resultUnavailable")]
public abstract class HandleAvailabilityResult : LexObject;

/// <summary>
/// A handle availability result whose <c>$type</c> this SDK version does not model. It keeps the
/// raw object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
/// <param name="type">The object's <c>$type</c>.</param>
/// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
public sealed class UnknownHandleAvailabilityResult(string type, JsonElement raw) : HandleAvailabilityResult, IUnknownUnionVariant
{
    /// <inheritdoc/>
    public string Type { get; } = UnknownUnionVariant.RequireType(type);

    /// <inheritdoc/>
    public JsonElement Raw { get; } = UnknownUnionVariant.RequireObject(raw);
}

/// <summary>The handle is available.</summary>
public sealed class HandleAvailable : HandleAvailabilityResult;

/// <summary>The handle is taken; the server suggests available ones.</summary>
public sealed class HandleUnavailable : HandleAvailabilityResult
{
    /// <summary>Available handles built from the inputs.</summary>
    [JsonPropertyName("suggestions")]
    public required IReadOnlyList<HandleSuggestion> Suggestions { get; init; }
}

/// <summary>An available handle the server suggests in place of a taken one.</summary>
public sealed class HandleSuggestion : LexObject
{
    /// <summary>The suggested handle.</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>How the suggestion was built. Opaque to clients; useful for metrics.</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }
}

// ── com.atproto.temp.checkSignupQueue ────────────────────────

/// <summary>Response from checkSignupQueue: where the signed-in account is in the signup queue.</summary>
public sealed class CheckSignupQueueResponse
{
    /// <summary>Whether the account has been activated.</summary>
    [JsonPropertyName("activated")]
    public required bool Activated { get; init; }

    /// <summary>The account's place in the queue, while it waits.</summary>
    [JsonPropertyName("placeInQueue")]
    public int? PlaceInQueue { get; init; }

    /// <summary>The estimated wait until activation, in milliseconds.</summary>
    [JsonPropertyName("estimatedTimeMs")]
    public long? EstimatedTimeMs { get; init; }
}

// ── com.atproto.temp.dereferenceScope ────────────────────────

/// <summary>Response from dereferenceScope.</summary>
internal sealed class DereferenceScopeResponse
{
    /// <summary>The full OAuth permission scope.</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }
}

// ── com.atproto.temp.requestPhoneVerification ────────────────

/// <summary>Request body for requestPhoneVerification.</summary>
internal sealed record RequestPhoneVerificationRequest(
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber);

// ── com.atproto.temp.revokeAccountCredentials ────────────────

/// <summary>Request body for revokeAccountCredentials.</summary>
internal sealed record RevokeAccountCredentialsRequest([property: JsonPropertyName("account")] AtIdentifier Account);
