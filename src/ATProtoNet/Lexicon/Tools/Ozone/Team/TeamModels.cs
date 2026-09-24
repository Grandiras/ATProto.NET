using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Team;

/// <summary>
/// A team member in the Ozone moderation service.
/// </summary>
public sealed class TeamMember : LexObject
{
    /// <summary>The DID of the team member.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }

    /// <summary>The profile of the member, if resolved.</summary>
    [JsonPropertyName("profile")]
    public TeamMemberProfile? Profile { get; init; }

    /// <summary>The role assigned to the member.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>When the member was added.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>When the member was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>
    /// Who last updated the member: a moderator's DID, or <c>admin_token</c> when an admin token
    /// made the change.
    /// </summary>
    [JsonPropertyName("lastUpdatedBy")]
    public string? LastUpdatedBy { get; init; }
}

/// <summary>
/// Profile information for a team member.
/// </summary>
public sealed class TeamMemberProfile : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

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
    public required Did Did { get; init; }

    /// <summary>The role assigned to the member.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Request to delete a team member.
/// </summary>
internal sealed class DeleteMemberRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// Request to update a team member.
/// </summary>
public sealed class UpdateMemberRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

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
public sealed class ListMembersResponse : ICursorPage<TeamMember>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The members.</summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<TeamMember> Members { get; init; }

    IReadOnlyList<TeamMember> ICursorPage<TeamMember>.Items => Members;
}
