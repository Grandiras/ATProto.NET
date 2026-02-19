# Blazor Integration

ATProto.NET provides pre-built Blazor components for common AT Protocol / Bluesky UI patterns.

## Installation

```bash
dotnet add package ATProtoNet.Blazor
```

## Service Registration

```csharp
// Program.cs
builder.Services.AddAtProtoBlazor();
```

This registers:
- `AtProtoClient` as a scoped service
- `AtProtoAuthStateProvider` as the `AuthenticationStateProvider`

## Components

### Login Form

```razor
@using ATProtoNet.Blazor.Components

<AtProtoLoginForm 
    InstanceUrl="https://your-pds.example.com"
    OnLoginSuccess="HandleLogin"
    OnLoginError="HandleError" />

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
