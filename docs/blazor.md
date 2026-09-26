# Blazor Integration

ATProto.NET provides Blazor components and server-side services for integrating AT Protocol OAuth into ASP.NET Core / Blazor Server applications. Authentication uses standard cookie-based auth — no custom `AuthenticationStateProvider` needed.

## Installation

```bash
dotnet add package ATProtoNet.Blazor
```

## Quick Start

Three lines in `Program.cs` and you're done:

```csharp
using ATProtoNet.Blazor;
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

// 2. Register AT Proto OAuth (auto-generates loopback client_id for development)
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientName = "My App";
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 3. Map AT Proto OAuth endpoints
app.MapAtProtoOAuth();

app.Run();
```

This maps three HTTP endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/atproto/login?handle=...` | GET | Starts OAuth flow, redirects to authorization server |
| `/atproto/callback` | GET | Handles OAuth callback, issues cookie, redirects to returnUrl |
| `/atproto/logout` | POST | Clears cookie, redirects to post-logout URL |

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
host) — anything else falls back to `DefaultReturnUrl`. See
[OAuth: State Parameter](oauth.md#state-parameter) for the same mechanism from the core client's
side.

## Components

### Login Form

The `<LoginForm>` component renders a ready-to-use login form that submits to the OAuth login endpoint:

```razor
@using ATProtoNet.Blazor.Components

<LoginForm ReturnUrl="/admin" />
```

The form includes:
- **Handle input** — the user's AT Protocol handle, labelled "Username" by default
- **PDS option** — optional checkbox to specify PDS URL manually (skips auto-discovery)
- **Error display** — automatically shows errors from failed OAuth callbacks
- **Submit button** — triggers the OAuth flow via the mapped endpoint

#### Parameters

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

#### Translations

Register an `IStringLocalizer<LoginForm>` (e.g. via `services.AddLocalization()`
and a `.resx` resource source) and the form will look up its default copy by
parameter name (`ButtonText`, `HandleLabel`, `HandleHint`, etc.). Explicit
parameter values still override the localizer; missing resource keys fall back
to the English defaults above.

The localizer is optional — `LoginForm` resolves it from the service provider
and renders the English defaults when no `IStringLocalizer<LoginForm>` is
registered, so apps that never call `AddLocalization()` need no extra setup.

### Using AuthorizeView

Standard Blazor `<AuthorizeView>` works automatically after login:

```razor
<AuthorizeView>
    <Authorized>
        <p>Welcome, @context.User.Identity?.Name!</p>
        <p>DID: @context.User.FindFirst("did")?.Value</p>
        <p>PDS: @context.User.FindFirst("pds_url")?.Value</p>

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

After login, the following claims are set on the user's `ClaimsPrincipal`:

| Claim | Description | Example |
|-------|-------------|---------|
| `ClaimTypes.NameIdentifier` | The user's DID | `did:plc:abc123` |
| `ClaimTypes.Name` | The user's handle | `alice.bsky.social` |
| `did` | The user's DID (convenience) | `did:plc:abc123` |
| `handle` | The user's handle (convenience) | `alice.bsky.social` |
| `pds_url` | The user's PDS URL | `https://bsky.social` |
| `auth_method` | Always `"oauth"` | `oauth` |

### Custom Claims

Override the default claims by providing a `ClaimsFactory`:

```csharp
builder.Services.AddAtProtoAuthentication(options =>
{
    // session is the OAuthSession the callback produced
    options.ClaimsFactory = session => new[]
    {
        new Claim(ClaimTypes.NameIdentifier, session.Did.Value),
        new Claim(ClaimTypes.Name, session.Handle.Value),
        new Claim(ClaimTypes.Role, session.Did.Value == "did:plc:myadmindid" ? "Admin" : "User"),
    };
});
```

## Configuration

### AtProtoOAuthServerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RoutePrefix` | `string` | `"/atproto"` | Route prefix for OAuth endpoints |
| `CookieScheme` | `string` | `"Cookies"` | Cookie authentication scheme name |
| `DefaultReturnUrl` | `string` | `"/"` | Default redirect after login |
| `PostLogoutRedirectUri` | `string` | `"/"` | Redirect after logout |
| `LoginPath` | `string` | `"/login"` | Redirect target on OAuth errors |
| `Scopes` | `string` | `"atproto transition:generic"` | OAuth scopes to request |
| `ClientName` | `string?` | — | App name shown on consent page |
| `BaseUrl` | `string?` | — | Explicit base URL (for reverse proxies) |
| `ClientMetadata` | `OAuthClientMetadata?` | — | Explicit client metadata (for production) |
| `ClaimsFactory` | `Func<OAuthSession, IEnumerable<Claim>>?` | — | Custom claims factory |
| `CookieExpiration` | `TimeSpan` | 7 days | Cookie lifetime |
| `IsPersistent` | `bool` | `true` | Persist cookie across sessions |
| `HttpClient` | `HttpClient?` | — | Client used for OAuth discovery, pushed authorization, token and revocation requests. Caller-owned: used as is, its `Timeout` is untouched and it is not disposed with the service |
| `HttpClientTimeout` | `TimeSpan` | 30 s | Timeout for the SDK-created OAuth `HttpClient`. Ignored when `HttpClient` is set |
| `HandleResolutionTimeout` | `TimeSpan` | 5 s | Budget per handle-resolution round. `Timeout.InfiniteTimeSpan` disables it |
| `AllowPrivateNetworks` | `bool` | `false` | Development opt-out for a local PDS or PLC: plain HTTP and private addresses in discovery and identity resolution. Never set it where users can name any handle, DID or PDS |

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

The `client_id` is auto-generated as `http://localhost?redirect_uri=...&scope=...` using the first request's URL.

### Production

For production, host a [client metadata JSON document](https://drafts.aaronpk.com/draft-parecki-oauth-client-id-metadata-document/) at a public HTTPS URL and provide it explicitly:

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
});
```

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

## Profile Card

```razor
<ProfileCard Actor="did:plc:abc123" />
<ProfileCard Actor="alice.bsky.social" />
```

## Feed View

Display a timeline feed:

```razor
<FeedView />
```

## Compose Post

```razor
<ComposePost OnPostCreated="HandlePost" />

@code {
    private void HandlePost(RecordRef response)
    {
        Console.WriteLine($"Posted: {response.Uri}");
    }
}
```

## Backend AT Proto Access

To also access AT Protocol APIs from backend code (API endpoints, services, or Blazor components),
add `ATProtoNet.Server`:

```bash
dotnet add package ATProtoNet.Server
```

```csharp
builder.Services.AddAtProtoServer(); // Registers IAtProtoSessionStore + IAtProtoClientFactory
```

This enables `IAtProtoClientFactory` to create authenticated `AtProtoClient` instances for logged-in users.
See [server.md](server.md) for full documentation.

## Examples

- [`samples/ServerIntegrationSample`](../samples/ServerIntegrationSample/) — OAuth login with `LoginForm`, plus backend AT Proto access
