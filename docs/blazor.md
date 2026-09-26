# Blazor Integration

ATProto.NET's Blazor support has two halves: the OAuth cookie login, which lives in
`ATProtoNet.Server` and works for any ASP.NET Core application, and the components in
`ATProtoNet.Blazor` — a login form, and widgets that read and write as the signed-in user.
Authentication uses standard cookie-based auth — no custom `AuthenticationStateProvider` needed.

## Installation

```bash
dotnet add package ATProtoNet.Blazor   # brings ATProtoNet.Server along
```

## Quick Start

```csharp
using ATProtoNet.Blazor;
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 1. Configure standard cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

// 2. Register the AT Proto OAuth login (a loopback client_id for development)
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientName = "My App";
});

// 3. Register the session store and client factory, and the widgets' user client
builder.Services.AddAtProtoServer();
builder.Services.AddAtProtoBlazor();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 4. Map AT Proto OAuth endpoints
app.MapAtProtoOAuth();

app.Run();
```

This maps four HTTP endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/atproto/login?handle=...` | GET | Starts OAuth flow, redirects to authorization server |
| `/atproto/callback` | GET | Handles OAuth callback, issues cookie, redirects to returnUrl |
| `/atproto/relay?code=...` | GET | Issues the cookie on the login's origin when the callback arrived on another loopback origin (development) |
| `/atproto/logout` | POST | Removes and revokes the session, clears cookie, redirects to post-logout URL |

### Login CSRF protection

The OAuth `state` parameter ties the callback to a pending authorization, not to a browser: on
its own, anyone who could get a victim to open a completed callback URL could sign them in as
whoever started that login. `/atproto/login` closes that gap itself, without any application
code: it gives the browser a random value in an HttpOnly, `SameSite=Lax` cookie (`Secure`, with
the `__Host-` prefix, on HTTPS) and keeps only its hash with the pending authorization; `/atproto/callback`
requires the same cookie, checks it against that hash, and deletes it once the login completes. A
callback that arrives without the matching cookie is refused (`login_not_bound`) and its tokens
are revoked. `returnUrl` is kept server-side with the pending authorization rather than in a
cookie, and only ever a local path (`/…`, never `//` or `/\`, which browsers can treat as another
host) — anything else falls back to `DefaultReturnUrl`. The callback only ever redirects to that
return URL, or to the relay on the login's own loopback origin (`AtProtoOAuthCallbackResult`); a
relayed login nobody redeems within two minutes has its tokens revoked. See
[OAuth: State Parameter](oauth.md#state-parameter) for the same mechanism from the core client's
side.

## Login Form

The `<LoginForm>` component renders a ready-to-use login form that submits to the OAuth login endpoint:

```razor
@using ATProtoNet.Blazor.Components

<LoginForm ReturnUrl="/admin" />
```

The form includes:
- **Handle input** — the user's AT Protocol handle, labelled "Username" by default
- **PDS option** — optional checkbox to specify PDS URL manually (skips auto-discovery)
- **Error display** — shows a message for the `error` code a failed login redirects with (see below)
- **Submit button** — triggers the OAuth flow via the mapped endpoint

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `LoginEndpoint` | `string` | `"/atproto/login"` | Login endpoint URL |
| `ReturnUrl` | `string` | `"/"` | Redirect URL after successful login |
| `CssClass` | `string?` | — | CSS class for the form container |
| `ShowPdsOption` | `bool` | `true` | Show "Specify PDS manually" checkbox |
| `HeadingText` | `string?` | — | Optional heading rendered above the form |
| `SubtitleText` | `string?` | — | Optional subtitle rendered above the form |
| `HandleLabel` | `string?` | `"Username"` | Label for the handle input |
| `HandlePlaceholder` | `string?` | `"alice.bsky.social"` | Placeholder text |
| `HandleHint` | `string?` | `"Your Atmosphere account username — your PDS is detected automatically."` | Hint text below input |
| `PdsCheckboxLabel` | `string?` | `"Specify PDS manually"` | PDS checkbox label |
| `PdsHint` | `string?` | `"Skip automatic PDS discovery and connect directly."` | PDS hint text |
| `ButtonText` | `string?` | `"Sign in with your Atmosphere account"` | Submit button text |

All labels are customizable — useful for localization. Default copy uses
"Atmosphere account" terminology (the community-facing umbrella term for the
AT Protocol ecosystem) and calls the identifier field a **username**, the word
every other sign-in form on the internet uses. The parameters, the `handle`
query parameter, and the `atproto-handle` input id keep the protocol's
vocabulary — only the copy a person reads changed. Override any parameter to
use different wording.

### Errors

A login that fails redirects to `LoginPath` with an `error` code, never a message (see
[OAuth: Hosted Login](oauth.md#oauth-flow)). `LoginForm` shows a message for the codes a user can
act on — `access_denied` (cancelled), `login_not_bound` (started in another browser),
`invalid_state` and `state_expired`, and the account-not-found codes (`invalid_handle`,
`handle_resolution_failed`, `did_resolution_failed`, `pds_not_found`, …) — and a generic one for
anything else. The code itself is never displayed, since anyone can link to the login page with
one. A localizer (below) can override each message under the key `Error_{code}`, and the generic
one under `Error`.

### Translations

Register an `IStringLocalizer<LoginForm>` (e.g. via `services.AddLocalization()`
and a `.resx` resource source) and the form will look up its default copy by
parameter name (`ButtonText`, `HandleLabel`, `HandleHint`, etc.). Explicit
parameter values still override the localizer; missing resource keys fall back
to the English defaults above.

The localizer is optional — `LoginForm` resolves it from the service provider
and renders the English defaults when no `IStringLocalizer<LoginForm>` is
registered, so apps that never call `AddLocalization()` need no extra setup.

## Using AuthorizeView

Standard Blazor `<AuthorizeView>` works automatically after login:

```razor
@using ATProtoNet.Server.Authentication

<AuthorizeView>
    <Authorized>
        <p>Welcome, @context.User.Identity?.Name!</p>
        <p>DID: @context.User.FindFirst(AtProtoClaimTypes.Did)?.Value</p>
        <p>PDS: @context.User.FindFirst(AtProtoClaimTypes.PdsUrl)?.Value</p>

        <form action="/atproto/logout" method="post">
            <button type="submit">Sign Out</button>
        </form>
    </Authorized>
    <NotAuthorized>
        <LoginForm />
    </NotAuthorized>
</AuthorizeView>
```

### Available Claims

After login, the following claims are set on the user's `ClaimsPrincipal`. The names are
constants on `AtProtoClaimTypes` (`ATProtoNet.Server.Authentication`):

| Claim | Description | Example |
|-------|-------------|---------|
| `ClaimTypes.NameIdentifier` | The user's DID | `did:plc:abc123` |
| `ClaimTypes.Name` | The user's handle, or `handle.invalid` when it does not verify | `alice.bsky.social` |
| `did` (`Did`) | The user's DID | `did:plc:abc123` |
| `handle` (`Handle`) | The user's handle, or the DID when it does not verify | `alice.bsky.social` |
| `handle_verified` (`HandleVerified`) | `"true"` or `"false"` | `true` |
| `pds_url` (`PdsUrl`) | The user's PDS URL | `https://bsky.social` |
| `auth_method` (`AuthMethod`) | Always `"oauth"` | `oauth` |

### Custom Claims

Override the default claims by providing a `ClaimsFactory`. Keep a `did` (or
`ClaimTypes.NameIdentifier`) claim: the client factory, the widgets and sign-out find the user's
session by it. They only look at the identity the login issues (authentication type `ATProto`),
never at a service auth identity carrying the same claim.

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    // session is the OAuthSession the callback produced
    options.ClaimsFactory = session => new[]
    {
        new Claim(ClaimTypes.NameIdentifier, session.Did.Value),
        new Claim(AtProtoClaimTypes.Did, session.Did.Value),
        new Claim(ClaimTypes.Name, session.Handle.Value),
        new Claim(ClaimTypes.Role, session.Did.Value == "did:plc:myadmindid" ? "Admin" : "User"),
    };
});
```

## Configuration

### AtProtoOAuthServerOptions

`AtProtoOAuthServerOptions` (`ATProtoNet.Server.Authentication`) configures the login:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RoutePrefix` | `string` | `"/atproto"` | Route prefix for OAuth endpoints |
| `CookieScheme` | `string` | `"Cookies"` | Cookie authentication scheme name |
| `DefaultReturnUrl` | `string` | `"/"` | Default redirect after login |
| `PostLogoutRedirectUri` | `string` | `"/"` | Redirect after logout |
| `LoginPath` | `string` | `"/login"` | Redirect target on OAuth errors, with an `error` code |
| `Scopes` | `string` | `"atproto transition:generic"` | OAuth scopes to request |
| `ClientName` | `string?` | — | App name shown on consent page |
| `BaseUrl` | `string?` | — | Explicit base URL: the callback is `{BaseUrl}{RoutePrefix}/callback` |
| `ClientMetadata` | `OAuthClientMetadata?` | — | Explicit client metadata (for production) |
| `ClientKeys` | `IList<OAuthClientKey>` | empty | Keys of a confidential (`private_key_jwt`) client; needs `ClientMetadata` |
| `ServeClientMetadata` | `bool` | `false` | Serve `ClientMetadata` at the path of its `client_id`, and the public keys at the path of its `jwks_uri` |
| `ClaimsFactory` | `Func<OAuthSession, IEnumerable<Claim>>?` | — | Custom claims factory |
| `CookieExpiration` | `TimeSpan` | 7 days | Cookie lifetime |
| `IsPersistent` | `bool` | `true` | Persist cookie across sessions |
| `HttpClient` | `HttpClient?` | — | Client used for OAuth discovery, pushed authorization, token and revocation requests. Caller-owned: used as is, its `Timeout` is untouched and it is not disposed with the service |
| `HttpClientTimeout` | `TimeSpan` | 30 s | Timeout for the SDK-created OAuth `HttpClient`. Ignored when `HttpClient` is set |
| `HandleResolutionTimeout` | `TimeSpan` | 5 s | Budget per handle-resolution round. `Timeout.InfiniteTimeSpan` disables it |
| `AllowPrivateNetworks` | `bool` | `false` | Development opt-out for a local PDS or PLC: plain HTTP and private addresses in discovery and identity resolution. Never set it where users can name any handle, DID or PDS |

The login's `OAuthClient` (`AtProtoOAuthService.Client`) is built from these options alone and
registered as the `OAuthClient` singleton, which the client factory refreshes and revokes sessions
with.

Without `HttpClient`, every request to a PDS or an authorization server (metadata, pushed
authorization, token, refresh, revocation) goes out under the identity fetch policy (public
addresses only, no redirects; see [OAuth](oauth.md#fetch-policy)). That owned client connects
directly and never through a proxy — the policy checks the address it connects to, and a proxy
would make that the proxy's address rather than the target's. An application that must reach the
internet through an egress proxy supplies its own `HttpClient` (which is then used as is, proxy
included) and relies on the proxy to keep requests off private addresses. A supplied `HttpClient`
carries every one of those requests as is.

Pending logins are limited per remote address, grouping an IPv6 address by its /64 (see
[OAuth](oauth.md#pending-authorizations)), so one address flooding `/atproto/login` only displaces
its own. Behind a reverse proxy or load balancer, that address is the proxy's unless the
application restores the client's with `app.UseForwardedHeaders()` (with the proxy listed in
`KnownProxies`/`KnownNetworks`) before `MapAtProtoOAuth()` runs — otherwise every user behind it
shares one requester's limit. To share pending logins between instances, or keep them across a
restart, register a state store; the service takes it from dependency injection:

```csharp
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = "localhost:6379");
builder.Services.AddSingleton<IOAuthStateStore>(sp => new DistributedCacheOAuthStateStore(
    sp.GetRequiredService<IDistributedCache>(),
    new DistributedCacheOAuthStateStoreOptions
    {
        Protect = protector.Protect,      // an IDataProtector: the entries hold DPoP private keys
        Unprotect = protector.Unprotect,
    }));
```

### Handle resolution timeouts

Handle resolution talks to a host named by the user (`https://<handle>/.well-known/atproto-did`), which may be parked or firewalled and silently drop traffic on port 443. The SDK runs that lookup alongside the DNS-over-HTTPS TXT lookup and bounds both with `HandleResolutionTimeout`, so a dead handle domain costs a few seconds instead of the `HttpClient` timeout. When an `IIdentityResolver` is registered (`AddAtProtoIdentity`), the service uses it instead, and its own options apply. Raise it for slow networks, or lower it for a snappier sign-in:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    options.HandleResolutionTimeout = TimeSpan.FromSeconds(3);
    options.HttpClientTimeout = TimeSpan.FromSeconds(20);
});
```

### Development (Loopback Client)

For development, the library auto-generates [loopback client metadata](https://atproto.com/specs/oauth#localhost-client-development). Just call `AddAtProtoAuthentication()` without explicit `ClientMetadata`:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientName = "My Dev App";
});
```

The `client_id` is `http://localhost?redirect_uri=...&scope=...`, with the callback on the
server's plain HTTP address on `127.0.0.1` (a `localhost` or any-address binding counts), or on
`BaseUrl` when that is set. It is fixed by the server's address rather than taken from a request,
so after a restart the stored sessions still refresh. Bind a plain HTTP address, such as
`http://127.0.0.1:5000` in `launchSettings.json`, even when the browser uses HTTPS: loopback
clients call back over HTTP, and the login relays the cookie to the browser's origin. Without one
(and without `BaseUrl`), the login fails with an `InvalidOperationException` saying so.

### Production

For production, host a [client metadata JSON document](https://drafts.aaronpk.com/draft-parecki-oauth-client-id-metadata-document/) at a public HTTPS URL and provide it explicitly; `ServeClientMetadata` has `MapAtProtoOAuth()` serve it at its `client_id`:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientMetadata = new OAuthClientMetadata
    {
        ClientId = "https://myapp.example.com/oauth-client-metadata.json",
        ClientName = "My App",
        ClientUri = "https://myapp.example.com",
        RedirectUris = ["https://myapp.example.com/atproto/callback"],
        GrantTypes = ["authorization_code", "refresh_token"],
        ResponseTypes = ["code"],
        Scope = "atproto transition:generic",
        TokenEndpointAuthMethod = "none",
        ApplicationType = "web",
        DpopBoundAccessTokens = true,
    };
    options.BaseUrl = "https://myapp.example.com";
    options.ServeClientMetadata = true;   // serves GET /oauth-client-metadata.json
});
```

A confidential client adds its `ClientKeys`; with a `JwksUri` in the metadata and
`ServeClientMetadata` on, `MapAtProtoOAuth()` serves their public halves there too. See
[OAuth: Hosted Login](oauth.md#production-configuration).

## Protecting Pages

Use `[Authorize]` on pages that require authentication:

```razor
@page "/admin"
@attribute [Authorize]

<h1>Admin Panel</h1>
<p>Only visible to authenticated users.</p>
```

Or with role-based authorization using a custom `ClaimsFactory`:

```razor
@attribute [Authorize(Roles = "Admin")]
```

## Widgets

`FeedView`, `PostCard`, `ProfileCard` and `ComposePost` act as the signed-in user. They get the
user's client from `AtProtoUserClientAccessor`, a scoped service `AddAtProtoBlazor()` registers:
it creates the client through `IAtProtoClientFactory` from the authentication state on first use,
shares it among the components of the circuit (or of a server-rendered request), replaces it when
the user changes, and disposes it with the scope. When nobody is signed in, or no session is stored
for the user, the widgets say so instead of calling anything. Your own components can inject the
accessor too:

```razor
@inject AtProtoUserClientAccessor UserClient

@code {
    protected override async Task OnInitializedAsync()
    {
        if (await UserClient.GetClientAsync() is { } client)   // do not dispose it
            _notifications = await client.Bsky.Notification.ListNotificationsAsync();
    }
}
```

### Feed View

```razor
<FeedView />  @* the signed-in user's home timeline *@
<FeedView FeedSource="@FeedSource.Author(AtIdentifier.Parse("alice.bsky.social"))" PageSize="30" />
<FeedView FeedSource="@FeedSource.Feed(AtUri.Parse("at://did:plc:abc/app.bsky.feed.generator/cats"))" />
<FeedView FeedSource="@FeedSource.List(AtUri.Parse("at://did:plc:abc/app.bsky.graph.list/3k2l"))" />
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `FeedSource` | `FeedSource` | `FeedSource.Timeline` | `Timeline`, `Author(actor)`, `Feed(generatorUri)` or `List(listUri)` |
| `PageSize` | `int` | `25` | Posts per page; "Load more" fetches the next |
| `OnReply` | `EventCallback<PostView>` | — | Reply clicked on a post |
| `OnRepost` | `EventCallback<PostView>` | — | Replaces the posts' own repost behaviour |
| `OnLike` | `EventCallback<PostView>` | — | Replaces the posts' own like behaviour |
| `CssClass` | `string?` | — | Extra class on the container |

The feed loads again only when `FeedSource` or `PageSize` changes. Give a `FeedView` a new `@key`
to start it afresh, for example after the user posts.

### Post Card

```razor
<PostCard Post="@post" OnReply="StartReply" />
```

Renders a `PostView`: author, text, image and link embeds, and reply, repost and like buttons.
Without an `OnLike` or `OnRepost` callback, the like and repost buttons like, repost, or undo either
as the signed-in user, and update their counts.

### Profile Card

```razor
<ProfileCard Actor="@AtIdentifier.Parse("alice.bsky.social")" />
<ProfileCard Profile="@profile" ShowBanner="false">
    <button>Follow</button>
</ProfileCard>
```

With `Profile`, the card shows it as given; with only `Actor`, it fetches that account's profile
as the signed-in user. `ShowBanner`, `ShowDescription` and `ShowStats` (all `true`) choose the
parts shown, and `ChildContent` renders at the bottom.

### Compose Post

```razor
<ComposePost OnPostCreated="HandlePost" />

@code {
    private void HandlePost(RecordRef post)
    {
        Console.WriteLine($"Posted: {post.Uri}");
    }
}
```

Posts as the signed-in user. The counter counts graphemes against the 300-grapheme limit of a
post, and the button stays disabled beyond it. `ReplyTo`, `Langs`, `Placeholder` and `Rows` set
the reply, the languages and the textarea.

### Styling

The components render plain HTML with `atproto-*` classes and no styles of their own. Include the
stylesheet the package ships for a minimal default look:

```html
<link rel="stylesheet" href="_content/ATProtoNet.Blazor/atproto-components.css" />
```

It styles only these classes, through the custom properties `--atproto-muted`, `--atproto-border`,
`--atproto-accent`, `--atproto-danger`, `--atproto-radius` and `--atproto-gap`, which you can set
on `:root` or on a container. Or leave it out and style the classes yourself; every widget also
takes a `CssClass` for its container.

| Component | Classes |
|-----------|---------|
| `FeedView` | `atproto-feed`, plus `atproto-feed-loading`, `atproto-feed-error` or `atproto-feed-signed-out` while it has no posts; `atproto-feed-empty`, `atproto-feed-load-more` |
| `PostCard` | `atproto-post`, `atproto-post-header`, `atproto-post-avatar`, `atproto-post-author`, `atproto-post-displayname`, `atproto-post-handle`, `atproto-post-time`, `atproto-post-content`, `atproto-post-text`, `atproto-post-embed`, `atproto-embed-images`, `atproto-embed-external`, `atproto-post-actions`, `atproto-post-action` (with `atproto-post-reply`, `atproto-post-repost` or `atproto-post-like`, and `active` once the user has reposted or liked), `atproto-post-error` |
| `ProfileCard` | `atproto-profile-card`, `atproto-profile-banner`, `atproto-profile-info`, `atproto-profile-avatar`, `atproto-profile-avatar-placeholder`, `atproto-profile-names`, `atproto-profile-displayname`, `atproto-profile-handle`, `atproto-profile-description`, `atproto-profile-stats`, `atproto-profile-loading`, `atproto-profile-error` |
| `ComposePost` | `atproto-compose`, `atproto-compose-textarea`, `atproto-compose-footer`, `atproto-compose-charcount` (with `over` past the limit), `atproto-compose-submit`, `atproto-compose-error` |
| `LoginForm` | `atproto-login-form`, `atproto-login-heading`, `atproto-login-subtitle`, `atproto-login-error`, `atproto-login-field`, `atproto-login-hint`, `atproto-login-button`, alongside Bootstrap's form classes |

## Backend AT Proto Access

`AddAtProtoServer()` registers `IAtProtoSessionStore` and `IAtProtoClientFactory`, which API
endpoints and services use to create authenticated `AtProtoClient` instances for logged-in users.
See [server.md](server.md) for full documentation.

## Examples

- [`samples/ServerIntegrationSample`](../samples/ServerIntegrationSample/) — OAuth login with `LoginForm`, `ProfileCard`, `FeedView` and `ComposePost`, plus backend AT Proto access
