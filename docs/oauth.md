# OAuth Authentication

ATProto.NET supports the [AT Protocol OAuth specification](https://atproto.com/specs/oauth) for secure, standards-based authentication. This includes DPoP (RFC 9449), Pushed Authorization Requests (RFC 9126), and PKCE (RFC 7636).

## Overview

OAuth is the recommended authentication method for AT Protocol applications — especially user-facing apps where you don't want to handle passwords directly. The flow works as follows:

1. Your app redirects the user to their PDS authorization endpoint
2. The user authenticates with their PDS
3. The PDS redirects back to your app with an authorization code
4. Your app exchanges the code for DPoP-bound access and refresh tokens

## When to Use OAuth vs App Passwords

| Scenario | Recommendation |
|----------|---------------|
| User-facing web app (Blazor, ASP.NET) | **OAuth** — no password handling |
| CLI tool / daemon / server-to-server | **App password** — simpler flow |
| Mobile app | **OAuth** — with PKCE for security |
| Automated scripts | **App password** — non-interactive |

## Setup

### 1. Define Your Client Metadata

AT Protocol OAuth uses [Client ID Metadata Documents](https://drafts.aaronpk.com/draft-parecki-oauth-client-id-metadata-document/draft-parecki-oauth-client-id-metadata-document.html). Your client metadata URL serves as your `client_id`.

Host a JSON document at a public URL. The conventional path is `/oauth-client-metadata.json` at
the root of your host, with no port or query: authorization servers then show your bare domain on
the consent screen instead of the whole URL.

```json
{
  "client_id": "https://myapp.example.com/oauth-client-metadata.json",
  "client_name": "My AT Proto App",
  "client_uri": "https://myapp.example.com",
  "redirect_uris": ["https://myapp.example.com/oauth/callback"],
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"],
  "scope": "atproto include:app.bsky.authFullApp?aud=did:web:api.bsky.app%23bsky_appview blob:*/*",
  "token_endpoint_auth_method": "none",
  "application_type": "web",
  "dpop_bound_access_tokens": true
}
```

You can also serve the document straight from the `OAuthClientMetadata` you configure below — `ToJson()` omits unset optional fields, which authorization servers require (a `"jwks_uri": null` is rejected with `invalid_client_metadata`; *absent* and *null* are not the same thing):

```csharp
app.MapGet("/oauth-client-metadata.json", () =>
    Results.Content(oauthOptions.ClientMetadata.ToJson(), "application/json"));
```

`Results.Json(metadata)` works too — the optional properties are annotated with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, so nulls are never written regardless of the serializer options in play. The [hosted login](#hosted-login-aspnet-core) serves it for you with `ServeClientMetadata`.

### 2. Configure OAuthClient

```csharp
using ATProtoNet.Auth.OAuth;

var oauthOptions = new OAuthOptions
{
    ClientMetadata = new OAuthClientMetadata
    {
        ClientId = "https://myapp.example.com/oauth-client-metadata.json",
        ClientName = "My AT Proto App",
        ClientUri = "https://myapp.example.com",
        RedirectUris = ["https://myapp.example.com/oauth/callback"],
        GrantTypes = ["authorization_code", "refresh_token"],
        ResponseTypes = ["code"],
        Scope = AtProtoScopes.Presets.BlueskyApp,
        TokenEndpointAuthMethod = "none",
        ApplicationType = "web",
        DpopBoundAccessTokens = true,
    },
    Scope = AtProtoScopes.Presets.BlueskyApp,
};
```

The `Scope` you request must match the client metadata's. See [Scopes](#scopes) for what to ask for.

### 3. Create the OAuthClient

```csharp
var logger = loggerFactory.CreateLogger<OAuthClient>();
using var oauthClient = new OAuthClient(oauthOptions, logger);
```

Every request the client makes to a PDS or an authorization server goes out under the SDK's fetch
policy (see [Fetch Policy](#fetch-policy)). Set `OAuthOptions.HttpClient` to route them through a
client of your own instead, and `OAuthOptions.StateStore` to keep pending logins somewhere other
than process memory (see [Pending Authorizations](#pending-authorizations)).

### Confidential Clients

A server-side application (a "backend for frontend") can authenticate itself to the authorization
server with a key, as a *confidential* client (`private_key_jwt`). The reference authorization
server gives confidential clients far longer sessions: refresh tokens last 180 days instead of two
weeks.

1. Generate a P-256 key once, and store it as you would any server secret:

   ```csharp
   using var key = OAuthClientKey.Generate("key-2026");
   byte[] pkcs8 = key.ExportPrivateKey();            // persist this
   // later: using var key = OAuthClientKey.Import("key-2026", pkcs8);
   ```

2. Publish its public half in the client metadata and hand the key to the client:

   ```csharp
   var oauthOptions = new OAuthOptions
   {
       ClientMetadata = new OAuthClientMetadata
       {
           ClientId = "https://myapp.example.com/oauth-client-metadata.json",
           RedirectUris = ["https://myapp.example.com/oauth/callback"],
           Scope = AtProtoScopes.Presets.BlueskyApp,
           TokenEndpointAuthMethod = "private_key_jwt",
           TokenEndpointAuthSigningAlg = "ES256",
           Jwks = OAuthClientKey.CreateKeySet([key]),     // or JwksUri = "https://…/jwks.json"
       },
       Scope = AtProtoScopes.Presets.BlueskyApp,
       ClientKeys = [key],
   };
   ```

Every pushed authorization, token, refresh and revocation request then carries a fresh ES256
client assertion (RFC 7523): `iss` and `sub` the client id, `aud` the authorization server's
issuer, a random `jti`, and a 60-second lifetime. The constructor refuses an inconsistent setup
(keys for a public client, a confidential client without keys, an algorithm other than ES256, no
`jwks` or `jwks_uri`, or a key the inline `jwks` does not publish), and sign-in fails with
`unsupported_client_auth` at an authorization server that does not accept `private_key_jwt`.

The authorization server binds each grant to the key that authenticated it, so a session records
its key id (`OAuthSession.ClientKeyId`) and every refresh and revocation is signed with that key.
To rotate, put the new key first in `ClientKeys` (new logins use the first key) and keep the old
one configured and published until its sessions have ended; a session whose key is gone fails with
`client_key_unavailable`. Sessions from before a client became confidential carry no key id and
are still sent as a public client's, which an authorization server that now expects an assertion
refuses: those users sign in again. The keys are yours: the `OAuthClient` does not dispose them.

The hosted login takes the same keys in `AtProtoOAuthServerOptions.ClientKeys` (see
[Hosted Login](#production-configuration)).

### Development / Loopback Client

For local development, the AT Protocol OAuth spec provides a special [loopback client](https://atproto.com/specs/oauth#localhost-client-development) workflow. Use `http://localhost` as the `client_id` origin (no port number). Configuration is passed via query parameters:

- **`redirect_uri`** — declares allowed redirect paths (default: `http://127.0.0.1/` and `http://[::1]/`). Port numbers are ignored during matching; only the path must match exactly.
- **`scope`** — declares requested scopes (default: `atproto`).

```csharp
var oauthOptions = new OAuthOptions
{
    ClientMetadata = new OAuthClientMetadata
    {
        // Declare the /oauth/callback path via query parameter
        ClientId = "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%2Foauth%2Fcallback",
        RedirectUris = ["http://127.0.0.1:5000/oauth/callback"],
        Scope = "atproto",
        TokenEndpointAuthMethod = "none",
        DpopBoundAccessTokens = true,
    },
    Scope = "atproto",
};
```

> **Note:** The redirect URI uses `127.0.0.1` (not `localhost`) per [RFC 8252](https://datatracker.ietf.org/doc/html/rfc8252). Port numbers are not matched by the Authorization Server, so you can use any available port.

See the [`samples/ServerIntegrationSample`](../samples/ServerIntegrationSample/) project for a complete working example.

## Authorization Flow

### Step 1: Start Authorization

```csharp
// The identifier can be a handle, DID, or server URL
var authorization = await oauthClient.StartAuthorizationAsync(
    identifier: "alice.bsky.social",
    redirectUri: "https://myapp.example.com/oauth/callback",
    new OAuthAuthorizationOptions
    {
        // Who is signing in: pending logins are limited per requester (see below)
        RequesterId = httpContext.Connection.RemoteIpAddress?.ToString(),
    });

// Redirect the user; the callback will carry authorization.State back
return Results.Redirect(authorization.AuthorizationUrl.AbsoluteUri);
```

The result is an `OAuthAuthorizationRequest`: the `AuthorizationUrl`, the `State`, and `ExpiresAt`,
ten minutes (`OAuthClient.AuthorizationLifetime`) after the start. It deconstructs, like the tuple
it replaces: `var (url, state, _) = await oauthClient.StartAuthorizationAsync(…)`.

This performs:
- Identity resolution (handle → DID → PDS), for a handle or DID
- Authorization server discovery (PDS → protected-resource metadata → AS metadata)
- PKCE code verifier/challenge generation
- DPoP keypair generation (ES256/P-256)
- Pushed Authorization Request (PAR)
- Storing the pending authorization in the state store

A URL identifier, or `OAuthAuthorizationOptions.ServerUrl` (the identifier is then sent as the
login hint), names the server to sign in at: a PDS, or an authorization server such as an
entryway that serves no protected-resource metadata, which is then its own issuer. The account is
learned at the callback.

### Step 2: Handle the Callback

When the user is redirected back, your callback receives `code`, `state`, and `iss` query parameters:

```csharp
// In your callback handler
var session = await oauthClient.CompleteAuthorizationAsync(
    code: queryParams["code"],
    state: queryParams["state"],
    issuer: queryParams["iss"]);

Console.WriteLine($"Authenticated as {session.Handle} ({session.Did})");
Console.WriteLine($"PDS: {session.ServiceEndpoint}");
```

The result is an `OAuthSession`: an immutable value holding the account's DID and PDS, the tokens,
their expiry, the authorization server's endpoints, and the DPoP key the tokens are bound to (as
PKCS#8 bytes). A handle that did not verify comes back as `handle.invalid`.

This performs:
- State lookup (CSRF protection); each state completes at most once
- Issuer verification (the `iss` must be exactly the issuer the request was pushed to)
- Authorization code exchange with DPoP proof (and client assertion, for a confidential client)
- Token response checks: a DPoP token, for the expected DID, with the `atproto` scope
- For a login started from a server URL: DID → PDS → AS consistency and handle verification

A login started from a handle or DID reuses the identity resolved at the start: the tokens must be
for that DID, and its PDS and verified handle are taken as they were, with nothing fetched again.
The session's `ServiceEndpoint` is always the PDS the account's DID document names, also when the
login began at an entryway.

### Step 3: Use the Session

Install the session on an `AtProtoClient`, together with the `OAuthClient` that issued it:

```csharp
await using var client = new AtProtoClient(
    sessionStore: sessionStore);   // optional: keeps a persisted copy current

await client.ApplySessionAsync(session, oauthClient);

// Now use the client normally: it is pointed at the session's PDS
var profile = await client.Bsky.Actor.GetProfileAsync(session.Did);
await client.Bsky.PostAsync("Hello from OAuth!");
```

The client neither copies nor disposes the `OAuthClient`; keep one per application and share it.

### Step 4: Refresh and Sign Out

The client refreshes the session by itself: before a request when the access token is about to
expire, and once more when the PDS answers with a DPoP `invalid_token` challenge. Concurrent requests
share one refresh, since a refresh token can only be spent once. Each refresh installs a new
`OAuthSession` and writes it to the session store; `SessionChanged` reports it.

```csharp
await client.RefreshSessionAsync();   // explicitly, if you need to

// Sign out: drops the session locally and from the store, then revokes it (RFC 7009)
await client.LogoutAsync();
```

Before it spends the refresh token, a refresh resolves the account's DID again and checks that its
authorization server is still the session's issuer (`auth_server_mismatch` otherwise, with nothing
sent), as the reference client does; the session moves to the PDS the DID document names now. The
response must be for the same DID (`did_mismatch`) and carry the `atproto` scope (`invalid_scope`);
a response that fails either check is never installed.

If the authorization server refuses the refresh token (`invalid_grant`: expired, revoked, or already
used), the session has ended: the client removes it and throws `OAuthException`. Outside an
`AtProtoClient`, `oauthClient.RefreshAsync(session)` returns the refreshed session and
`oauthClient.RevokeAsync(session)` revokes it.

To resume after a restart, restore the session from your store and hand the `OAuthClient` over again:

```csharp
if (await client.TryRestoreSessionAsync(did, oauthClient))
    Console.WriteLine($"Welcome back, {client.Handle}");
```

See [Session Management](session-management.md) for the whole lifecycle.

## Scopes

`atproto` is required; everything else says what the session may do. Request only what the
application needs, as granular permissions:

```csharp
var scope = AtProtoScopes.Combine(
    AtProtoScopes.AtProto,
    AtProtoScopes.Repo("com.example.todo"),                                       // repo:com.example.todo
    AtProtoScopes.Rpc("app.bsky.actor.getProfile", AtProtoScopes.BlueskyAppView), // rpc:…?aud=did:web:api.bsky.app%23bsky_appview
    AtProtoScopes.Blob("image/*"));                                               // blob:image/*
```

A Bluesky client includes Bluesky's published permission sets instead of listing every method;
`AtProtoScopes.Presets` has the common ones:

| Preset | Scope |
|--------|-------|
| `BlueskyApp` | `atproto include:app.bsky.authFullApp?aud=did:web:api.bsky.app%23bsky_appview blob:*/*` |
| `BlueskyAppWithChat` | `BlueskyApp` plus `include:chat.bsky.authFullChatClient?aud=did:web:api.bsky.chat%23bsky_chat` |
| `BlueskyReadOnly` | `atproto include:app.bsky.authViewAll?aud=did:web:api.bsky.app%23bsky_appview` |
| `BlueskyPosting` | `atproto include:app.bsky.authCreatePosts?aud=did:web:api.bsky.app%23bsky_appview blob:*/*` |

Permission sets cannot grant `blob` or `account` permissions, so the presets that upload media add
`blob:*/*` themselves. `AtProtoScopes.PermissionSets` lists the published set NSIDs.

An `rpc` or `include` audience is a service: a DID with its service fragment
(`did:web:api.bsky.app#bsky_appview`), or `*` for `rpc`. Authorization servers reject a bare DID,
so `Rpc` and `Include` throw `ArgumentException` for one.

The transitional scopes (`transition:generic`, `transition:chat.bsky`, `transition:email`) are
legacy: still accepted, but the specification intends to remove them, and the consent screen
presents `transition:generic` as access to nearly everything. `AtProtoScopes.Default`
(`atproto transition:generic`) remains the SDK's default for now; new applications should not rely
on it. The `action` parameter of `identity` scopes is gone from the specification and
authorization servers reject it, so `IdentityAction` and `Identity(attr, action)` are obsolete:
use `AtProtoScopes.Identity("handle")` or `AtProtoScopes.Identity("*")`.

## Dynamic PDS Selection

Users on the AT Protocol can use any PDS. Rather than hardcoding a PDS URL, resolve the user's PDS dynamically:

```csharp
// Set PDS URL at runtime
client.SetServiceUrl(new Uri("https://custom-pds.example.com"));

// Or let OAuth do it — the session's ServiceEndpoint is the account's PDS
var authorization = await oauthClient.StartAuthorizationAsync(
    "alice.custom-pds.example.com",
    "https://myapp.example.com/callback");
```

## Hosted Login (ASP.NET Core)

`ATProtoNet.Server` runs the whole flow for an ASP.NET Core application (MVC, Razor Pages, minimal
APIs or Blazor) and signs the user in with a standard authentication cookie.

### Setup

```csharp
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

// Program.cs
builder.Services.AddAuthentication("Cookies").AddCookie("Cookies");
builder.Services.AddAtProtoAuthentication();
builder.Services.AddAtProtoServer();   // session store, client factory, refresh coordinator
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAtProtoOAuth();  // Maps /atproto/login, /atproto/callback, /atproto/relay, /atproto/logout
```

`AddAtProtoAuthentication()` registers `AtProtoOAuthService` and its `OAuthClient`, built from the
options alone, as singletons. The client factory refreshes the sessions it restores with that
`OAuthClient`, so a process that has just restarted refreshes the sessions it stored before. With
an OAuth flow of your own, register your `OAuthClient` as a singleton instead, and the factory uses
it.

For Blazor, the `ATProtoNet.Blazor` package adds a `LoginForm` component that submits to the
login endpoint, and widgets that act as the signed-in user (see [blazor.md](blazor.md)):

```razor
<LoginForm ReturnUrl="/" ShowPdsOption="true" />
```

### OAuth Flow

1. The browser opens `GET /atproto/login?handle=alice.bsky.social&returnUrl=/inbox`, which resolves
   the user's PDS, starts OAuth, and binds the login to the browser with a cookie
2. The user authorizes at their authorization server
3. `GET /atproto/callback` exchanges the code for tokens, checks the browser's binding cookie,
   stores the session, and issues the authentication cookie via `HttpContext.SignInAsync()`
4. `[Authorize]`, `<AuthorizeView>` and `IAtProtoClientFactory` work with the signed-in user

`AtProtoOAuthService.CompleteCallbackAsync` returns an `AtProtoOAuthCallbackResult`: the account's
`Did`, and the `RedirectUrl` to send the browser to, which is the login's own local return URL or,
when the callback arrived on another loopback origin than the login started on, the relay
endpoint on the login's origin (`IsRelay`). It is never a URL taken from the request. A relayed
login that is not redeemed within two minutes, or is evicted when more than 256 wait, has its
tokens revoked.

A login that fails redirects to `LoginPath` (default `/login`) with an `error` code, never a
message: the `OAuthException.Error` (`invalid_handle`, `login_not_bound`, `invalid_state`, …), the
authorization server's own error code (`access_denied`, …), or `login_failed` for anything else,
which is logged. `LoginForm` turns the codes into messages.

### Available Claims

After login, these claims are available on `context.User`; the names are constants on
`AtProtoClaimTypes`:

| Claim | Description |
|-------|-------------|
| `ClaimTypes.NameIdentifier` | User's DID |
| `ClaimTypes.Name` | User's handle, or `handle.invalid` when it did not verify |
| `did` (`AtProtoClaimTypes.Did`) | User's DID |
| `handle` (`AtProtoClaimTypes.Handle`) | User's handle, or the DID when it did not verify |
| `handle_verified` (`AtProtoClaimTypes.HandleVerified`) | `"true"` or `"false"` |
| `pds_url` (`AtProtoClaimTypes.PdsUrl`) | User's PDS URL |
| `auth_method` (`AtProtoClaimTypes.AuthMethod`) | Always `"oauth"` |

With `AddAtProtoServer()` registered, the callback stores the `OAuthSession` in the
`IAtProtoSessionStore`, and `/atproto/logout` removes it and revokes it at the authorization
server. Both take the account's refresh lock (see [Refreshing Across Requests](#refreshing-across-requests)),
so neither interleaves with a refresh.

### Production Configuration

For production, provide explicit client metadata instead of the auto-generated loopback client_id,
and let `MapAtProtoOAuth()` serve it at its `client_id`:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientMetadata = new OAuthClientMetadata
    {
        ClientId = "https://myapp.example.com/oauth-client-metadata.json",
        ClientName = "My App",
        ClientUri = "https://myapp.example.com",
        RedirectUris = ["https://myapp.example.com/atproto/callback"],
        Scope = AtProtoScopes.Presets.BlueskyApp,
    };
    options.Scopes = AtProtoScopes.Presets.BlueskyApp;
    options.ServeClientMetadata = true;   // GET /oauth-client-metadata.json
});
```

A confidential client adds its keys, and can have their public halves served at its `jwks_uri`:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientMetadata = new OAuthClientMetadata
    {
        ClientId = "https://myapp.example.com/oauth-client-metadata.json",
        RedirectUris = ["https://myapp.example.com/atproto/callback"],
        Scope = AtProtoScopes.Presets.BlueskyApp,
        TokenEndpointAuthMethod = "private_key_jwt",
        TokenEndpointAuthSigningAlg = "ES256",
        JwksUri = "https://myapp.example.com/oauth/jwks.json",
    };
    options.Scopes = AtProtoScopes.Presets.BlueskyApp;
    options.ClientKeys.Add(OAuthClientKey.Import("key-2026", pkcs8));  // new logins use the first key
    options.ServeClientMetadata = true;   // also GET /oauth/jwks.json
});
```

See [Confidential Clients](#confidential-clients) for key rotation, and [blazor.md](blazor.md) for
every option.

### Refreshing Across Requests

The client factory creates a client per request from the stored session, so two requests of one
user can find its access token about to expire at the same moment. Refresh tokens are single-use:
if both clients refreshed, the authorization server would see its token spent twice and end the
session. `AddAtProtoServer()` therefore registers an `ISessionRefreshCoordinator`
(`InProcessSessionRefreshCoordinator`), and every client the factory creates refreshes under the
account's lock and reads the store once it holds it. A session another client has refreshed
meanwhile is taken up without spending anything, and one the store no longer holds was signed out,
so it ends (`XrpcAuthenticationException`, `InvalidToken`) instead of being brought back.

Clients you create yourself get the same protection with `AtProtoClientOptions.RefreshCoordinator`,
given the same coordinator and a shared store. The in-process coordinator covers one process; several
instances sharing one store need a distributed lock behind `ISessionRefreshCoordinator`.

## Security Considerations

### DPoP (Demonstration of Proof-of-Possession)

All access tokens are DPoP-bound. Every API request includes a DPoP proof JWT signed with the session's ES256 key. This prevents token theft — even if an attacker intercepts the access token, they cannot use it without the private key.

DPoP nonces are the server's, not the session's: the SDK keeps the latest nonce each server handed
out, by origin, in one cache per process, shared by every `AtProtoClient`, per-request server
client, space reader and `OAuthClient`. A client created for a single request therefore sends a
known nonce with its first call instead of paying a `use_dpop_nonce` round trip, and the nonce a
pushed authorization request learns is reused by the token exchange. A stale nonce only costs the
retry it was meant to save.

### PKCE (Proof Key for Code Exchange)

The authorization code flow uses PKCE with S256 challenge method. The code verifier is generated with 32 bytes of cryptographic randomness and never sent to the authorization server directly.

### State Parameter

The state parameter is generated with 32 bytes of cryptographic randomness. It ties the callback
to a pending authorization, and each state completes at most once. It does not tie the callback
to the browser that started the login, so a web front end must bind the two itself; the hosted
login does it with a cookie (see [blazor.md](blazor.md#login-csrf-protection)). Keep application data, such as
the return URL, with the pending authorization: `OAuthAuthorizationOptions.AppState` is handed
back by `CompleteAuthorizationWithAppStateAsync`.

### Issuer Verification

The `iss` parameter from the callback must be exactly the issuer the authorization was pushed to, which prevents mix-up attacks.

### DID Verification

At every code exchange and every refresh, the account the tokens name is resolved from a freshly
fetched DID document (`IIdentityResolver.ResolveUncachedAsync`), and its authorization server,
read from freshly fetched metadata, must be the issuer (`auth_server_mismatch` otherwise). No
cached copy is used for this check, since one from before an account moved would confirm the
server it left. A login started from a handle or DID must also have produced tokens for that
account (`did_mismatch`). Tokens that fail a check are revoked, best effort.

### Authorization Server Metadata

Authorization server metadata is held to the AT Protocol profile:

- its `issuer` is exactly the issuer it was looked up as, in canonical form (scheme, host, port
  and path included), and the protected-resource metadata must name a canonical issuer;
- the authorization, token, pushed-authorization and revocation endpoints are absolute `https`
  URLs with no query, fragment or userinfo;
- `require_pushed_authorization_requests`, `authorization_response_iss_parameter_supported` and
  `client_id_metadata_document_supported` are all `true`;
- `scopes_supported` includes `atproto` and `dpop_signing_alg_values_supported` includes `ES256`.

A PDS's protected-resource metadata must name that PDS as its `resource` (RFC 9728 section 3.3)
and exactly one authorization server, and an authorization server that lists
`protected_resources` must list that PDS (section 4), as the reference client checks. Metadata
documents are cached by URL for five minutes (`AuthorizationServerDiscovery.MetadataCacheLifetime`)
for starting logins; the issuer check at a code exchange or refresh fetches them afresh.

### Handle and DID Resolution

Identities are resolved through an `IIdentityResolver` (see [Identity Resolution](did-resolution.md)).
A handle or DID is parsed before anything is sent, and every identity fetch follows the SDK's SSRF
policy: HTTPS only, public addresses only (checked after DNS), hostname-level `did:web` without a port,
and bounded responses. The account's handle counts as verified only when the DID document claims
it and the handle's own authorities (DNS TXT and `/.well-known/atproto-did`) resolve it back to the
same DID; otherwise the session's `Handle` is `handle.invalid` (`Handle.Invalid`).

`OAuthOptions.IdentityResolver` supplies the resolver, for example a shared one from
`AddAtProtoIdentity` or one with the development opt-out for a local PDS and PLC; the caller keeps
ownership of it. When it is `null`, the client creates one with `IdentityResolver.CreateDefault`,
applying `HandleResolutionTimeout` and `AllowPrivateNetworks`, and disposes it with the client.

### Fetch Policy

The PDS URL comes from a DID document, the authorization server from the PDS's metadata, and the
pushed-authorization, token and revocation endpoints from the authorization server's metadata:
all written by whoever controls the account. So every request the client makes to them follows
the same policy as identity fetches: HTTPS only, public addresses only (checked after DNS), no
redirects, and capped responses (64 KiB). Metadata requests also have a 10-second timeout. A
metadata URL the rules refuse fails with `invalid_server_url` before anything is sent; a refused
connection, a redirect, an error status or an oversized body is `metadata_fetch_failed`; an answer
that is not metadata JSON is `invalid_metadata`. A pushed-authorization, token or revocation
endpoint that is not an HTTPS URL without query or fragment, or that resolves to a private
address, fails with `invalid_server_url`, again before anything reaches it.

`OAuthOptions.AllowPrivateNetworks` is the development opt-out for a local PDS (plain HTTP and
private addresses), for these requests and for the identity resolver the client creates.
`OAuthOptions.HttpClient` routes the requests through a client of your own, used as is: the URL
rules and the body caps still apply, but the address check lives in the SDK's handler, so supply
one only if it is already safe for such URLs (or reaches them through a proxy you control).

Each pushed-authorization, token, refresh or revocation request, retry and response body
included, must complete within `OAuthOptions.RequestTimeout` (30 seconds by default;
`HttpClient.Timeout` stops applying once the headers arrive), or it fails with
`request_timeout`. A connection that fails or a body that breaks off is `request_failed`. Only
the caller's own cancellation surfaces as `OperationCanceledException`.

The SDK's handler connects directly and never through a proxy, since a proxy would make the
checked address the proxy's. An application that must reach the internet through an egress
proxy supplies its own `HttpClient` and relies on the proxy to keep requests off private
addresses.

### Redirect URI Validation

Redirect URIs must use HTTPS. HTTP is only allowed for localhost during development.

### Pending Authorizations

Between the start and the callback, a login waits in an `IOAuthStateStore` (`OAuthOptions.StateStore`).
Starting a login needs no credentials, so the store must not let anyone crowd out other people's
logins:

- **`InMemoryOAuthStateStore`** (the default) holds at most ten pending logins per requester and
  10,000 in all. A requester over its limit displaces its own oldest login, and past the total the
  oldest of all goes; nothing is ever refused, and entries expire after ten minutes. Derive the
  requester with `OAuthAuthorizationOptions.RequesterIdFor(remoteAddress)`, which groups an IPv6
  address by its /64, so one subscriber cannot mint unlimited requesters. Logins without a
  requester count only toward the total. Behind a reverse proxy, restore the client's address
  first (`app.UseForwardedHeaders()` with the proxy in `KnownProxies`), or every user shares one
  requester's limit of ten.
- **`DistributedCacheOAuthStateStore`** keeps them in an `IDistributedCache` (Redis, SQL Server…),
  so a login started on one instance completes on another, or after a restart. Entries are keyed
  by a hash of the state and expire with the login. They hold the PKCE verifier and the DPoP
  private key the session will use, so the store requires `Protect` and `Unprotect` (for example
  an `IDataProtector`'s methods); `StoreSecretsUnencrypted` is the explicit opt-out for a cache
  nobody else can read. Taking an entry is a read and then a removal, not atomic: two callbacks
  racing with one state can both read it, but the authorization server exchanges the code once,
  and the browser binding still has to match. A distributed cache cannot count entries per
  requester: rate-limit the login endpoint in front of the application.

```csharp
var oauthOptions = new OAuthOptions
{
    // …
    StateStore = new DistributedCacheOAuthStateStore(cache, new DistributedCacheOAuthStateStoreOptions
    {
        Protect = protector.Protect,
        Unprotect = protector.Unprotect,
    }),
};
```

## Server Discovery

The `AuthorizationServerDiscovery` class handles the full resolution chain:

```
Handle → DID → PDS → Protected Resource Metadata → Authorization Server Metadata
```

### Resolution Methods

`OAuthClient.StartAuthorizationAsync` walks the whole chain for you. The steps are public if you need
them on their own:

```csharp
var discovery = new AuthorizationServerDiscovery(httpClient: null, logger, identityResolver);

// Handle or DID → DID, PDS and authorization server metadata, as OAuthException on failure
var (pdsUrl, metadata, did) = await discovery.ResolveFromIdentifierAsync("alice.bsky.social");

// PDS → authorization server metadata (protected-resource metadata, then AS metadata), validated
var metadata2 = await discovery.ResolveAuthorizationServerAsync("https://pds.example.com");

// The identity steps on their own: DID document, verified handle, PDS
var identity = await discovery.IdentityResolver.ResolveAsync(AtIdentifier.Parse("alice.bsky.social"));
```

`FetchProtectedResourceMetadataAsync` and `FetchAuthorizationServerMetadataAsync` return the
documents as fetched, without the checks above.

Handle resolution consults only the handle's own authorities, DNS TXT (over a configurable
DNS-over-HTTPS endpoint) and the HTTPS well-known, concurrently and within `HandleResolutionTimeout`.
When both answer they must agree, and a disagreement fails with `handle_resolution_conflict`; no third
party such as an AppView is asked.

## DPoP Key Management

Each OAuth session has its own ES256 (P-256) key pair, carried by the session as PKCS#8 bytes, so
persisting the session persists the key:

```csharp
ReadOnlyMemory<byte> keyBytes = session.DPoPKey;

// A proof generator over the same key, should you need one outside an AtProtoClient
using var dpop = new DPoPProofGenerator(keyBytes.ToArray());
```

AT Protocol DPoP proofs are ES256 only, so importing a key on any other curve (a K-256 repo signing key, for example) throws `ArgumentException`.

> **Warning:** The exported key is unencrypted PKCS#8. Store it in a secure location such as an OS keychain, encrypted database, or DPAPI-protected storage.

## Error Handling

OAuth-specific errors throw `OAuthException`:

```csharp
try
{
    var session = await oauthClient.CompleteAuthorizationAsync(code, state, issuer);
}
catch (OAuthException ex) when (ex.Error == "invalid_state")
{
    // Unknown or expired state parameter
}
catch (OAuthException ex) when (ex.Error == "issuer_mismatch")
{
    // Authorization server issuer doesn't match
}
catch (OAuthException ex) when (ex.Error == "did_mismatch")
{
    // Token DID doesn't match expected identity
}
catch (OAuthException ex)
{
    Console.WriteLine($"OAuth error ({ex.Error}): {ex.Message}");
}
```

### Error Codes

| Code | Description |
|------|-------------|
| `invalid_state` | Unknown or already completed state parameter |
| `state_expired` | Authorization state exceeded 10-minute timeout |
| `issuer_mismatch` | Callback issuer doesn't match expected AS |
| `missing_sub` | Token response missing subject (DID) |
| `invalid_sub` | Token response subject is not a valid DID |
| `did_mismatch` | Token DID doesn't match expected identity |
| `invalid_scope` | Token doesn't include `atproto` scope |
| `unsupported_scope` | The authorization server does not offer a required scope |
| `par_failed` | The pushed authorization response was not usable (no `request_uri`, not JSON); an error the server answered with keeps its own code |
| `token_error` | A token response was not usable: not JSON, over 64 KiB, no access token, or not a DPoP token |
| `invalid_grant` | The refresh token has expired, been revoked, or already been used; sign in again |
| `use_dpop_nonce` | The server kept asking for a new DPoP nonce (the first request is retried automatically) |
| `no_refresh_token` | Refresh attempted without refresh token |
| `invalid_handle` | Handle contains invalid characters or format |
| `invalid_did` | The DID is malformed, or the identity fetch policy refuses it (a private host, a port, a path-based `did:web`) |
| `unsupported_did_method` | The DID uses a method other than `did:plc` / `did:web` |
| `handle_resolution_failed` | The handle resolves to no DID |
| `handle_resolution_conflict` | HTTPS and DNS resolution returned different DIDs |
| `did_resolution_failed` | The DID document could not be fetched, or is not the requested DID's |
| `pds_not_found` | The DID document declares no PDS service endpoint |
| `invalid_server_url` | A PDS, authorization server or endpoint URL the fetch policy refuses (not HTTPS, a query or fragment, a private address) |
| `invalid_resource_metadata` | Protected-resource metadata names no usable authorization server, or describes another resource |
| `metadata_fetch_failed` | Metadata could not be fetched: a refused address, a redirect, an error status, an oversized body |
| `invalid_metadata` | Metadata was not valid JSON, or authorization server metadata lacks a required field or capability, or names an unusable endpoint |
| `auth_server_mismatch` | The AS the DID resolves to isn't the one that issued the token (at the callback, or before a refresh) |
| `unsupported_dpop_alg` | The authorization server does not accept ES256 DPoP proofs |
| `unsupported_client_auth` | A confidential client's authorization server does not accept `private_key_jwt` with ES256 |
| `client_key_unavailable` | The session was issued to a client key the client no longer has |
| `verification_failed` | The account's authorization server could not be confirmed |
| `server_error` | The authorization server returned an error response without an OAuth error code |
