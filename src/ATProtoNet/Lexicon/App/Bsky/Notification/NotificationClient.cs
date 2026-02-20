using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.App.Bsky.Notification;

/// <summary>
/// Client for app.bsky.notification.* XRPC endpoints.
/// </summary>
public sealed class NotificationClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal NotificationClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// List notifications for the authenticated user.
    /// </summary>
    /// <param name="limit">Max notifications per page (1-100, default 50).</param>
    /// <param name="priority">Filter for priority notifications only.</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="seenAt">Timestamp to filter new notifications since
    /// (only return notifications after this time).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListNotificationsResponse> ListNotificationsAsync(
        int? limit = null,
        bool? priority = null,
        string? cursor = null,
        string? seenAt = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["priority"] = priority?.ToString()?.ToLowerInvariant(),
            ["cursor"] = cursor,
            ["seenAt"] = seenAt,
        };

        return _xrpc.QueryAsync<ListNotificationsResponse>(
            "app.bsky.notification.listNotifications", parameters, cancellationToken);
    }

    /// <summary>
    /// Get the count of unread notifications.
    /// </summary>
    /// <param name="priority">Count only priority notifications.</param>
    /// <param name="seenAt">Count notifications newer than this timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetUnreadCountResponse> GetUnreadCountAsync(
        bool? priority = null,
        string? seenAt = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["priority"] = priority?.ToString()?.ToLowerInvariant(),
            ["seenAt"] = seenAt,
        };

        return _xrpc.QueryAsync<GetUnreadCountResponse>(
            "app.bsky.notification.getUnreadCount", parameters, cancellationToken);
    }

    /// <summary>
    /// Mark notifications as seen up to the given timestamp.
    /// </summary>
    /// <param name="seenAt">ISO 8601 timestamp of when the user last viewed notifications.
    /// Pass <c>AtProtoJsonDefaults.NowTimestamp()</c> to mark all as read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateSeenAsync(
        string seenAt, CancellationToken cancellationToken = default)
    {
        var request = new UpdateSeenRequest { SeenAt = seenAt };
        await _xrpc.ProcedureAsync<UpdateSeenRequest, object>(
            "app.bsky.notification.updateSeen", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mark all notifications as read (convenience method).
    /// </summary>
    public Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        return UpdateSeenAsync(Serialization.AtProtoJsonDefaults.NowTimestamp(), cancellationToken);
    }

    /// <summary>
    /// Register a push notification token.
    /// </summary>
    public async Task RegisterPushAsync(
        RegisterPushRequest request, CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync<RegisterPushRequest, object>(
            "app.bsky.notification.registerPush", request, cancellationToken: cancellationToken);
    }
}
