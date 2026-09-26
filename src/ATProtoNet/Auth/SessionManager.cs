using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Auth;

/// <summary>
/// The session of one <see cref="AtProtoClient"/>: installs it, keeps it refreshed and
/// persisted, and signs it out.
/// </summary>
/// <remarks>
/// <para>The session is one immutable <see cref="State"/>, published through a volatile field.
/// The credentials requests carry are published with the service they belong to as one value
/// (<see cref="XrpcClient.SetSession"/>), so a request never sees half of a change. Transitions
/// (install, refresh, sign-out) are serialized by one lock and publish a new state; nothing is
/// mutated in place.</para>
/// <para>The credentials instance is the session's generation. A refresh is single-flight:
/// callers that saw the same credentials share one refresh, and a caller that finds the
/// credentials replaced by the time it would refresh (another call refreshed, or a new session
/// was installed) refreshes nothing. That is what keeps concurrent callers from spending a
/// single-use refresh token twice. A rejected call is only ever resent with credentials of the
/// same account on the same service.</para>
/// <para>Once a token exchange has started, nothing cancels it but its own time limit, and its
/// result is stored whatever happens to the caller or the client: an authorization server
/// rotates the refresh token as soon as it answers, so an exchange abandoned halfway would leave
/// the store holding a token that is already spent. Cancelling a call, or disposing the client,
/// only stops the waiting.</para>
/// <para>With a <see cref="ISessionRefreshCoordinator"/> and a store, a refresh also runs under
/// the account's lock among every client sharing the store, and starts by reading the store: a
/// session another client has refreshed meanwhile is taken up instead of being exchanged again,
/// and one the store no longer holds was signed out. The result is stored before the lock is
/// released, so the next client reads it.</para>
/// </remarks>
internal sealed class SessionManager : IXrpcSessionHandler
{
    /// <summary>How long before its expiry an access token is refreshed.</summary>
    internal static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);

    /// <summary>The longest a token exchange may take.</summary>
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest single timer delay; a later expiry is reached in steps.</summary>
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(7);

    /// <summary>The first wait before a failed background refresh is retried; it doubles per failure.</summary>
    private static readonly TimeSpan BackgroundRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait between background refresh retries.</summary>
    private static readonly TimeSpan MaxBackgroundRetryDelay = TimeSpan.FromMinutes(10);

    private readonly XrpcClient _xrpc;
    private readonly ServerClient _server;
    private readonly IAtProtoSessionStore? _store;
    private readonly ISessionRefreshCoordinator? _coordinator;
    private readonly Action<AtProtoSessionChangedEventArgs> _raise;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly bool _autoRefresh;
    private readonly bool _backgroundRefresh;

    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();

    private volatile State? _state;
    private volatile bool _disposed;

    // Guarded by _gate.
    private RefreshOperation? _refresh;
    private ITimer? _timer;
    private int _backgroundFailures;

    internal SessionManager(
        XrpcClient xrpc,
        ServerClient server,
        IAtProtoSessionStore? store,
        Action<AtProtoSessionChangedEventArgs> raise,
        ILogger logger,
        TimeProvider time,
        bool autoRefresh,
        bool backgroundRefresh,
        ISessionRefreshCoordinator? coordinator = null)
    {
        _xrpc = xrpc;
        _server = server;
        _store = store;
        _coordinator = store is null ? null : coordinator;
        _raise = raise;
        _logger = logger;
        _time = time;
        _autoRefresh = autoRefresh;
        _backgroundRefresh = backgroundRefresh;
    }

    /// <summary>The installed session, or <see langword="null"/>.</summary>
    internal AtProtoSession? Session => _state?.Session;

    /// <summary>The session store, if the client has one.</summary>
    internal IAtProtoSessionStore? Store => _store;

    // ──────────────────────────────────────────────────────────
    //  Install
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Installs a session, replacing any installed one, and points the client at its service.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="oauthClient">For an OAuth session, the client that refreshes it; when
    /// <see langword="null"/>, the one the previous OAuth session had is kept.</param>
    /// <param name="persist">Write the session to the store.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait for the session lock. Once the session is installed, the store write
    /// completes regardless, so the store and the event agree with the client.
    /// </param>
    /// <param name="key">
    /// For an OAuth session, its DPoP key already loaded, which the manager takes over (and
    /// disposes if the session is refused); <see langword="null"/> imports the session's key.
    /// </param>
    /// <returns>The credentials the installed session publishes.</returns>
    /// <exception cref="ArgumentException">
    /// The session's service endpoint is not an acceptable service URL, or its DPoP key is not a
    /// P-256 PKCS#8 key. The client is left as it was.
    /// </exception>
    internal async Task<XrpcCredentials> InstallAsync(
        AtProtoSession session, OAuthClient? oauthClient, bool persist, CancellationToken cancellationToken,
        DPoPProofGenerator? key = null)
    {
        DPoPProofGenerator? dpop = key;
        var published = false;
        AtProtoSessionChangedEventArgs? change = null;
        XrpcCredentials credentials;

        try
        {
            ArgumentNullException.ThrowIfNull(session);
            ThrowIfDisposed();

            var endpoint = AtProtoHttp.NormalizeBaseUrl(session.ServiceEndpoint);
            if (session is OAuthSession oauth)
                dpop ??= LoadKey(oauth);
            else if (dpop is not null)
                throw new ArgumentException("Only an OAuth session has a DPoP key.", nameof(key));

            await _transition.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                var previous = _state;

                // The only step that can refuse the session, so it runs before anything changes.
                var serviceUrl = endpoint.Equals(_xrpc.ServiceUrl)
                    ? endpoint
                    : AtProtoHttp.ValidateServiceUrl(endpoint, nameof(session));

                var refresher = session is OAuthSession ? oauthClient ?? previous?.OAuthClient : null;
                credentials = Credentials(session, dpop, serviceUrl);
                Publish(new State(session, dpop, refresher, credentials), serviceUrl);
                published = true;

                // The replaced session's key object signs nothing any more; a call of that
                // session still in flight fails instead of signing with it (see XrpcClient).
                previous?.DPoP?.Dispose();

                if (persist)
                    await PersistAsync(session);

                change = new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Created, session, previous?.Session);
            }
            finally
            {
                _transition.Release();
            }
        }
        catch when (!published)
        {
            dpop?.Dispose();
            throw;
        }

        Raise(change);
        return credentials;
    }

    /// <summary>
    /// Replaces the installed session's account details (handle, email, status) with what
    /// <c>getSession</c> reported, keeping its credentials. Applies to password sessions only:
    /// an OAuth session's handle was verified at sign-in, which the service's word does not
    /// improve on.
    /// </summary>
    /// <returns>The session now installed.</returns>
    internal async Task<AtProtoSession?> UpdateAccountAsync(GetSessionResponse account, CancellationToken cancellationToken)
    {
        AtProtoSessionChangedEventArgs? change = null;
        AtProtoSession? current;

        await _transition.WaitAsync(cancellationToken);
        try
        {
            var state = _state;
            current = state?.Session;

            if (state?.Session is PasswordSession password && account.Did == password.Did)
            {
                var updated = password with
                {
                    Handle = account.Handle,
                    Email = account.Email ?? password.Email,
                    EmailConfirmed = account.EmailConfirmed ?? password.EmailConfirmed,
                    EmailAuthFactor = account.EmailAuthFactor ?? password.EmailAuthFactor,
                    Active = account.Active ?? password.Active,
                    Status = account.Active is null ? password.Status : account.Status,
                };

                if (updated != password)
                {
                    // Same credentials instance, so the session's generation does not change.
                    Publish(state with { Session = updated }, serviceUrl: null);
                    await PersistAsync(updated);
                    change = new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Refreshed, updated, password);
                    current = updated;
                }
            }
        }
        finally
        {
            _transition.Release();
        }

        Raise(change);
        return current;
    }

    /// <summary>
    /// Removes the session installed with <paramref name="installed"/> (or a refreshed version of
    /// it) because the service rejected it, as a refused refresh does.
    /// </summary>
    /// <param name="installed">The credentials the session was installed with.</param>
    /// <param name="error">The rejection.</param>
    internal async Task ExpireAsync(XrpcCredentials installed, Exception error)
    {
        AtProtoSessionChangedEventArgs? change = null;

        await _transition.WaitAsync();
        try
        {
            if (_state is { } state && SameSession(state.Credentials, installed))
                change = await ExpireLockedAsync(state, error);
        }
        finally
        {
            _transition.Release();
        }

        Raise(change);
    }

    // ──────────────────────────────────────────────────────────
    //  Refresh
    // ──────────────────────────────────────────────────────────

    /// <summary>Refreshes the installed session now.</summary>
    /// <exception cref="InvalidOperationException">No session is installed.</exception>
    internal Task RefreshAsync(CancellationToken cancellationToken)
    {
        var state = _state ?? throw new InvalidOperationException("No session to refresh. Sign in first.");
        return RefreshAsync(state.Credentials, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask BeforeSendAsync(CancellationToken cancellationToken)
    {
        var state = _state;
        if (!_autoRefresh || state is null || !CanRefresh(state) || state.Session.ExpiresAt is not { } expiresAt)
            return default;

        if (expiresAt - _time.GetUtcNow() > RefreshSkew)
            return default;

        return new ValueTask(RefreshBeforeSendAsync(state, expiresAt, cancellationToken));
    }

    private async Task RefreshBeforeSendAsync(State state, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(state.Credentials, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && _state is not null && expiresAt > _time.GetUtcNow())
        {
            // The token has not expired yet, so the call goes out with it rather than failing
            // over a refresh that may succeed next time; if the service rejects the token, the
            // call refreshes again.
            _logger.LogWarning(ex, "Refreshing the session of {Did} before it expires failed; continuing with the current token",
                state.Session.Did);
        }
    }

    /// <inheritdoc/>
    public async Task<XrpcCredentials?> TryRecoverAsync(XrpcCredentials rejected, CancellationToken cancellationToken)
    {
        var state = _state;
        if (state is null)
            return null;

        // Changed after the call was sent: refreshed by another call, or replaced by a session
        // that may belong to someone else.
        if (!ReferenceEquals(state.Credentials, rejected))
            return SameSession(state.Credentials, rejected) ? state.Credentials : null;

        if (!_autoRefresh || !CanRefresh(state))
            return null;

        await RefreshAsync(rejected, cancellationToken);

        return _state is { } current && !ReferenceEquals(current.Credentials, rejected) &&
               SameSession(current.Credentials, rejected)
            ? current.Credentials
            : null;
    }

    /// <summary>
    /// Whether <paramref name="current"/> are credentials of the account, on the service, that
    /// <paramref name="earlier"/> were: the only credentials a call made with the earlier ones may
    /// be resent with.
    /// </summary>
    private static bool SameSession(XrpcCredentials current, XrpcCredentials earlier) =>
        current.Account is { } account && account == earlier.Account && Equals(current.Service, earlier.Service);

    /// <summary>
    /// Refreshes the session whose generation is <paramref name="observed"/>, joining a refresh
    /// of it already under way. Completes without refreshing when the session has moved on.
    /// </summary>
    private Task RefreshAsync(XrpcCredentials observed, CancellationToken cancellationToken)
    {
        RefreshOperation operation;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (!ReferenceEquals(_state?.Credentials, observed))
                return Task.CompletedTask;

            if (_refresh is { } running && ReferenceEquals(running.Observed, observed))
            {
                operation = running;
            }
            else
            {
                operation = new RefreshOperation(observed);
                _refresh = operation;
                _ = Task.Run(() => RunRefreshAsync(operation));
            }
        }

        return operation.Completion.Task.WaitAsync(cancellationToken);
    }

    private async Task RunRefreshAsync(RefreshOperation operation)
    {
        Exception? failure = null;
        try
        {
            await RefreshCoreAsync(operation.Observed);
        }
        catch (OperationCanceledException ex) when (_lifetime.IsCancellationRequested)
        {
            // Disposed before the refresh got its turn; nothing was exchanged.
            failure = new ObjectDisposedException("The client was disposed before the session could be refreshed.", ex);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        // Retired before it completes, so no caller joins an operation that has already ended.
        lock (_gate)
        {
            if (ReferenceEquals(_refresh, operation))
                _refresh = null;
        }

        if (failure is null)
        {
            operation.Completion.SetResult();
        }
        else
        {
            operation.Completion.SetException(failure);

            // Every caller may have stopped waiting; the failure is theirs to see, not the finalizer's.
            _ = operation.Completion.Task.Exception;
        }
    }

    private async Task RefreshCoreAsync(XrpcCredentials observed)
    {
        // Disposal cancels the wait for a turn, and only that (see the class remarks).
        await _transition.WaitAsync(_lifetime.Token);

        AtProtoSessionChangedEventArgs? change = null;
        IAsyncDisposable? lease = null;
        try
        {
            var state = _state;
            if (state is null || !ReferenceEquals(state.Credentials, observed))
                return;

            if (_coordinator is not null)
            {
                lease = await AcquireRefreshLeaseAsync(state.Session.Did);

                // Read under the lease: what the store holds now is what the last client to
                // refresh this account left there.
                var stored = await ReadStoredAsync(state.Session.Did);
                if (stored is null || stored.Did != state.Session.Did)
                {
                    var signedOut = new XrpcAuthenticationException(
                        XrpcErrors.InvalidToken,
                        "The session was signed out: the session store no longer holds it.",
                        HttpStatusCode.Unauthorized,
                        nsid: null);
                    change = await ExpireLockedAsync(state, signedOut);
                    throw signedOut;
                }

                if (!SameGeneration(stored, state.Session))
                {
                    change = Adopt(state, stored);
                    return;
                }
            }

            AtProtoSession refreshed;
            using (var deadline = new CancellationTokenSource(RefreshTimeout, _time))
            {
                try
                {
                    refreshed = await ExchangeAsync(state, deadline.Token);
                }
                catch (Exception ex) when (IsSessionEnded(ex))
                {
                    change = await ExpireLockedAsync(state, ex);
                    throw;
                }
                catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Refreshing the session did not complete within {RefreshTimeout.TotalSeconds:0} s.", ex);
                }
            }

            // A password session's DID document can name a new PDS (the account migrated). The
            // exchange has already rotated the tokens, so a move that is refused keeps the service.
            var serviceUrl = state.Credentials.Service!;
            if (!refreshed.ServiceEndpoint.Equals(state.Session.ServiceEndpoint))
            {
                try
                {
                    serviceUrl = AtProtoHttp.ValidateServiceUrl(refreshed.ServiceEndpoint, nameof(refreshed));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Not moving the session of {Did} to {Service}", refreshed.Did, refreshed.ServiceEndpoint);
                    refreshed = refreshed with { ServiceEndpoint = state.Session.ServiceEndpoint };
                }
            }

            var moved = !serviceUrl.Equals(state.Credentials.Service);
            Publish(
                state with { Session = refreshed, Credentials = Credentials(refreshed, state.DPoP, serviceUrl) },
                moved ? serviceUrl : null);
            await PersistAsync(refreshed);

            _logger.LogDebug("Refreshed the session of {Did}", refreshed.Did);
            change = new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Refreshed, refreshed, state.Session);
        }
        finally
        {
            if (lease is not null)
                await lease.DisposeAsync();

            _transition.Release();
            Raise(change);
        }
    }

    /// <summary>
    /// Waits for the account's refresh lease: as long as a token exchange may take elsewhere, and
    /// only until this client is disposed.
    /// </summary>
    private async Task<IAsyncDisposable> AcquireRefreshLeaseAsync(Did did)
    {
        using var deadline = new CancellationTokenSource(RefreshTimeout, _time);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, _lifetime.Token);
        try
        {
            return await _coordinator!.AcquireAsync(did, wait.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Another client did not finish refreshing the session within {RefreshTimeout.TotalSeconds:0} s.", ex);
        }
    }

    /// <summary>Reads the account's session from the store, within the refresh time limit.</summary>
    private async Task<AtProtoSession?> ReadStoredAsync(Did did)
    {
        using var deadline = new CancellationTokenSource(RefreshTimeout, _time);
        try
        {
            return await _store!.GetAsync(did, deadline.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Reading the stored session did not complete within {RefreshTimeout.TotalSeconds:0} s.", ex);
        }
    }

    /// <summary>
    /// Whether <paramref name="stored"/> carries the same tokens as <paramref name="current"/>:
    /// no other client has refreshed or replaced the session since this one read it.
    /// </summary>
    private static bool SameGeneration(AtProtoSession stored, AtProtoSession current) =>
        stored.GetType() == current.GetType() &&
        string.Equals(stored.AccessCredential, current.AccessCredential, StringComparison.Ordinal) &&
        string.Equals(RefreshCredential(stored), RefreshCredential(current), StringComparison.Ordinal);

    /// <summary>
    /// Takes up the session another client stored for this account, with the transition lock
    /// held, instead of refreshing: its refresh token is the live one, and this client's copy is
    /// already spent.
    /// </summary>
    private AtProtoSessionChangedEventArgs Adopt(State state, AtProtoSession stored)
    {
        // The same grant keeps its key; a new sign-in of the account brings its own.
        var dpop = stored switch
        {
            OAuthSession oauth when state.Session is OAuthSession current && current.DPoPKey.Span.SequenceEqual(oauth.DPoPKey.Span)
                => state.DPoP,
            OAuthSession oauth => LoadKey(oauth),
            _ => null,
        };

        var serviceUrl = state.Credentials.Service!;
        if (!stored.ServiceEndpoint.Equals(state.Session.ServiceEndpoint))
        {
            try
            {
                serviceUrl = AtProtoHttp.ValidateServiceUrl(AtProtoHttp.NormalizeBaseUrl(stored.ServiceEndpoint), nameof(stored));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Not moving the session of {Did} to {Service}", stored.Did, stored.ServiceEndpoint);
            }
        }

        var moved = !serviceUrl.Equals(state.Credentials.Service);
        Publish(
            state with { Session = stored, DPoP = dpop, Credentials = Credentials(stored, dpop, serviceUrl) },
            moved ? serviceUrl : null);

        if (!ReferenceEquals(dpop, state.DPoP))
            state.DPoP?.Dispose();

        _logger.LogDebug("Took up the session of {Did} another client refreshed", stored.Did);
        return new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Refreshed, stored, state.Session);
    }

    /// <summary>The token that refreshes a session, which identifies its generation.</summary>
    private static string? RefreshCredential(AtProtoSession session) => session switch
    {
        PasswordSession password => password.RefreshJwt,
        OAuthSession oauth => oauth.RefreshToken ?? oauth.AccessToken,
        _ => null,
    };

    private async Task<AtProtoSession> ExchangeAsync(State state, CancellationToken cancellationToken)
    {
        switch (state.Session)
        {
            case PasswordSession password:
            {
                var response = await _server.RefreshSessionAsync(password.RefreshJwt, cancellationToken);
                return password with
                {
                    Handle = response.Handle,
                    AccessJwt = response.AccessJwt,
                    RefreshJwt = response.RefreshJwt,
                    ExpiresAt = ReadJwtExpiry(response.AccessJwt),
                    ServiceEndpoint = ResolveServiceEndpoint(response.DidDoc, password.Did, password.ServiceEndpoint),
                    Email = response.Email ?? password.Email,
                    EmailConfirmed = response.EmailConfirmed ?? password.EmailConfirmed,
                    EmailAuthFactor = response.EmailAuthFactor ?? password.EmailAuthFactor,
                    Active = response.Active ?? password.Active,
                    Status = response.Active is null ? password.Status : response.Status,
                };
            }

            case OAuthSession oauth:
            {
                var client = state.OAuthClient ?? throw new InvalidOperationException(
                    "An OAuth session is refreshed by the OAuthClient that issued it, and none was given. " +
                    "Pass it to ApplySessionAsync, ResumeSessionAsync or TryRestoreSessionAsync.");

                return await client.RefreshAsync(oauth, state.DPoP!, cancellationToken);
            }

            default:
                throw new NotSupportedException($"Unknown session type {state.Session.GetType().Name}.");
        }
    }

    /// <summary>
    /// Whether a refresh failure means the refresh token itself was refused, so no later
    /// attempt can succeed: the PDS rejecting the refresh JWT, or the authorization server
    /// answering <c>invalid_grant</c> (expired, revoked or already used).
    /// </summary>
    private static bool IsSessionEnded(Exception exception) => exception switch
    {
        XrpcAuthenticationException => true,
        OAuthException { Error: "invalid_grant" } => true,
        _ => false,
    };

    private static bool CanRefresh(State state) =>
        state.Session.CanRefresh && (state.Session is not OAuthSession || state.OAuthClient is not null);

    // ──────────────────────────────────────────────────────────
    //  Expiry and sign-out
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Drops a session the service no longer accepts, with the transition lock held: from the
    /// client, and from the store if the store still holds this copy.
    /// </summary>
    private async Task<AtProtoSessionChangedEventArgs> ExpireLockedAsync(State state, Exception error)
    {
        _logger.LogWarning(error, "The session of {Did} is no longer accepted and has expired", state.Session.Did);

        Publish(null, serviceUrl: null);
        state.DPoP?.Dispose();
        await ForgetAsync(state.Session);

        return new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Expired, null, state.Session, error);
    }

    /// <summary>
    /// Signs the session out: drops it from the client and the store first, then revokes it
    /// at the service — <c>deleteSession</c> with the refresh JWT, or RFC 7009 revocation for
    /// an OAuth session — and rethrows what failed.
    /// </summary>
    internal async Task LogoutAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var failures = new List<Exception>(2);
        State? state;

        await _transition.WaitAsync(cancellationToken);
        try
        {
            state = _state;
            if (state is null)
                return;

            _logger.LogInformation("Signing out {Did}", state.Session.Did);

            // Local teardown first: whatever the service says, no request carries these
            // credentials again and the store no longer holds them. The store removal is not
            // cancelled with the call, so the credentials do not outlive the sign-out.
            Publish(null, serviceUrl: null);

            if (_store is not null)
            {
                try
                {
                    await _store.RemoveAsync(state.Session.Did, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove the stored session of {Did}", state.Session.Did);
                    failures.Add(ex);
                }
            }

            try
            {
                await RevokeAsync(state, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not revoke the session of {Did} at the service", state.Session.Did);
                failures.Add(ex);
            }
            finally
            {
                state.DPoP?.Dispose();
            }
        }
        finally
        {
            _transition.Release();
        }

        Raise(new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Removed, null, state.Session));

        if (failures.Count == 1)
            ExceptionDispatchInfo.Throw(failures[0]);
        if (failures.Count > 1)
            throw new AggregateException("Signing out failed at the service and in the session store.", failures);
    }

    private async Task RevokeAsync(State state, CancellationToken cancellationToken)
    {
        switch (state.Session)
        {
            case PasswordSession { RefreshJwt.Length: > 0 } password:
                try
                {
                    await _server.DeleteSessionAsync(password.RefreshJwt, cancellationToken);
                }
                catch (XrpcAuthenticationException ex)
                {
                    // The refresh JWT is already expired or revoked: there is nothing left to end.
                    _logger.LogDebug(ex, "The PDS no longer accepts the refresh token of {Did}", password.Did);
                }
                break;

            case OAuthSession oauth when state.OAuthClient is { } client:
                await client.RevokeAsync(oauth, state.DPoP!, cancellationToken);
                break;

            case OAuthSession:
                throw new InvalidOperationException(
                    "The OAuth session was signed out locally but not revoked at its authorization server: " +
                    "revoking needs the OAuthClient that issued it, and none was given.");
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Publication, persistence, events
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Makes <paramref name="next"/> the session: first the state that refreshes and recoveries
    /// compare against, then the credentials requests carry together with their service, then
    /// the background refresh schedule.
    /// </summary>
    /// <param name="next">The new state, or <see langword="null"/> to remove the session.</param>
    /// <param name="serviceUrl">The service to move to, validated; <see langword="null"/> stays.</param>
    private void Publish(State? next, Uri? serviceUrl)
    {
        // A call reads the credentials after they are published, and by then this is the state
        // it is compared against, so it can never be resent with the previous generation.
        _state = next;
        _xrpc.SetSession(serviceUrl, next?.Credentials);
        ScheduleBackgroundRefresh(next);
    }

    /// <summary>
    /// Writes a session to the store. Not cancellable: it runs after the session has changed,
    /// and a store left behind the client would hand a spent token to the next reader.
    /// </summary>
    private async Task PersistAsync(AtProtoSession session)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SetAsync(session, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The session in memory is valid and stays installed; the store keeps the older copy
            // until the next successful write.
            _logger.LogError(ex, "Could not persist the session of {Did}", session.Did);
        }
    }

    /// <summary>
    /// Removes a refused session from the store, if the store still holds that copy of it.
    /// </summary>
    /// <remarks>
    /// Another client sharing the store may have refreshed the same session first, stored the
    /// new tokens, and so caused this client's refusal; removing the entry then would sign out a
    /// session that is valid. A <see cref="ISessionRefreshCoordinator"/> keeps that from
    /// happening among the clients it coordinates.
    /// </remarks>
    private async Task ForgetAsync(AtProtoSession refused)
    {
        if (_store is null)
            return;

        try
        {
            var stored = await _store.GetAsync(refused.Did, CancellationToken.None);
            if (stored is not null && string.Equals(RefreshCredential(stored), RefreshCredential(refused), StringComparison.Ordinal))
                await _store.RemoveAsync(refused.Did, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not remove the expired session of {Did} from the store", refused.Did);
        }
    }

    private void Raise(AtProtoSessionChangedEventArgs? change)
    {
        if (change is null)
            return;

        try
        {
            _raise(change);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A SessionChanged handler threw");
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Background refresh
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Arms the timer for <paramref name="state"/>: shortly before its expiry, or after
    /// <paramref name="retryIn"/> when a background refresh failed.
    /// </summary>
    private void ScheduleBackgroundRefresh(State? state, TimeSpan? retryIn = null)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;

            if (retryIn is null)
                _backgroundFailures = 0;

            if (!_backgroundRefresh || !_autoRefresh || _disposed || state is null || !CanRefresh(state) ||
                state.Session.ExpiresAt is not { } expiresAt)
            {
                return;
            }

            var due = retryIn ?? expiresAt - RefreshSkew - _time.GetUtcNow();
            due = due < TimeSpan.Zero ? TimeSpan.Zero : due > MaxTimerDelay ? MaxTimerDelay : due;

            _timer = _time.CreateTimer(_ => _ = OnTimerAsync(state), null, due, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task OnTimerAsync(State state)
    {
        if (_disposed || !ReferenceEquals(_state, state))
            return;

        // A long-lived token is reached in steps of MaxTimerDelay.
        if (state.Session.ExpiresAt is { } expiresAt && expiresAt - _time.GetUtcNow() > RefreshSkew)
        {
            ScheduleBackgroundRefresh(state);
            return;
        }

        try
        {
            await RefreshAsync(state.Credentials, _lifetime.Token);
        }
        catch (Exception) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh of the session of {Did} failed", state.Session.Did);

            // A transient failure leaves the session installed; try again, backing off. (A refused
            // refresh token removed the session, and there is nothing to retry.)
            if (ReferenceEquals(_state, state))
            {
                TimeSpan delay;
                lock (_gate)
                {
                    _backgroundFailures++;
                    var factor = Math.Pow(2, Math.Min(_backgroundFailures - 1, 16));
                    delay = TimeSpan.FromTicks((long)Math.Min(
                        BackgroundRetryDelay.Ticks * factor, MaxBackgroundRetryDelay.Ticks));
                }

                ScheduleBackgroundRefresh(state, delay);
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Lifetime
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Stops refreshing without waiting. A token exchange already under way finishes and is
    /// stored in the background; the DPoP key is released once it is done with it.
    /// </summary>
    internal void Dispose()
    {
        if (!TryBeginDispose(out var timer, out var refresh))
            return;

        timer?.Dispose();

        if (_state?.DPoP is { } key)
        {
            if (refresh is null || refresh.IsCompleted)
                key.Dispose();
            else
                refresh.ContinueWith(_ => key.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Stops refreshing, and waits for a token exchange already under way to finish and be
    /// stored (at most the exchange's time limit, plus the store write).
    /// </summary>
    internal async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose(out var timer, out var refresh))
            return;

        if (timer is not null)
            await timer.DisposeAsync();

        if (refresh is not null)
        {
            try
            {
                await refresh;
            }
            catch
            {
                // Its callers see the failure; disposal only waits for the end.
            }
        }

        _state?.DPoP?.Dispose();
    }

    private bool TryBeginDispose(out ITimer? timer, out Task? refresh)
    {
        lock (_gate)
        {
            timer = _timer;
            refresh = _refresh?.Completion.Task;

            if (_disposed)
                return false;

            _disposed = true;
            _timer = null;
        }

        _lifetime.Cancel();
        return true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, typeof(AtProtoClient));

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static XrpcCredentials Credentials(AtProtoSession session, DPoPProofGenerator? dpop, Uri serviceUrl) =>
        new(session.AccessCredential, RefreshToken: null, dpop) { Account = session.Did, Service = serviceUrl };

    private static DPoPProofGenerator LoadKey(OAuthSession session)
    {
        try
        {
            return new DPoPProofGenerator(session.DPoPKey.ToArray());
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new ArgumentException("The session's DPoP key is not a PKCS#8 private key.", nameof(session), ex);
        }
    }

    /// <summary>
    /// Reads the <c>exp</c> claim of a JWT without verifying it, for scheduling a refresh.
    /// </summary>
    /// <returns>The expiry, or <see langword="null"/> for a token that is not a JWT or has none.</returns>
    internal static DateTimeOffset? ReadJwtExpiry(string token)
    {
        if (!Jwt.TryDecode(token, out var jwt, out _) ||
            !jwt.Payload.TryGetProperty("exp", out var exp) ||
            exp.ValueKind != JsonValueKind.Number ||
            !exp.TryGetInt64(out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The service a password session should use after <c>createSession</c> or
    /// <c>refreshSession</c>: the <c>#atproto_pds</c> endpoint of the DID document the service
    /// returned, when it is the account's own document and an HTTPS URL, and
    /// <paramref name="current"/> otherwise.
    /// </summary>
    /// <remarks>
    /// This is how signing in through an entryway (<c>bsky.social</c>) lands the session on the
    /// account's PDS, as the reference client does. The move is only between HTTPS services: a
    /// development PDS reached over plain HTTP publishes the address it believes it has, which
    /// is usually not the one this process reaches it at.
    /// </remarks>
    internal static Uri ResolveServiceEndpoint(object? didDoc, Did did, Uri current)
    {
        if (didDoc is not JsonElement { ValueKind: JsonValueKind.Object } document ||
            document.GetStringOrNull("id") != did.Value ||
            !document.TryGetProperty("service", out var services) ||
            services.ValueKind != JsonValueKind.Array ||
            current.Scheme != Uri.UriSchemeHttps)
        {
            return current;
        }

        foreach (var service in services.EnumerateArray())
        {
            var id = service.GetStringOrNull("id");
            if ((id == "#atproto_pds" || id == did.Value + "#atproto_pds") &&
                service.GetStringOrNull("type") == "AtprotoPersonalDataServer")
            {
                return AtProtoHttp.TryNormalizeBaseUrl(service.GetStringOrNull("serviceEndpoint"), out var pds) &&
                       pds.Scheme == Uri.UriSchemeHttps
                    ? pds
                    : current;
            }
        }

        return current;
    }

    /// <summary>
    /// The installed session, with the key object and refresher that belong to it and the
    /// credentials derived from it.
    /// </summary>
    /// <param name="Session">The session.</param>
    /// <param name="DPoP">The session's DPoP key, owned by the client, for an OAuth session.</param>
    /// <param name="OAuthClient">The client that refreshes an OAuth session; not owned.</param>
    /// <param name="Credentials">What requests carry, and the session's generation.</param>
    private sealed record State(
        AtProtoSession Session, DPoPProofGenerator? DPoP, OAuthClient? OAuthClient, XrpcCredentials Credentials)
    {
        /// <summary>The session's kind, DID and handle; never its credentials.</summary>
        public override string ToString() => $"State {{ Session = {Session} }}";
    }

    /// <summary>One token exchange, shared by every caller that saw the same credentials.</summary>
    private sealed class RefreshOperation(XrpcCredentials observed)
    {
        public XrpcCredentials Observed { get; } = observed;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
