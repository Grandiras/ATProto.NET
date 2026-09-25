using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Notification;

/// <summary>
/// Client for app.bsky.notification.* XRPC endpoints.
/// </summary>
public sealed class NotificationClient
{
    private readonly XrpcClient _xrpc;

    internal NotificationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// List one page of notifications for the authenticated user.
    /// </summary>
    /// <param name="reasons">
    /// Only notifications with one of these reasons (see <see cref="NotificationReasons"/>);
    /// <see langword="null"/> for all.
    /// </param>
    /// <param name="limit">Max notifications per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListNotificationsResponse> ListNotificationsAsync(
        IEnumerable<string>? reasons = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("reasons", reasons)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListNotificationsResponse>(
            "app.bsky.notification.listNotifications", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the authenticated user's notifications, fetching pages as needed.
    /// </summary>
    /// <param name="reasons">
    /// Only notifications with one of these reasons (see <see cref="NotificationReasons"/>);
    /// <see langword="null"/> for all.
    /// </param>
    /// <param name="pageSize">Notifications per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<NotificationView> EnumerateNotificationsAsync(
        IEnumerable<string>? reasons = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListNotificationsResponse, NotificationView>(
            (cursor, ct) => ListNotificationsAsync(reasons, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get the count of unread notifications.
    /// </summary>
    /// <param name="seenAt">Count notifications newer than this timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetUnreadCountResponse> GetUnreadCountAsync(
        AtDatetime? seenAt = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("seenAt", seenAt?.ToString());

        return _xrpc.QueryAsync<GetUnreadCountResponse>(
            "app.bsky.notification.getUnreadCount", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mark notifications as seen up to the given timestamp.
    /// </summary>
    /// <param name="seenAt">When the user last viewed notifications. Pass
    /// <see cref="AtDatetime.Now"/> to mark all as read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateSeenAsync(
        AtDatetime seenAt, CancellationToken cancellationToken = default)
    {
        var request = new UpdateSeenRequest { SeenAt = seenAt };
        await _xrpc.ProcedureAsync(
            "app.bsky.notification.updateSeen", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mark all notifications as read (convenience method).
    /// </summary>
    public Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        return UpdateSeenAsync(AtDatetime.Now(), cancellationToken);
    }

    /// <summary>
    /// Register a push notification token.
    /// </summary>
    public async Task RegisterPushAsync(
        RegisterPushRequest request, CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "app.bsky.notification.registerPush", request, cancellationToken: cancellationToken);
    }
}
