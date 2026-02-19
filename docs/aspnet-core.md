# ASP.NET Core Integration

ATProto.NET provides first-class ASP.NET Core integration through the `ATProtoNet.Server` package.

## Installation

```bash
dotnet add package ATProtoNet.Server
```

## Service Registration

### Singleton Client

Register a single shared `AtProtoClient`:

```csharp
// Program.cs
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

### Custom Session Store

```csharp
builder.Services.AddAtProto<DatabaseSessionStore>(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

### Scoped Client (Per-Request)

For multi-user scenarios where each request has its own session:

```csharp
builder.Services.AddAtProtoScoped(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

## JWT Authentication Handler

Validate AT Protocol JWTs on incoming requests:

```csharp
builder.Services.AddAuthentication()
    .AddScheme<AtProtoAuthenticationOptions, AtProtoAuthenticationHandler>(
        "AtProto", options =>
        {
            options.PdsUrl = "https://your-pds.example.com";
        });

builder.Services.AddAuthorization();
```

Then protect endpoints:

```csharp
app.MapGet("/api/protected", [Authorize(AuthenticationSchemes = "AtProto")] 
    (ClaimsPrincipal user) =>
{
    var did = user.FindFirstValue("did");
    return Results.Ok(new { did });
});
```

## Controller Example: Custom App

```csharp
[ApiController]
[Route("api/todos")]
public class TodoController : ControllerBase
{
    private readonly AtProtoClient _client;

    public TodoController(AtProtoClient client) => _client = client;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 50, [FromQuery] string? cursor = null)
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");
        var page = await todos.ListAsync(limit: limit, cursor: cursor);

        return Ok(new
        {
            items = page.Records.Select(r => new
            {
                key = r.RecordKey,
                uri = r.Uri,
                title = r.Value.Title,
                completed = r.Value.Completed,
            }),
            cursor = page.Cursor,
            hasMore = page.HasMore,
        });
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");

        try
        {
            var item = await todos.GetAsync(key);
            return Ok(item.Value);
        }
        catch (AtProtoHttpException ex) when (ex.ErrorType is "RecordNotFound")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TodoItem item)
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");
        var created = await todos.CreateAsync(item);

        return CreatedAtAction(nameof(Get),
            new { key = created.RecordKey },
            new { uri = created.Uri, key = created.RecordKey });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] TodoItem item)
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");
        var updated = await todos.PutAsync(key, item);

        return Ok(new { uri = updated.Uri, cid = updated.Cid });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");
        await todos.DeleteAsync(key);
        return NoContent();
    }
}
```

## Minimal API Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});

var app = builder.Build();

// Login on startup
var client = app.Services.GetRequiredService<AtProtoClient>();
await client.LoginAsync("service-account.example.com", "app-password");

var todos = client.GetCollection<TodoItem>("com.example.todo.item");

app.MapGet("/todos", async (int? limit, string? cursor) =>
{
    var page = await todos.ListAsync(limit: limit ?? 50, cursor: cursor);
    return Results.Ok(page);
});

app.MapPost("/todos", async (TodoItem item) =>
{
    var created = await todos.CreateAsync(item);
    return Results.Created(created.Uri, created);
});

app.Run();
```

## Multi-User Pattern

For apps where each user authenticates with their own AT Protocol account:

```csharp
// Create a per-request client
builder.Services.AddAtProtoScoped(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});

// Middleware to authenticate the AT Protocol session from a cookie/header
app.Use(async (context, next) =>
{
    var sessionJson = context.Request.Cookies["atproto_session"];
    if (sessionJson is not null)
    {
        var session = JsonSerializer.Deserialize<Session>(sessionJson);
        var client = context.RequestServices.GetRequiredService<AtProtoClient>();
        await client.ResumeSessionAsync(session!);
    }
    await next();
});
```
