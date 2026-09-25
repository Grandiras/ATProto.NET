using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Setting;

/// <summary>
/// Client for tools.ozone.setting.* endpoints: Ozone's key-value settings, for the whole instance
/// or for one moderator.
/// </summary>
public sealed class SettingClient
{
    private readonly XrpcClient _xrpc;

    internal SettingClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// List one page of settings.
    /// </summary>
    /// <param name="scope">
    /// Whose settings: <see cref="SettingScope.Instance"/> (the server default) or
    /// <see cref="SettingScope.Personal"/>.
    /// </param>
    /// <param name="prefix">Only settings whose key starts with this prefix.</param>
    /// <param name="keys">Only these settings (at most 100); ignored with <paramref name="prefix"/>.</param>
    /// <param name="limit">Maximum number of settings (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListOptionsResponse> ListOptionsAsync(
        string? scope = null,
        string? prefix = null,
        IEnumerable<Nsid>? keys = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor)
            .Add("scope", scope)
            .Add("prefix", prefix)
            .AddAll("keys", keys?.Select(key => key.Value));
        return _xrpc.QueryAsync<ListOptionsResponse>(
            "tools.ozone.setting.listOptions", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every setting matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="scope">
    /// Whose settings: <see cref="SettingScope.Instance"/> (the server default) or
    /// <see cref="SettingScope.Personal"/>.
    /// </param>
    /// <param name="prefix">Only settings whose key starts with this prefix.</param>
    /// <param name="keys">Only these settings (at most 100); ignored with <paramref name="prefix"/>.</param>
    /// <param name="pageSize">Settings per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<SettingOption> EnumerateOptionsAsync(
        string? scope = null,
        string? prefix = null,
        IEnumerable<Nsid>? keys = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListOptionsResponse, SettingOption>(
            (cursor, ct) => ListOptionsAsync(scope, prefix, keys, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Create a setting or replace its value.
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <param name="scope">Whom the setting applies to (see <see cref="SettingScope"/>).</param>
    /// <param name="value">The value, a JSON object.</param>
    /// <param name="description">A description of the setting.</param>
    /// <param name="managerRole">The lowest team role that may change the setting (see <c>TeamMemberRole</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<UpsertOptionResponse> UpsertOptionAsync(
        Nsid key,
        string scope,
        JsonElement value,
        string? description = null,
        string? managerRole = null,
        CancellationToken cancellationToken = default)
    {
        var request = new UpsertOptionRequest
        {
            Key = key,
            Scope = scope,
            Value = value,
            Description = description,
            ManagerRole = managerRole,
        };
        return _xrpc.ProcedureAsync<UpsertOptionResponse>(
            "tools.ozone.setting.upsertOption", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Remove settings.
    /// </summary>
    /// <param name="keys">The keys of the settings to remove (at most 200).</param>
    /// <param name="scope">Whom the settings apply to (see <see cref="SettingScope"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RemoveOptionsAsync(
        IEnumerable<Nsid> keys,
        string scope,
        CancellationToken cancellationToken = default)
    {
        var request = new RemoveOptionsRequest { Keys = [.. keys], Scope = scope };
        return _xrpc.ProcedureAsync(
            "tools.ozone.setting.removeOptions", request, cancellationToken: cancellationToken);
    }
}
