# Blazor Integration

`ATProtoNet.Blazor` has the components of a Blazor application whose users sign in with their AT
Protocol account: a login form, and widgets that read and write as the signed-in user. The sign-in
itself is the [OAuth cookie login](oauth.md#hosted-login-aspnet-core) of `ATProtoNet.Server`, which
the package brings along and which works for any ASP.NET Core application. Authentication uses
standard cookie-based auth — no custom `AuthenticationStateProvider` needed.

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

// 2. Register the AT Proto OAuth login (a loopback client_id for development), the client
//    factory and a session store (in memory, with a startup warning, unless you choose one)
builder.Services.AddAtProto()
    .WithOAuth(options => options.ClientName = "My App")
    .WithClientFactory()
    .WithFileSessionStore();

// 3. Register the widgets' user client
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

`MapAtProtoOAuth()` maps the login's endpoints (`/atproto/login`, `/atproto/callback`,
`/atproto/relay` and `/atproto/logout`). Their options, the claims the login issues, the login's
CSRF protection, and development and production client metadata are on
[OAuth: Hosted Login](oauth.md#hosted-login-aspnet-core).

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

The claims (`AtProtoClaimTypes`), and how to replace them with a `ClaimsFactory`, are listed on
[OAuth: Claims](oauth.md#claims).

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
the user changes, and disposes it with the scope. Each call checks that the session store still
holds the user's session, so a sign-out in another tab or request takes effect in a long-lived
circuit at once. When nobody is signed in, or no session is stored for the user, the widgets say so
instead of calling anything, and try again when the authentication state changes (with
`AddCascadingAuthenticationState()`) or their parent renders them again. Your own components can
inject the accessor too:

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

Once loaded, the feed loads again only when `FeedSource`, `PageSize` or the authentication state
changes; a load that failed, or found nobody signed in, is tried again the next time the parent
renders it. Give a `FeedView` a new `@key` to start it afresh, for example after the user posts.

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

`WithClientFactory()` registers `IAtProtoClientFactory` and the `IAtProtoSessionStore` it reads,
which API endpoints and services use to create authenticated `AtProtoClient` instances for
signed-in users. See [Acting as the Signed-In User](server.md), also for choosing the store.

## Examples

- [`samples/ServerIntegrationSample`](../samples/ServerIntegrationSample/) — OAuth login with `LoginForm`, `ProfileCard`, `FeedView` and `ComposePost`, plus backend AT Proto access
