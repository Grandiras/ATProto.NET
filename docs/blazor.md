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

## Components

### Login Form

The `<LoginForm>` component renders a ready-to-use login form that submits to the OAuth login endpoint:

```razor
@using ATProtoNet.Blazor.Components

<LoginForm ReturnUrl="/admin" />
```

The form includes:
- **Handle input** — user's AT Protocol handle
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
| `HandleLabel` | `string` | `"Handle"` | Label for the handle input |
| `HandlePlaceholder` | `string` | `"alice.bsky.social"` | Placeholder text |
| `HandleHint` | `string` | `"Your AT Protocol handle..."` | Hint text below input |
| `PdsCheckboxLabel` | `string` | `"Specify PDS manually"` | PDS checkbox label |
| `PdsHint` | `string` | `"Skip automatic PDS discovery..."` | PDS hint text |
| `ButtonText` | `string` | `"Sign in with AT Proto"` | Submit button text |

All labels are customizable — useful for localization.

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
    options.ClaimsFactory = result => new[]
    {
        new Claim(ClaimTypes.NameIdentifier, result.Did),
        new Claim(ClaimTypes.Name, result.Handle),
        new Claim(ClaimTypes.Role, result.Did == "did:plc:myadmindid" ? "Admin" : "User"),
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
| `ClaimsFactory` | `Func<...>?` | — | Custom claims factory |
| `CookieExpiration` | `TimeSpan` | 7 days | Cookie lifetime |
| `IsPersistent` | `bool` | `true` | Persist cookie across sessions |

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
        ClientId = "https://myapp.example.com/client-metadata.json",
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
    private void HandlePost(CreateRecordResponse response)
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
builder.Services.AddAtProtoServer(); // Registers IAtProtoTokenStore + IAtProtoClientFactory
```

This enables `IAtProtoClientFactory` to create authenticated `AtProtoClient` instances for logged-in users.
See [server.md](server.md) for full documentation.

## Examples

- [`samples/BlazorOAuthSample`](../samples/BlazorOAuthSample/) — Minimal OAuth login example
- [`samples/ServerIntegrationSample`](../samples/ServerIntegrationSample/) — Blazor + backend AT Proto access
