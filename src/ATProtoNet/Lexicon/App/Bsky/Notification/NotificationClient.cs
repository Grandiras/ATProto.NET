using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;

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

    /// <summary>
    /// Stop push notifications to a token that <see cref="RegisterPushAsync"/> registered.
    /// </summary>
    /// <param name="serviceDid">The DID of the push service the token was registered with.</param>
    /// <param name="token">The push token.</param>
    /// <param name="platform">The push platform (see <see cref="PushPlatform"/>).</param>
    /// <param name="appId">The application identifier the token belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task UnregisterPushAsync(
        Did serviceDid,
        string token,
        string platform,
        string appId,
        CancellationToken cancellationToken = default)
    {
        var request = new UnregisterPushRequest
        {
            ServiceDid = serviceDid,
            Token = token,
            Platform = platform,
            AppId = appId,
        };

        return _xrpc.ProcedureAsync(
            "app.bsky.notification.unregisterPush", request, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Preferences
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get the authenticated account's notification preferences.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NotificationPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _xrpc.QueryAsync<GetPreferencesResponse>(
            "app.bsky.notification.getPreferences", cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Preferences;
    }

    /// <summary>
    /// Change some of the authenticated account's notification preferences.
    /// </summary>
    /// <param name="preferences">The preferences to change; the ones left <see langword="null"/> keep their value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All preferences after the change.</returns>
    public async Task<NotificationPreferences> PutPreferencesV2Async(
        PutPreferencesV2Request preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var response = await _xrpc.ProcedureAsync<PutPreferencesV2Response>(
            "app.bsky.notification.putPreferencesV2", preferences, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Preferences;
    }

    // ──────────────────────────────────────────────────────────
    //  Activity subscriptions
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// List one page of the accounts whose activity the authenticated account is subscribed to.
    /// </summary>
    /// <param name="limit">Max accounts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListActivitySubscriptionsResponse> ListActivitySubscriptionsAsync(
        int? limit = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListActivitySubscriptionsResponse>(
            "app.bsky.notification.listActivitySubscriptions", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the accounts whose activity the authenticated account is subscribed to,
    /// fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Accounts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateActivitySubscriptionsAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListActivitySubscriptionsResponse, ProfileView>(
            (cursor, ct) => ListActivitySubscriptionsAsync(pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Subscribe to an account's posts, replies, or both; with both off, unsubscribe.
    /// </summary>
    /// <param name="subject">The account.</param>
    /// <param name="post">Whether to be notified of the account's posts.</param>
    /// <param name="reply">Whether to be notified of the account's replies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subscription as stored.</returns>
    public Task<PutActivitySubscriptionResponse> PutActivitySubscriptionAsync(
        Did subject, bool post, bool reply, CancellationToken cancellationToken = default)
    {
        var request = new PutActivitySubscriptionRequest
        {
            Subject = subject,
            ActivitySubscription = new ActivitySubscription { Post = post, Reply = reply },
        };

        return _xrpc.ProcedureAsync<PutActivitySubscriptionResponse>(
            "app.bsky.notification.putActivitySubscription", request, cancellationToken: cancellationToken);
    }
}
