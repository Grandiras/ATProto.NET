using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Team;

/// <summary>
/// A team member in the Ozone moderation service.
/// </summary>
public sealed class TeamMember
{
    /// <summary>The DID of the team member.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }

    /// <summary>The profile of the member, if resolved.</summary>
    [JsonPropertyName("profile")]
    public TeamMemberProfile? Profile { get; init; }

    /// <summary>The role assigned to the member.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    /// <summary>Timestamp of the last update (ISO 8601).</summary>
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }

    /// <summary>The DID of the account that last updated this.</summary>
    [JsonPropertyName("lastUpdatedBy")]
    public string? LastUpdatedBy { get; init; }
}

/// <summary>
/// Profile information for a team member.
/// </summary>
public sealed class TeamMemberProfile
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }
}

/// <summary>
/// Team member role constants.
/// </summary>
public static class TeamMemberRole
{
    /// <summary>The <c>tools.ozone.team.defs#roleAdmin</c> team member role.</summary>
    public const string Admin = "tools.ozone.team.defs#roleAdmin";

    /// <summary>The <c>tools.ozone.team.defs#roleModerator</c> team member role.</summary>
    public const string Moderator = "tools.ozone.team.defs#roleModerator";

    /// <summary>The <c>tools.ozone.team.defs#roleTriage</c> team member role.</summary>
    public const string Triage = "tools.ozone.team.defs#roleTriage";
}

/// <summary>
/// Request to add a team member.
/// </summary>
public sealed class AddMemberRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The role assigned to the member.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Request to delete a team member.
/// </summary>
public sealed class DeleteMemberRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// Request to update a team member.
/// </summary>
public sealed class UpdateMemberRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }

    /// <summary>The role assigned to the member.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Response from listMembers.
/// </summary>
public sealed class ListMembersResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The members.</summary>
    [JsonPropertyName("members")]
    public required List<TeamMember> Members { get; init; }
}
