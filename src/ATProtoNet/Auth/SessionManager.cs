using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Auth;

// The session of one AtProtoClient: installs it, keeps it refreshed and persisted, and signs it out.
//
// The session is one immutable State, published through a volatile field. The credentials requests
// carry are published with the service they belong to as one value (XrpcClient.SetSession), so a
// request never sees half of a change. Transitions (install, refresh, sign-out) are serialized by one
// lock and publish a new state; nothing is mutated in place.
//
// The credentials instance is the session's generation. A refresh is single-flight: callers that saw
// the same credentials share one refresh, and a caller that finds the credentials replaced by the time
// it would refresh (another call refreshed, or a new session was installed) refreshes nothing. That is
// what keeps concurrent callers from spending a single-use refresh token twice. A rejected call is
// only ever resent with credentials of the same account on the same service.
//
// Once a token exchange has started, nothing cancels it but its own time limit, and its result is
// stored whatever happens to the caller or the client: an authorization server rotates the refresh
// token as soon as it answers, so an exchange abandoned halfway would leave the store holding a token
// that is already spent. Cancelling a call, or disposing the client, only stops the waiting.
//
// With a ISessionRefreshCoordinator and a store, a refresh also runs under the account's lock among
// every client sharing the store, and starts by reading the store: a session another client has
// refreshed meanwhile is taken up instead of being exchanged again, and one the store no longer holds
// was signed out. The result is stored before the lock is released, so the next client reads it. Every
// other store change (a session installed, its account details updated, an expired one forgotten, a
// sign-out) takes the same lock, so none interleaves with a refresh: a sign-out waits for a refresh
// under way and then removes its result too, instead of the refresh writing the session back
// afterwards.
internal sealed class SessionManager : IXrpcSessionHandler
{
    // How long before its expiry an access token is refreshed.
    internal static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);

    // The longest a token exchange may take.
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    // The longest single timer delay; a later expiry is reached in steps.
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(7);

    // The first wait before a failed background refresh is retried; it doubles per failure.
    private static readonly TimeSpan BackgroundRetryDelay = TimeSpan.FromSeconds(30);

    // The longest wait between background refresh retries.
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

    // The installed session, or null.
    internal AtProtoSession? Session => _state?.Session;

    // The session store, if the client has one.
    internal IAtProtoSessionStore? Store => _store;

    // ── Install ──────────────────────────────────────────────

    // Installs a session, replacing any installed one, and points the client at its service.
    //
    // oauthClient: For an OAuth session, the client that refreshes it; when null, the one the previous
    // OAuth session had is kept.
    //
    // persist: Write the session to the store.
    //
    // cancellationToken: Cancels the wait for the session lock. Once the session is installed, the store
    // write completes regardless, so the store and the event agree with the client.
    //
    // key: For an OAuth session, its DPoP key already loaded, which the manager takes over (and disposes
    // if the session is refused); null imports the session's key.
    //
    // Returns: The credentials the installed session publishes.
    //
    // Throws ArgumentException: The session's service endpoint is not an acceptable service URL, or its
    // DPoP key is not a P-256 PKCS#8 key. The client is left as it was.
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

            await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
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

                // Under the account's lease, so a refresh of the account's previous session by
                // another client cannot write its result over this one afterwards.
                if (persist)
                {
                    await using (await AcquireStoreLeaseAsync(session.Did).ConfigureAwait(false))
                        await PersistAsync(session).ConfigureAwait(false);
                }

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

    // Replaces the installed session's account details (handle, email, status) with what getSession
    // reported, keeping its credentials. Applies to password sessions only: an OAuth session's handle was
    // verified at sign-in, which the service's word does not improve on.
    //
    // Returns: The session now installed.
    internal async Task<AtProtoSession?> UpdateAccountAsync(GetSessionResponse account, CancellationToken cancellationToken)
    {
        AtProtoSessionChangedEventArgs? change = null;
        AtProtoSession? current;

        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
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

                    // The store is written only while it still holds these tokens: another client
                    // may have refreshed the session, or signed it out, since this one read it, and
                    // new account details are not worth undoing either.
                    await using (await AcquireStoreLeaseAsync(updated.Did).ConfigureAwait(false))
                    {
                        if (_coordinator is null ||
                            await ReadStoredQuietlyAsync(updated.Did).ConfigureAwait(false) is { } stored && SameGeneration(stored, password))
                        {
                            await PersistAsync(updated).ConfigureAwait(false);
                        }
                    }

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

    // Removes the session installed with installed (or a refreshed version of it) because the service
    // rejected it, as a refused refresh does.
    //
    // installed: The credentials the session was installed with.
    //
    // error: The rejection.
    internal async Task ExpireAsync(XrpcCredentials installed, Exception error)
    {
        AtProtoSessionChangedEventArgs? change = null;

        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is { } state && SameSession(state.Credentials, installed))
                change = await ExpireLockedAsync(state, error, leaseHeld: false).ConfigureAwait(false);
        }
        finally
        {
            _transition.Release();
        }

        Raise(change);
    }

    // ── Refresh ──────────────────────────────────────────────

    // Refreshes the installed session now.
    //
    // Throws InvalidOperationException: No session is installed.
    internal Task RefreshAsync(CancellationToken cancellationToken)
    {
        var state = _state ?? throw new InvalidOperationException("No session to refresh. Sign in first.");
        return RefreshAsync(state.Credentials, cancellationToken);
    }

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
            await RefreshAsync(state.Credentials, cancellationToken).ConfigureAwait(false);
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

        await RefreshAsync(rejected, cancellationToken).ConfigureAwait(false);

        return _state is { } current && !ReferenceEquals(current.Credentials, rejected) &&
               SameSession(current.Credentials, rejected)
            ? current.Credentials
            : null;
    }

    // Whether current are credentials of the account, on the service, that earlier were: the only
    // credentials a call made with the earlier ones may be resent with.
    private static bool SameSession(XrpcCredentials current, XrpcCredentials earlier) =>
        current.Account is { } account && account == earlier.Account && Equals(current.Service, earlier.Service);

    // Refreshes the session whose generation is observed, joining a refresh of it already under way.
    // Completes without refreshing when the session has moved on.
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
            await RefreshCoreAsync(operation.Observed).ConfigureAwait(false);
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
        await _transition.WaitAsync(_lifetime.Token).ConfigureAwait(false);

        AtProtoSessionChangedEventArgs? change = null;
        IAsyncDisposable? lease = null;
        try
        {
            var state = _state;
            if (state is null || !ReferenceEquals(state.Credentials, observed))
                return;

            if (_coordinator is not null)
            {
                lease = await AcquireRefreshLeaseAsync(state.Session.Did).ConfigureAwait(false);

                // Read under the lease: what the store holds now is what the last client to
                // refresh this account left there.
                var stored = await ReadStoredAsync(state.Session.Did).ConfigureAwait(false);
                if (stored is null || stored.Did != state.Session.Did)
                {
                    var signedOut = new XrpcAuthenticationException(
                        XrpcErrors.InvalidToken,
                        "The session was signed out: the session store no longer holds it.",
                        HttpStatusCode.Unauthorized,
                        nsid: null);
                    change = await ExpireLockedAsync(state, signedOut, leaseHeld: true).ConfigureAwait(false);
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
                    refreshed = await ExchangeAsync(state, deadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsSessionEnded(ex))
                {
                    change = await ExpireLockedAsync(state, ex, leaseHeld: lease is not null).ConfigureAwait(false);
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
            await PersistAsync(refreshed).ConfigureAwait(false);

            _logger.LogDebug("Refreshed the session of {Did}", refreshed.Did);
            change = new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Refreshed, refreshed, state.Session);
        }
        finally
        {
            if (lease is not null)
                await lease.DisposeAsync().ConfigureAwait(false);

            _transition.Release();
            Raise(change);
        }
    }

    // Waits for the account's refresh lease: as long as a token exchange may take elsewhere, and only
    // until this client is disposed.
    private async Task<IAsyncDisposable> AcquireRefreshLeaseAsync(Did did)
    {
        using var deadline = new CancellationTokenSource(RefreshTimeout, _time);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, _lifetime.Token);
        try
        {
            return await _coordinator!.AcquireAsync(did, wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Another client did not finish refreshing the session within {RefreshTimeout.TotalSeconds:0} s.", ex);
        }
    }

    // Waits for the account's lease before the store is changed outside a refresh (a session installed,
    // updated, expired or signed out), so the change and another client's refresh do not interleave: a
    // refresh under way stores its result first. Returns at once without a coordinator.
    //
    // Not cancelled, since the store change it guards completes regardless. After the refresh time limit
    // the change goes ahead without the lease, as it would with no coordinator.
    private async Task<IAsyncDisposable> AcquireStoreLeaseAsync(Did did)
    {
        if (_coordinator is null)
            return NoLease.Instance;

        using var deadline = new CancellationTokenSource(RefreshTimeout, _time);
        try
        {
            return await _coordinator.AcquireAsync(did, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Another client held the session of {Did} for {Seconds:0} s; changing the store without waiting further",
                did, RefreshTimeout.TotalSeconds);
            return NoLease.Instance;
        }
    }

    // Reads the account's session from the store, or null when that fails.
    private async Task<AtProtoSession?> ReadStoredQuietlyAsync(Did did)
    {
        try
        {
            return await ReadStoredAsync(did).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the stored session of {Did}", did);
            return null;
        }
    }

    // Whether stored is a later copy of current that another client stored after refreshing it: the same
    // account and kind, other tokens, and for an OAuth session the same grant (its DPoP key).
    private static bool IsLaterCopy(AtProtoSession stored, AtProtoSession current) =>
        stored.Did == current.Did &&
        !SameGeneration(stored, current) &&
        (stored, current) switch
        {
            (OAuthSession later, OAuthSession earlier) => later.DPoPKey.Span.SequenceEqual(earlier.DPoPKey.Span),
            (PasswordSession, PasswordSession) => true,
            _ => false,
        };

    // Reads the account's session from the store, within the refresh time limit.
    private async Task<AtProtoSession?> ReadStoredAsync(Did did)
    {
        using var deadline = new CancellationTokenSource(RefreshTimeout, _time);
        try
        {
            return await _store!.GetAsync(did, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Reading the stored session did not complete within {RefreshTimeout.TotalSeconds:0} s.", ex);
        }
    }

    // Whether stored carries the same tokens as current: no other client has refreshed or replaced the
    // session since this one read it.
    private static bool SameGeneration(AtProtoSession stored, AtProtoSession current) =>
        stored.GetType() == current.GetType() &&
        string.Equals(stored.AccessCredential, current.AccessCredential, StringComparison.Ordinal) &&
        string.Equals(RefreshCredential(stored), RefreshCredential(current), StringComparison.Ordinal);

    // Takes up the session another client stored for this account, with the transition lock held, instead
    // of refreshing: its refresh token is the live one, and this client's copy is already spent.
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

    // The token that refreshes a session, which identifies its generation.
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
                var response = await _server.RefreshSessionAsync(password.RefreshJwt, cancellationToken).ConfigureAwait(false);
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

                return await client.RefreshAsync(oauth, state.DPoP!, cancellationToken).ConfigureAwait(false);
            }

            default:
                throw new NotSupportedException($"Unknown session type {state.Session.GetType().Name}.");
        }
    }

    // Whether a refresh failure means the refresh token itself was refused, so no later attempt can
    // succeed: the PDS rejecting the refresh JWT, or the authorization server answering invalid_grant
    // (expired, revoked or already used).
    private static bool IsSessionEnded(Exception exception) => exception switch
    {
        XrpcAuthenticationException => true,
        OAuthException { Error: "invalid_grant" } => true,
        _ => false,
    };

    private static bool CanRefresh(State state) =>
        state.Session.CanRefresh && (state.Session is not OAuthSession || state.OAuthClient is not null);

    // ── Expiry and sign-out ──────────────────────────────────

    // Drops a session the service no longer accepts, with the transition lock held: from the client, and
    // from the store if the store still holds this copy.
    //
    // state: The session to drop.
    //
    // error: Why it is dropped.
    //
    // leaseHeld: Whether the caller holds the account's refresh lease already.
    private async Task<AtProtoSessionChangedEventArgs> ExpireLockedAsync(State state, Exception error, bool leaseHeld)
    {
        _logger.LogWarning(error, "The session of {Did} is no longer accepted and has expired", state.Session.Did);

        Publish(null, serviceUrl: null);
        state.DPoP?.Dispose();

        if (leaseHeld)
        {
            await ForgetAsync(state.Session).ConfigureAwait(false);
        }
        else
        {
            await using (await AcquireStoreLeaseAsync(state.Session.Did).ConfigureAwait(false))
                await ForgetAsync(state.Session).ConfigureAwait(false);
        }

        return new AtProtoSessionChangedEventArgs(AtProtoSessionChange.Expired, null, state.Session, error);
    }

    // Signs the session out: drops it from the client and the store first, then revokes it at the service
    // — deleteSession with the refresh JWT, or RFC 7009 revocation for an OAuth session — and rethrows
    // what failed.
    internal async Task LogoutAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var failures = new List<Exception>(2);
        State? state;

        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
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

            // What is revoked: this client's session, or the later copy of it another client
            // stored after refreshing it, whose tokens are the live ones.
            var revoked = state;
            if (_store is not null)
            {
                try
                {
                    // Under the account's lease: a refresh by another client finishes and stores
                    // its result first, and is then removed with the rest, instead of writing the
                    // session back once it has been signed out.
                    await using (await AcquireStoreLeaseAsync(state.Session.Did).ConfigureAwait(false))
                    {
                        if (_coordinator is not null && await ReadStoredQuietlyAsync(state.Session.Did).ConfigureAwait(false) is { } stored &&
                            IsLaterCopy(stored, state.Session))
                        {
                            revoked = state with { Session = stored };
                        }

                        await _store.RemoveAsync(state.Session.Did, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove the stored session of {Did}", state.Session.Did);
                    failures.Add(ex);
                }
            }

            try
            {
                await RevokeAsync(revoked, cancellationToken).ConfigureAwait(false);
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
                    await _server.DeleteSessionAsync(password.RefreshJwt, cancellationToken).ConfigureAwait(false);
                }
                catch (XrpcAuthenticationException ex)
                {
                    // The refresh JWT is already expired or revoked: there is nothing left to end.
                    _logger.LogDebug(ex, "The PDS no longer accepts the refresh token of {Did}", password.Did);
                }
                break;

            case OAuthSession oauth when state.OAuthClient is { } client:
                await client.RevokeAsync(oauth, state.DPoP!, cancellationToken).ConfigureAwait(false);
                break;

            case OAuthSession:
                throw new InvalidOperationException(
                    "The OAuth session was signed out locally but not revoked at its authorization server: " +
                    "revoking needs the OAuthClient that issued it, and none was given.");
        }
    }

    // ── Publication, persistence, events ─────────────────────

    // Makes next the session: first the state that refreshes and recoveries compare against, then the
    // credentials requests carry together with their service, then the background refresh schedule.
    //
    // next: The new state, or null to remove the session.
    //
    // serviceUrl: The service to move to, validated; null stays.
    private void Publish(State? next, Uri? serviceUrl)
    {
        // A call reads the credentials after they are published, and by then this is the state
        // it is compared against, so it can never be resent with the previous generation.
        _state = next;
        _xrpc.SetSession(serviceUrl, next?.Credentials);
        ScheduleBackgroundRefresh(next);
    }

    // Writes a session to the store. Not cancellable: it runs after the session has changed, and a store
    // left behind the client would hand a spent token to the next reader.
    private async Task PersistAsync(AtProtoSession session)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SetAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The session in memory is valid and stays installed; the store keeps the older copy
            // until the next successful write.
            _logger.LogError(ex, "Could not persist the session of {Did}", session.Did);
        }
    }

    // Removes a refused session from the store, if the store still holds that copy of it.
    //
    // Another client sharing the store may have refreshed the same session first, stored the new tokens,
    // and so caused this client's refusal; removing the entry then would sign out a session that is valid.
    // A ISessionRefreshCoordinator keeps that from happening among the clients it coordinates.
    private async Task ForgetAsync(AtProtoSession refused)
    {
        if (_store is null)
            return;

        try
        {
            var stored = await _store.GetAsync(refused.Did, CancellationToken.None).ConfigureAwait(false);
            if (stored is not null && string.Equals(RefreshCredential(stored), RefreshCredential(refused), StringComparison.Ordinal))
                await _store.RemoveAsync(refused.Did, CancellationToken.None).ConfigureAwait(false);
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

    // ── Background refresh ───────────────────────────────────

    // Arms the timer for state: shortly before its expiry, or after retryIn when a background refresh
    // failed.
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
            await RefreshAsync(state.Credentials, _lifetime.Token).ConfigureAwait(false);
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

    // ── Lifetime ─────────────────────────────────────────────

    // Stops refreshing without waiting. A token exchange already under way finishes and is stored in the
    // background; the DPoP key is released once it is done with it.
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

    // Stops refreshing, and waits for a token exchange already under way to finish and be stored (at most
    // the exchange's time limit, plus the store write).
    internal async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose(out var timer, out var refresh))
            return;

        if (timer is not null)
            await timer.DisposeAsync().ConfigureAwait(false);

        if (refresh is not null)
        {
            try
            {
                await refresh.ConfigureAwait(false);
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

    // ── Helpers ──────────────────────────────────────────────

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

    // Reads the exp claim of a JWT without verifying it, for scheduling a refresh.
    //
    // Returns: The expiry, or null for a token that is not a JWT or has none.
    internal static DateTimeOffset? ReadJwtExpiry(string token) =>
        Jwt.TryDecode(token, out var jwt, out _) && jwt.Payload.TryGetNumericDate("exp", out var exp) ? exp : null;

    // The service a password session should use after createSession or refreshSession: the #atproto_pds
    // endpoint of the DID document the service returned, when it is the account's own document and an
    // HTTPS URL, and current otherwise.
    //
    // This is how signing in through an entryway (bsky.social) lands the session on the account's PDS, as
    // the reference client does. The move is only between HTTPS services: a development PDS reached over
    // plain HTTP publishes the address it believes it has, which is usually not the one this process
    // reaches it at. Only the id and the service entries are read, so a malformed entry elsewhere does not
    // keep the session on the entryway.
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
            if ((id == DidDocument.PdsServiceId || id == did.Value + DidDocument.PdsServiceId) &&
                service.GetStringOrNull("type") == DidDocument.PdsServiceType)
            {
                return AtProtoHttp.TryNormalizeBaseUrl(service.GetStringOrNull("serviceEndpoint"), out var pds) &&
                       pds.Scheme == Uri.UriSchemeHttps
                    ? pds
                    : current;
            }
        }

        return current;
    }

    // The installed session, with the key object and refresher that belong to it and the credentials
    // derived from it.
    //
    // DPoP: The session's DPoP key, owned by the client, for an OAuth session.
    //
    // OAuthClient: The client that refreshes an OAuth session; not owned.
    //
    // Credentials: What requests carry, and the session's generation.
    private sealed record State(
        AtProtoSession Session, DPoPProofGenerator? DPoP, OAuthClient? OAuthClient, XrpcCredentials Credentials)
    {
        // The session's kind, DID and handle; never its credentials.
        public override string ToString() => $"State {{ Session = {Session} }}";
    }

    // One token exchange, shared by every caller that saw the same credentials.
    private sealed class RefreshOperation(XrpcCredentials observed)
    {
        public XrpcCredentials Observed { get; } = observed;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
