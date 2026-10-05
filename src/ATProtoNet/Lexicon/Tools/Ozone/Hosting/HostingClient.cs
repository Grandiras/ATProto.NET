using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Hosting;

/// <summary>Client for tools.ozone.hosting.* endpoints: what the account's host knows about it.</summary>
public sealed class HostingClient
{
    private readonly XrpcClient _xrpc;

    internal HostingClient(XrpcClient xrpc) => _xrpc = xrpc;

    /// <summary>Get one page of an account's history on its host: account creation, email and handle changes, email confirmation and password changes.</summary>
    /// <param name="did">The account's DID.</param>
    /// <param name="events">Only these kinds of event (see <see cref="AccountHistoryEventType"/>).</param>
    /// <param name="limit">Maximum number of events (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<GetAccountHistoryResponse> GetAccountHistoryAsync(
        Did did,
        IEnumerable<string>? events = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetAccountHistoryResponse>(
            "tools.ozone.hosting.getAccountHistory",
            new XrpcParams()
                .Add("did", did)
                .AddAll("events", events)
                .Add("cursor", cursor)
                .Add("limit", limit),
            cancellationToken: cancellationToken);
}
