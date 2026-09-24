using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Tools.Ozone.Set;

/// <summary>
/// Client for tools.ozone.set.* endpoints.
/// </summary>
public sealed class SetClient
{
    private readonly XrpcClient _xrpc;

    internal SetClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Create or update a named set.
    /// </summary>
    /// <param name="request">The set's name and description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<OzoneSetView> UpsertSetAsync(
        UpsertSetRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<OzoneSetView>(
            "tools.ozone.set.upsertSet", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Delete a named set.
    /// </summary>
    /// <param name="name">The set's name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteSetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteSetRequest { Name = name };
        await _xrpc.ProcedureAsync(
            "tools.ozone.set.deleteSet", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Add values to a named set.
    /// </summary>
    /// <param name="name">The set's name.</param>
    /// <param name="values">The values to add (at most 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AddValuesAsync(
        string name,
        IEnumerable<string> values,
        CancellationToken cancellationToken = default)
    {
        var request = new AddValuesRequest { Name = name, Values = [.. values] };
        await _xrpc.ProcedureAsync(
            "tools.ozone.set.addValues", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete values from a named set.
    /// </summary>
    /// <param name="name">The set's name.</param>
    /// <param name="values">The values to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteValuesAsync(
        string name,
        IEnumerable<string> values,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteValuesRequest { Name = name, Values = [.. values] };
        await _xrpc.ProcedureAsync(
            "tools.ozone.set.deleteValues", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the values in a named set.
    /// </summary>
    /// <param name="name">The set's name.</param>
    /// <param name="limit">Maximum number of values (1-1000, default 100).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetValuesResponse> GetValuesAsync(
        string name,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("name", name)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<GetValuesResponse>(
            "tools.ozone.set.getValues", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every value in a named set, fetching pages as needed.
    /// </summary>
    /// <param name="name">The set's name.</param>
    /// <param name="pageSize">Values per request (1-1000); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<string> EnumerateValuesAsync(
        string name,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetValuesResponse, string>(
            (cursor, ct) => GetValuesAsync(name, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Query one page of sets.
    /// </summary>
    /// <param name="limit">Maximum number of sets (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QuerySetsResponse> QuerySetsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<QuerySetsResponse>(
            "tools.ozone.set.querySets", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every set, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Sets per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<OzoneSetView> EnumerateSetsAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QuerySetsResponse, OzoneSetView>(
            (cursor, ct) => QuerySetsAsync(pageSize, cursor, ct),
            cancellationToken);
}
