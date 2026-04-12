using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Tools.Ozone.Set;

/// <summary>
/// Client for tools.ozone.set.* endpoints.
/// </summary>
public sealed class SetClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal SetClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Create or update a named set.
    /// </summary>
    public Task<OzoneSetView> UpsertSetAsync(
        UpsertSetRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<UpsertSetRequest, OzoneSetView>(
            "tools.ozone.set.upsertSet", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Delete a named set.
    /// </summary>
    public async Task DeleteSetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteSetRequest { Name = name };
        await _xrpc.ProcedureAsync<DeleteSetRequest, object>(
            "tools.ozone.set.deleteSet", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Add values to a named set.
    /// </summary>
    public async Task AddValuesAsync(
        string name,
        List<string> values,
        CancellationToken cancellationToken = default)
    {
        var request = new AddValuesRequest { Name = name, Values = values };
        await _xrpc.ProcedureAsync<AddValuesRequest, object>(
            "tools.ozone.set.addValues", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete values from a named set.
    /// </summary>
    public async Task DeleteValuesAsync(
        string name,
        List<string> values,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteValuesRequest { Name = name, Values = values };
        await _xrpc.ProcedureAsync<DeleteValuesRequest, object>(
            "tools.ozone.set.deleteValues", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get values from a named set.
    /// </summary>
    public Task<GetValuesResponse> GetValuesAsync(
        string name,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["name"] = name,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };
        return _xrpc.QueryAsync<GetValuesResponse>(
            "tools.ozone.set.getValues", parameters, cancellationToken);
    }

    /// <summary>
    /// Query all sets.
    /// </summary>
    public Task<QuerySetsResponse> QuerySetsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };
        return _xrpc.QueryAsync<QuerySetsResponse>(
            "tools.ozone.set.querySets", parameters, cancellationToken);
    }
}
