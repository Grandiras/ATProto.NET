using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.AgeAssurance;

// ──────────────────────────────────────────────────────────────
//  State
// ──────────────────────────────────────────────────────────────

/// <summary>
/// An account's age assurance state as the server computed it
/// (<c>app.bsky.ageassurance.defs#state</c>).
/// </summary>
public sealed class AgeAssuranceState : LexObject
{
    /// <summary>When the state was last updated.</summary>
    [JsonPropertyName("lastInitiatedAt")]
    public AtDatetime? LastInitiatedAt { get; init; }

    /// <summary>Where the age assurance process stands (see <see cref="AgeAssuranceStatus"/>).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The access the account is granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>
/// What a client needs to compute an account's age assurance state itself
/// (<c>app.bsky.ageassurance.defs#stateMetadata</c>).
/// </summary>
public sealed class AgeAssuranceStateMetadata : LexObject
{
    /// <summary>When the account was created.</summary>
    [JsonPropertyName("accountCreatedAt")]
    public AtDatetime? AccountCreatedAt { get; init; }
}

/// <summary>
/// Known values of <see cref="AgeAssuranceState.Status"/>.
/// </summary>
public static class AgeAssuranceStatus
{
    /// <summary>The account has not been through age assurance.</summary>
    public const string Unknown = "unknown";

    /// <summary>Age assurance was started and has not concluded.</summary>
    public const string Pending = "pending";

    /// <summary>The account's age was assured.</summary>
    public const string Assured = "assured";

    /// <summary>The account is blocked by age assurance.</summary>
    public const string Blocked = "blocked";
}

/// <summary>
/// Known values of <see cref="AgeAssuranceState.Access"/> and of a rule's granted access.
/// </summary>
public static class AgeAssuranceAccess
{
    /// <summary>The access level is not known.</summary>
    public const string Unknown = "unknown";

    /// <summary>No access.</summary>
    public const string None = "none";

    /// <summary>Access limited to content safe for minors.</summary>
    public const string Safe = "safe";

    /// <summary>Full access.</summary>
    public const string Full = "full";
}

// ──────────────────────────────────────────────────────────────
//  Configuration
// ──────────────────────────────────────────────────────────────

/// <summary>
/// The age assurance configuration, per region (<c>app.bsky.ageassurance.defs#config</c>).
/// </summary>
public sealed class AgeAssuranceConfig : LexObject
{
    /// <summary>The configuration of each region that has one.</summary>
    [JsonPropertyName("regions")]
    public required IReadOnlyList<AgeAssuranceRegion> Regions { get; init; }
}

/// <summary>
/// The age assurance configuration of one country or region
/// (<c>app.bsky.ageassurance.defs#configRegion</c>).
/// </summary>
public sealed class AgeAssuranceRegion : LexObject
{
    /// <summary>
    /// The platforms the configuration applies to (<c>web</c>, <c>ios</c>, <c>android</c>), or
    /// <see langword="null"/> for all of them.
    /// </summary>
    [JsonPropertyName("platforms")]
    public IReadOnlyList<string>? Platforms { get; init; }

    /// <summary>The ISO 3166-1 alpha-2 country code.</summary>
    [JsonPropertyName("countryCode")]
    public required string CountryCode { get; init; }

    /// <summary>The ISO 3166-2 region code, or <see langword="null"/> for the whole country.</summary>
    [JsonPropertyName("regionCode")]
    public string? RegionCode { get; init; }

    /// <summary>The minimum age, in whole years, to use Bluesky in the region.</summary>
    [JsonPropertyName("minAccessAge")]
    public required int MinAccessAge { get; init; }

    /// <summary>
    /// Verification methods the region permits besides the third-party flow, which is always
    /// available: <c>device</c> permits the platform's own age APIs.
    /// </summary>
    [JsonPropertyName("additionalVerificationMethods")]
    public IReadOnlyList<string>? AdditionalVerificationMethods { get; init; }

    /// <summary>
    /// The rules, in order: the first that matches decides the access. The last is a
    /// <see cref="DefaultAgeRule"/>.
    /// </summary>
    [JsonPropertyName("rules")]
    public required IReadOnlyList<AgeAssuranceRule> Rules { get; init; }
}

/// <summary>
/// A rule of a region's age assurance configuration (the open union behind
/// <see cref="AgeAssuranceRegion.Rules"/>). A rule this SDK does not model reads as
/// <see cref="UnknownAgeAssuranceRule"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownAgeAssuranceRule))]
[JsonDerivedType(typeof(DefaultAgeRule), "app.bsky.ageassurance.defs#configRegionRuleDefault")]
[JsonDerivedType(typeof(DeclaredOverAgeRule), "app.bsky.ageassurance.defs#configRegionRuleIfDeclaredOverAge")]
[JsonDerivedType(typeof(DeclaredUnderAgeRule), "app.bsky.ageassurance.defs#configRegionRuleIfDeclaredUnderAge")]
[JsonDerivedType(typeof(AssuredOverAgeRule), "app.bsky.ageassurance.defs#configRegionRuleIfAssuredOverAge")]
[JsonDerivedType(typeof(AssuredUnderAgeRule), "app.bsky.ageassurance.defs#configRegionRuleIfAssuredUnderAge")]
[JsonDerivedType(typeof(AccountNewerThanRule), "app.bsky.ageassurance.defs#configRegionRuleIfAccountNewerThan")]
[JsonDerivedType(typeof(AccountOlderThanRule), "app.bsky.ageassurance.defs#configRegionRuleIfAccountOlderThan")]
public abstract class AgeAssuranceRule : LexObject;

/// <summary>
/// An age assurance rule whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownAgeAssuranceRule : AgeAssuranceRule, IUnknownUnionVariant
{
    /// <summary>Creates an unknown rule from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownAgeAssuranceRule(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>The rule that applies when no other does.</summary>
public sealed class DefaultAgeRule : AgeAssuranceRule
{
    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>Applies when the account declared an age at or over <see cref="Age"/>.</summary>
public sealed class DeclaredOverAgeRule : AgeAssuranceRule
{
    /// <summary>The age threshold, in whole years.</summary>
    [JsonPropertyName("age")]
    public required int Age { get; init; }

    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>Applies when the account declared an age under <see cref="Age"/>.</summary>
public sealed class DeclaredUnderAgeRule : AgeAssuranceRule
{
    /// <summary>The age threshold, in whole years.</summary>
    [JsonPropertyName("age")]
    public required int Age { get; init; }

    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>Applies when the account's age was assured to be at or over <see cref="Age"/>.</summary>
public sealed class AssuredOverAgeRule : AgeAssuranceRule
{
    /// <summary>The age threshold, in whole years.</summary>
    [JsonPropertyName("age")]
    public required int Age { get; init; }

    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>Applies when the account's age was assured to be under <see cref="Age"/>.</summary>
public sealed class AssuredUnderAgeRule : AgeAssuranceRule
{
    /// <summary>The age threshold, in whole years.</summary>
    [JsonPropertyName("age")]
    public required int Age { get; init; }

    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>Applies when the account was created at or after <see cref="Date"/>.</summary>
public sealed class AccountNewerThanRule : AgeAssuranceRule
{
    /// <summary>The date threshold.</summary>
    [JsonPropertyName("date")]
    public required AtDatetime Date { get; init; }

    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

/// <summary>Applies when the account was created before <see cref="Date"/>.</summary>
public sealed class AccountOlderThanRule : AgeAssuranceRule
{
    /// <summary>The date threshold.</summary>
    [JsonPropertyName("date")]
    public required AtDatetime Date { get; init; }

    /// <summary>The access granted (see <see cref="AgeAssuranceAccess"/>).</summary>
    [JsonPropertyName("access")]
    public required string Access { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  API requests and responses
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for begin.
/// </summary>
internal sealed class BeginRequest
{
    /// <summary>The address to send the age assurance instructions to.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    /// <summary>The language to communicate in.</summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>The ISO 3166-1 alpha-2 code of the user's country.</summary>
    [JsonPropertyName("countryCode")]
    public required string CountryCode { get; init; }

    /// <summary>The ISO 3166-2 code of the user's region, if any.</summary>
    [JsonPropertyName("regionCode")]
    public string? RegionCode { get; init; }
}

/// <summary>
/// Response from getState.
/// </summary>
public sealed class GetStateResponse
{
    /// <summary>The state the server computed.</summary>
    [JsonPropertyName("state")]
    public required AgeAssuranceState State { get; init; }

    /// <summary>What a client needs to compute the state itself.</summary>
    [JsonPropertyName("metadata")]
    public required AgeAssuranceStateMetadata Metadata { get; init; }
}

/// <summary>
/// Error names <c>app.bsky.ageassurance.begin</c> declares, for matching with
/// <see cref="Http.XrpcException.Is"/>.
/// </summary>
public static class AgeAssuranceErrors
{
    /// <summary>The email address is not valid.</summary>
    public const string InvalidEmail = "InvalidEmail";

    /// <summary>The account's DID is too long for the age assurance provider.</summary>
    public const string DidTooLong = "DidTooLong";

    /// <summary>Age assurance cannot be started for the account.</summary>
    public const string InvalidInitiation = "InvalidInitiation";

    /// <summary>Age assurance is not offered in the given region.</summary>
    public const string RegionNotSupported = "RegionNotSupported";
}
