# Session Management

An `AtProtoClient` holds at most one session: who the account is, which service its requests go
to, and the tokens that authorize them. This page covers how a session is created, kept fresh,
persisted, resumed and ended, and what the client guarantees when it is used from many threads.

## The session model

A session is an immutable value of one of two kinds, both deriving from `AtProtoSession`:

| Type | Created by | Credentials |
|------|------------|-------------|
| `PasswordSession` | `LoginAsync`, `CreateAccountAndLoginAsync` | `AccessJwt` / `RefreshJwt` bearer tokens |
| `OAuthSession` | `OAuthClient.CompleteAuthorizationAsync` | DPoP-bound `AccessToken` / `RefreshToken`, and the `DPoPKey` they are bound to |

Every session has:

| Property | Type | Description |
|----------|------|-------------|
| `Did` | `Did` | The account's DID |
| `Handle` | `Handle` | The account's handle; `handle.invalid` when it could not be verified |
| `ServiceEndpoint` | `Uri` | The service its requests go to: the account's PDS |
| `ExpiresAt` | `DateTimeOffset?` | When the access token expires, if known |

A `PasswordSession` adds `Email`, `EmailConfirmed`, `EmailAuthFactor`, `Active` and `Status`. An
`OAuthSession` adds `Issuer`, `TokenEndpoint`, `RevocationEndpoint` and `Scope`, and holds its DPoP
private key as PKCS#8 bytes (`DPoPKey`) rather than a key object, so a session owns nothing that
needs disposing.

Refreshing a session never mutates it: the client installs a new value. Read the current one from
`client.Session`, or subscribe to `SessionChanged`.

```csharp
client.IsAuthenticated  // bool
client.Did              // Did?
client.Handle           // Handle?
client.Session          // AtProtoSession? — a PasswordSession or an OAuthSession

if (client.Session is PasswordSession { Email: { } email })
    Console.WriteLine(email);
```

`ToString()` on a session prints its kind, DID and handle, never its tokens.

## Signing in

### Password or app password

```csharp
var session = await client.LoginAsync("alice.example.com", "app-password");

// Two-factor: the service answers AuthFactorTokenRequired, and the user gets an email.
var session = await client.LoginAsync("alice.example.com", "app-password", authFactorToken: "123456");

// A taken-down account (AccountTakedown) can still sign in to migrate or export its data.
var session = await client.LoginAsync("alice.example.com", "password", allowTakendown: true);
```

The request goes to `client.ServiceUrl` (`AtProtoClientOptions.InstanceUrl` by default). When that is
an entryway such as `bsky.social`, the response carries the account's DID document, and the client
moves to the PDS it names. It only moves between HTTPS services: a development PDS reached over plain
HTTP publishes the address it believes it has, which is usually not the one you reach it at.

### A new account

```csharp
var session = await client.CreateAccountAndLoginAsync(new CreateAccountRequest
{
    Handle = Handle.Parse("alice.example.com"),
    Email = "alice@example.com",
    Password = "correct-horse-battery-staple",
});
```

### OAuth

```csharp
var session = await oauthClient.CompleteAuthorizationAsync(code, state, issuer);
await client.ApplySessionAsync(session, oauthClient);
```

Pass the `OAuthClient` that issued the session: it refreshes and revokes it. See the
[OAuth guide](oauth.md).

### The sub-clients never sign in

`client.Server.CreateSessionAsync`, `CreateAccountAsync`, `RefreshSessionAsync` and
`DeleteSessionAsync` are plain calls: they return the tokens they receive and take the ones they
need, but they never install or clear the client's session. Only the `AtProtoClient` methods above
change it.

## Staying signed in

With `AutoRefreshSession` on (the default) the client refreshes the session by itself:

- **Before a request**, when the access token expires within a minute. A password session's expiry
  is read from the access JWT's `exp` claim; an OAuth session's from the token response's
  `expires_in`.
- **After a rejection**, when the service answers `ExpiredToken`, or a 401 with a DPoP
  `invalid_token` challenge: the client refreshes and resends the request, once.

Concurrent requests that find the token expired share one refresh. This matters: refresh tokens are
single-use, and a second refresh with the same token would be refused and end the session.

```csharp
var client = new AtProtoClient(new AtProtoClientOptions
{
    InstanceUrl = "https://your-pds.example.com",
    AutoRefreshSession = true,   // default
    BackgroundRefresh = false,   // default
});
```

`BackgroundRefresh` adds a timer that refreshes shortly before expiry even when the client is idle,
which keeps a persisted session's copy current. A background refresh that fails transiently is
retried after 30 seconds, doubling up to 10 minutes. On-demand refresh already covers every request.

You can also refresh explicitly:

```csharp
await client.RefreshSessionAsync();
```

### When a refresh fails

If the service refuses the refresh token itself (the PDS answers `ExpiredToken` or `InvalidToken`,
the authorization server `invalid_grant`), the session is over: the client removes it, deletes it
from the session store, raises `SessionChanged` with `Expired`, and rethrows the error
(`XrpcAuthenticationException` or `OAuthException`). The user has to sign in again.

The store entry is only deleted if it still holds the refused refresh token. Another client that
shares the store may have refreshed the same session first, which is what spent the token; its
newer copy is left alone.

Any other failure (the service unreachable, a 5xx) leaves the session installed. A request whose
proactive refresh fails that way is still sent with the current token if it has not expired yet.

A failed refresh is never mistaken for new tokens: an error response, or a success without an
access token, fails the refresh instead of being installed.

## Persisting sessions

Give the client an `IAtProtoSessionStore` and it keeps the store current: it writes every session it
installs and every refreshed version, and removes the session on sign-out or expiry.

```csharp
public interface IAtProtoSessionStore
{
    ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken ct = default);
    ValueTask SetAsync(AtProtoSession session, CancellationToken ct = default);
    ValueTask RemoveAsync(Did did, CancellationToken ct = default);
}
```

| Implementation | Package | Notes |
|----------------|---------|-------|
| `InMemoryAtProtoSessionStore` | `ATProtoNet` | Process memory; lost on exit |
| `FileAtProtoSessionStore` | `ATProtoNet.Server` | One file per account, encrypted with Data Protection |
| `EfCoreAtProtoSessionStore<TContext>` | `ATProtoNet.Server` | A database table, encrypted with Data Protection |

Sessions serialize with `System.Text.Json` as `AtProtoSession`, a `$kind` member telling the two kinds
apart, so a custom store is a few lines:

```csharp
public sealed class FileSessionStore(string directory) : IAtProtoSessionStore
{
    public async ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken ct = default)
    {
        var path = PathFor(did);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AtProtoSession>(stream, cancellationToken: ct);
    }

    public async ValueTask SetAsync(AtProtoSession session, CancellationToken ct = default) =>
        await File.WriteAllTextAsync(PathFor(session.Did), JsonSerializer.Serialize(session), ct);

    public ValueTask RemoveAsync(Did did, CancellationToken ct = default)
    {
        File.Delete(PathFor(did));
        return ValueTask.CompletedTask;
    }

    private string PathFor(Did did) => Path.Combine(directory, Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(did.Value))) + ".json");
}
```

> **Security:** a session holds bearer tokens, and an OAuth session its DPoP private key. Encrypt
> sessions at rest (the Server stores use ASP.NET Core Data Protection), and never log them.

A store shared between processes must make each write visible before the next refresh reads it: a
client that refreshes from a stale copy spends a refresh token that is no longer valid.

If a store write fails, the new session stays installed in the client (it is valid) and the error is
logged; the store keeps the older copy until the next successful write.

## Resuming a session

Three ways to install a session you saved earlier:

| Method | Contacts the service | Writes the store |
|--------|----------------------|------------------|
| `ResumeSessionAsync(session, oauthClient?)` | Yes: `getSession`, refreshing if the token expired | Yes |
| `ApplySessionAsync(session, oauthClient?)` | No | Yes |
| `TryRestoreSessionAsync(did, oauthClient?)` | No | No (it reads from the store) |

```csharp
var client = new AtProtoClient(sessionStore: store);

if (!await client.TryRestoreSessionAsync(savedDid))
    await client.LoginAsync("alice.example.com", "app-password");
```

`ResumeSessionAsync` returns the session as it is after the check: refreshed if it had to be, and
for a password session with the handle and email the service reported. If the service rejects the
session (still, after a refresh where one applied), it throws `XrpcAuthenticationException` (or
`OAuthException` for a refused OAuth refresh) and the session is removed from the client and the
store, reported as `Expired`. Any other failure, such as the service being unreachable, leaves the
session installed.

Installing a session points the client at the session's `ServiceEndpoint` and replaces any session
already installed, of either kind: a password login after an OAuth session no longer refreshes
through OAuth.

## Signing out

```csharp
await client.LogoutAsync();
```

Sign-out first tears the session down locally: the client drops it, removes it from the session
store and raises `SessionChanged` with `Removed`. Then it ends the session at the service:

- a password session with `com.atproto.server.deleteSession`, sent with the **refresh** JWT the
  Lexicon requires;
- an OAuth session with token revocation (RFC 7009) at the authorization server's
  `revocation_endpoint`, with a DPoP proof. The refresh token is revoked, which ends the grant.

If that fails, or the store refuses the removal, `LogoutAsync` throws after the local teardown, since
a session left active at the service is worth knowing about. So does signing out an OAuth session
that was installed without its `OAuthClient` (`InvalidOperationException`): it cannot be revoked. A
token the service already considers invalid is not a failure.

## Session events

```csharp
client.SessionChanged += (_, e) =>
{
    switch (e.Change)
    {
        case AtProtoSessionChange.Created:   // installed: login, apply, resume
        case AtProtoSessionChange.Refreshed: // replaced by a newer version of itself
            Save(e.Session!);
            break;
        case AtProtoSessionChange.Expired:   // refresh token refused; e.Error says why
        case AtProtoSessionChange.Removed:   // signed out
            Forget(e.Previous!.Did);
            break;
    }
};
```

Handlers run synchronously on the thread that made the change, after the client has released its
session lock, so a handler may call back into the client. An exception from a handler is logged,
not propagated. For persistence prefer an `IAtProtoSessionStore`, which the client awaits.

## Thread safety

`AtProtoClient` is safe to share:

- **Requests** may run concurrently from any number of threads. Each call reads the service and
  the session's credentials once, as one immutable snapshot, and every attempt of it (a DPoP nonce
  retry, a wait on a 429) uses that snapshot, so it never sees half of a change.
- **A rejected call is resent only as the same account.** After an `ExpiredToken` or
  `invalid_token` it is resent with that account's refreshed tokens, to the same service. If
  another account's session was installed meanwhile, the call fails instead of running as that
  account; a call whose session was signed out or replaced while in flight fails with
  `XrpcAuthenticationException` (`AuthenticationRequired`).
- **Session changes** (`LoginAsync`, `CreateAccountAndLoginAsync`, `ApplySessionAsync`,
  `ResumeSessionAsync`, `TryRestoreSessionAsync`, `RefreshSessionAsync`, `LogoutAsync`, and the
  client's own refreshes) are serialized. A refresh that was waiting while another change installed
  a different session does nothing.
- **Refreshes coalesce**: concurrent callers that saw the same tokens share one token exchange,
  whether they called `RefreshSessionAsync` or had a request rejected.
- **A token exchange is never abandoned halfway.** The authorization server rotates the refresh
  token as soon as it answers, so once an exchange has started only its own 30-second limit ends
  it, and its result is stored. Cancelling the call that started it, or disposing the client, only
  stops the waiting. Likewise, a store write that follows a session change (install, refresh,
  expiry, sign-out) is not cancelled with the call, so the store, the client and `SessionChanged`
  agree.
- `SetServiceUrl`, `SetProxy` and `SetLabelers` swap a value atomically and apply from the next
  request. They are client-wide defaults, not meant to change while other callers rely on them; for
  per-call values pass `XrpcCallOptions`.

Coalescing works within one client. Several clients that share one stored session (per-request
clients on a web server, several processes on one store) each refresh on their own, so two of them
refreshing at the same moment can spend the same refresh token.

## Ownership and disposal

The client disposes what it creates, and nothing it is given:

| Given to the client | Disposed by the client |
|---------------------|------------------------|
| `HttpClient` | No (one it creates itself, yes) |
| `OAuthClient` | No |
| `IAtProtoSessionStore` | No |
| A session | Sessions are values; the client builds its own DPoP key object from `OAuthSession.DPoPKey` and disposes that |

Prefer `await using` / `DisposeAsync`, which waits for a token exchange already under way to finish
and be stored (at most its 30-second limit). The synchronous `Dispose` returns at once: the exchange
finishes and is stored in the background, and the DPoP key is released after it.

Disposing is not signing out: the session stays valid at the service and in the store. Call
`LogoutAsync` first to end it.

## ASP.NET Core

For per-user clients on a server, `IAtProtoClientFactory` builds a client per request from the
session store, refreshing on demand and writing rotated tokens back. See
[Server Integration](server.md) and [ASP.NET Core Integration](aspnet-core.md).
