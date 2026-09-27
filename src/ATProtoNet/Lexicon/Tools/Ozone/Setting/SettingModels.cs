using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Setting;

/// <summary>
/// One Ozone setting: a value under an NSID key, for the whole instance or for one moderator
/// (<c>tools.ozone.setting.defs#option</c>).
/// </summary>
public sealed class SettingOption : LexObject
{
    /// <summary>The setting's key.</summary>
    [JsonPropertyName("key")]
    public required Nsid Key { get; init; }

    /// <summary>The DID the setting belongs to: the Ozone service, or the moderator for a personal setting.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The setting's value, in the shape its key defines.</summary>
    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    /// <summary>A description of the setting.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>When the setting was created.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>When the setting last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>The lowest team role that may change the setting (see <c>TeamMemberRole</c>).</summary>
    [JsonPropertyName("managerRole")]
    public string? ManagerRole { get; init; }

    /// <summary>Whether the setting is for the instance or one moderator (see <see cref="SettingScope"/>).</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>The DID of the moderator who created the setting.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>The DID of the moderator who last changed the setting.</summary>
    [JsonPropertyName("lastUpdatedBy")]
    public required Did LastUpdatedBy { get; init; }
}

/// <summary>Whom a setting applies to (<see cref="SettingOption.Scope"/>).</summary>
public static class SettingScope
{
    /// <summary>The whole Ozone instance.</summary>
    public const string Instance = "instance";

    /// <summary>The moderator who set it.</summary>
    public const string Personal = "personal";
}

// ─── Request / Response Models ───

/// <summary>Response from tools.ozone.setting.listOptions.</summary>
public sealed record ListOptionsResponse : CursorPage<SettingOption>
{
    /// <summary>The settings.</summary>
    [JsonPropertyName("options")]
    public required IReadOnlyList<SettingOption> Options { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<SettingOption> Items => Options;
}

/// <summary>Request body for tools.ozone.setting.upsertOption.</summary>
internal sealed record UpsertOptionRequest(
    [property: JsonPropertyName("key")] Nsid Key,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("value")] JsonElement Value,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("managerRole")] string? ManagerRole = null);

/// <summary>Response from tools.ozone.setting.upsertOption.</summary>
public sealed class UpsertOptionResponse
{
    /// <summary>The setting as stored.</summary>
    [JsonPropertyName("option")]
    public required SettingOption Option { get; init; }
}

/// <summary>Request body for tools.ozone.setting.removeOptions.</summary>
internal sealed record RemoveOptionsRequest(
    [property: JsonPropertyName("keys")] IReadOnlyList<Nsid> Keys,
    [property: JsonPropertyName("scope")] string Scope);
