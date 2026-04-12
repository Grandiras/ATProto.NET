using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Tools.Ozone.Team;

/// <summary>
/// Client for tools.ozone.team.* endpoints.
/// </summary>
public sealed class TeamClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal TeamClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Add a new team member with the specified role.
    /// </summary>
    public Task<TeamMember> AddMemberAsync(
        AddMemberRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<AddMemberRequest, TeamMember>(
            "tools.ozone.team.addMember", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Remove a team member.
    /// </summary>
    public async Task DeleteMemberAsync(
        string did,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteMemberRequest { Did = did };
        await _xrpc.ProcedureAsync<DeleteMemberRequest, object>(
            "tools.ozone.team.deleteMember", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List team members.
    /// </summary>
    public Task<ListMembersResponse> ListMembersAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };
        return _xrpc.QueryAsync<ListMembersResponse>(
            "tools.ozone.team.listMembers", parameters, cancellationToken);
    }

    /// <summary>
    /// Update a team member's role or status.
    /// </summary>
    public Task<TeamMember> UpdateMemberAsync(
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<UpdateMemberRequest, TeamMember>(
            "tools.ozone.team.updateMember", request, cancellationToken: cancellationToken);
}
