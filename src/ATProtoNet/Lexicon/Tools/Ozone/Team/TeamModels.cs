using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Team;

/// <summary>
/// A team member in the Ozone moderation service.
/// </summary>
public sealed class TeamMember
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }

    [JsonPropertyName("profile")]
    public TeamMemberProfile? Profile { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }

    [JsonPropertyName("lastUpdatedBy")]
    public string? LastUpdatedBy { get; init; }
}

/// <summary>
/// Profile information for a team member.
/// </summary>
public sealed class TeamMemberProfile
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }
}

/// <summary>
/// Team member role constants.
/// </summary>
public static class TeamMemberRole
{
    public const string Admin = "tools.ozone.team.defs#roleAdmin";
    public const string Moderator = "tools.ozone.team.defs#roleModerator";
    public const string Triage = "tools.ozone.team.defs#roleTriage";
}

/// <summary>
/// Request to add a team member.
/// </summary>
public sealed class AddMemberRequest
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Request to delete a team member.
/// </summary>
public sealed class DeleteMemberRequest
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// Request to update a team member.
/// </summary>
public sealed class UpdateMemberRequest
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Response from listMembers.
/// </summary>
public sealed class ListMembersResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("members")]
    public required List<TeamMember> Members { get; init; }
}
