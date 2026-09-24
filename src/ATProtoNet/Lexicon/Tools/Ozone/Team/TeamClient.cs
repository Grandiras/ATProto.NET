using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Team;

/// <summary>
/// Client for tools.ozone.team.* endpoints.
/// </summary>
public sealed class TeamClient
{
    private readonly XrpcClient _xrpc;

    internal TeamClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Add a new team member with the specified role.
    /// </summary>
    /// <param name="request">The member's DID and role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<TeamMember> AddMemberAsync(
        AddMemberRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<TeamMember>(
            "tools.ozone.team.addMember", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Remove a team member.
    /// </summary>
    /// <param name="did">The member's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteMemberAsync(
        Did did,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteMemberRequest { Did = did };
        await _xrpc.ProcedureAsync(
            "tools.ozone.team.deleteMember", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of team members.
    /// </summary>
    /// <param name="limit">Maximum number of members (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListMembersResponse> ListMembersAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<ListMembersResponse>(
            "tools.ozone.team.listMembers", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every team member, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Members per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<TeamMember> EnumerateMembersAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListMembersResponse, TeamMember>(
            (cursor, ct) => ListMembersAsync(pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Update a team member's role or status.
    /// </summary>
    /// <param name="request">The member's DID and the changes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<TeamMember> UpdateMemberAsync(
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<TeamMember>(
            "tools.ozone.team.updateMember", request, cancellationToken: cancellationToken);
}
