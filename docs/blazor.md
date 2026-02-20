# Blazor Integration

ATProto.NET provides pre-built Blazor components for common AT Protocol / Bluesky UI patterns, including OAuth authentication.

## Installation

```bash
dotnet add package ATProtoNet.Blazor
```

## Service Registration

### Basic (App Password Authentication)

```csharp
// Program.cs
builder.Services.AddAtProtoBlazor();
```

This registers:
- `AtProtoClient` as a scoped service
- `AtProtoAuthStateProvider` as the `AuthenticationStateProvider`

### With OAuth

```csharp
// Program.cs
builder.Services.AddAtProtoBlazor(options =>
{
    options.InstanceUrl = "https://bsky.social";
    options.OAuth = new OAuthOptions
    {
        ClientMetadata = new OAuthClientMetadata
        {
            ClientId = "https://myapp.example.com/client-metadata.json",
            ClientName = "My AT Proto App",
            ClientUri = "https://myapp.example.com",
            RedirectUris = ["https://myapp.example.com/oauth/callback"],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            Scope = "atproto transition:generic",
            TokenEndpointAuthMethod = "none",
            ApplicationType = "web",
            DpopBoundAccessTokens = true,
        },
        Scope = "atproto transition:generic",
    };
});
```

This additionally registers:
- `OAuthClient` for handling the OAuth flow
- `HttpClient` named "ATProtoOAuth" for OAuth-specific HTTP requests

## Components

### Login Form

The login form supports both app password and OAuth authentication:

```razor
@using ATProtoNet.Blazor.Components

@* Basic login (app password) *@
<AtProtoLoginForm 
    OnLoginSuccess="HandleLogin"
    OnLoginError="HandleError" />

@* OAuth login *@
<AtProtoLoginForm
    OnLoginSuccess="HandleLogin"
    OnLoginError="HandleError"
    OAuthRedirectUri="https://myapp.example.com/oauth/callback"
    PreferOAuth="true" />

@code {
    private void HandleLogin(Session session)
    {
        Console.WriteLine($"Logged in as {session.Handle}");
        NavigationManager.NavigateTo("/dashboard");
    }

    private void HandleError(string error)
    {
        Console.WriteLine($"Login failed: {error}");
    }
}
```

#### Login Form Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `OnLoginSuccess` | `EventCallback` | Called after successful login |
| `OnLoginError` | `EventCallback<string>` | Called on login failure |
| `CssClass` | `string?` | CSS class for the form container |
| `OAuthRedirectUri` | `string?` | OAuth callback URL (enables OAuth) |
| `PreferOAuth` | `bool` | Default to OAuth mode |
| `AdditionalPdsOptions` | `List<PdsOption>?` | Extra PDS options for the dropdown |

The form includes:
- **PDS selector** — dropdown with bsky.social (default) and "Custom PDS…" option
- **Handle input** — user's AT Protocol handle
- **Password field** — only shown for app password login
- **OAuth toggle** — switch between OAuth and app password modes
- **Custom PDS URL** — shown when "Custom PDS…" is selected

### OAuth Callback

Add this component on your OAuth redirect URI page:

```razor
@page "/oauth/callback"
@using ATProtoNet.Blazor.Components

<OAuthCallback />
```

This component automatically:
1. Parses `code`, `state`, and `iss` query parameters from the URL
2. Calls `CompleteOAuthLoginAsync()` on the auth state provider
3. Redirects to `/` on success
4. Displays any errors

### Profile Card

```razor
<AtProtoProfileCard Actor="did:plc:abc123" />

<!-- Or with a handle -->
<AtProtoProfileCard Actor="alice.bsky.social" />
```

### Post Card

```razor
<AtProtoPostCard Post="@post" />

@code {
    private PostView? post;
}
```

### Feed View

Display a timeline feed:

```razor
<AtProtoFeedView />
```

### Compose Post

```razor
<AtProtoComposePost OnPostCreated="HandlePost" />

@code {
    private void HandlePost(CreateRecordResponse response)
    {
        Console.WriteLine($"Posted: {response.Uri}");
    }
}
```

## Authentication State

The `AtProtoAuthStateProvider` integrates with Blazor's `AuthorizeView`:

```razor
<AuthorizeView>
    <Authorized>
        <p>Welcome, @context.User.Identity?.Name!</p>
        <AtProtoFeedView />
    </Authorized>
    <NotAuthorized>
        <AtProtoLoginForm OnLoginSuccess="HandleLogin" />
    </NotAuthorized>
</AuthorizeView>
```

## Custom App with Blazor

Build a custom AT Protocol app using Blazor for the UI:

```razor
@page "/todos"
@inject AtProtoClient Client

<h1>My Todo App</h1>

@if (!Client.IsAuthenticated)
{
    <AtProtoLoginForm OnLoginSuccess="_ => StateHasChanged()" />
}
else
{
    <h2>@Client.Handle's Todos</h2>
    
    <input @bind="newTitle" placeholder="New todo..." />
    <button @onclick="AddTodo">Add</button>

    @foreach (var todo in todos)
    {
        <div>
            <input type="checkbox" checked="@todo.Value.Completed" 
                   @onchange="() => ToggleTodo(todo)" />
            <span>@todo.Value.Title</span>
            <button @onclick="() => DeleteTodo(todo)">×</button>
        </div>
    }
}

@code {
    private string newTitle = "";
    private List<RecordView<TodoItem>> todos = [];
    private RecordCollection<TodoItem>? collection;

    protected override async Task OnInitializedAsync()
    {
        if (Client.IsAuthenticated)
            await LoadTodos();
    }

    private async Task LoadTodos()
    {
        collection = Client.GetCollection<TodoItem>("com.example.todo.item");
        todos = [];
        await foreach (var item in collection.EnumerateAsync())
            todos.Add(item);
    }

    private async Task AddTodo()
    {
        if (string.IsNullOrWhiteSpace(newTitle) || collection is null) return;
        
        await collection.CreateAsync(new TodoItem { Title = newTitle });
        newTitle = "";
        await LoadTodos();
    }

    private async Task ToggleTodo(RecordView<TodoItem> todo)
    {
        if (collection is null) return;
        
        await collection.PutAsync(todo.RecordKey, new TodoItem
        {
            Title = todo.Value.Title,
            Completed = !todo.Value.Completed,
        });
        await LoadTodos();
    }

    private async Task DeleteTodo(RecordView<TodoItem> todo)
    {
        if (collection is null) return;

        await collection.DeleteAsync(todo.RecordKey);
        await LoadTodos();
    }
}
```
