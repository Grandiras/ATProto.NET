# XRPC Endpoint Handlers

ATProtoNet.Server supports defining server-side XRPC endpoint handlers using a DI-friendly interface pattern. This is useful for building AT Protocol services (PDS, appview, relay, etc.) that expose `/xrpc/{nsid}` routes.

## Quick Start

### 1. Define an Endpoint

```csharp
using ATProtoNet.Server.Xrpc;

[XrpcEndpoint(Nsid = "com.example.getStatus")]
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public string Nsid => "com.example.getStatus";

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct)
    {
        return Task.FromResult(new StatusOutput { Status = "ok", Version = "1.0" });
    }
}

public class StatusOutput
{
    public string Status { get; set; } = "";
    public string Version { get; set; } = "";
}
```

### 2. Register and Map

```csharp
// Register endpoints
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

// Map all registered endpoints as /xrpc/{nsid} routes
app.MapXrpcEndpoints();
```

## Endpoint Types

### Query (GET)

For read-only operations:

```csharp
// Without parameters
[XrpcEndpoint(Nsid = "com.example.getStatus")]
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public string Nsid => "com.example.getStatus";

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct)
    {
        return Task.FromResult(new StatusOutput { Status = "ok" });
    }
}

// With query parameters
[XrpcEndpoint(Nsid = "com.example.search")]
public class SearchEndpoint : IXrpcQuery<SearchParams, SearchOutput>
{
    public string Nsid => "com.example.search";

    public Task<SearchOutput> HandleAsync(SearchParams parameters, HttpContext context, CancellationToken ct)
    {
        // parameters are bound from ?key=value query string
        var results = DoSearch(parameters.Query, parameters.Limit);
        return Task.FromResult(new SearchOutput { Results = results });
    }
}

public class SearchParams
{
    public string Query { get; set; } = "";
    public int Limit { get; set; } = 25;
}
```

### Procedure (POST)

For write operations:

```csharp
// With input and output
[XrpcEndpoint(Nsid = "com.example.createItem")]
public class CreateItemEndpoint : IXrpcProcedure<CreateItemInput, CreateItemOutput>
{
    public string Nsid => "com.example.createItem";

    public Task<CreateItemOutput> HandleAsync(CreateItemInput input, HttpContext context, CancellationToken ct)
    {
        // input is deserialized from the JSON request body
        var item = SaveItem(input);
        return Task.FromResult(new CreateItemOutput { Uri = item.Uri, Cid = item.Cid });
    }
}

// Fire-and-forget (no output)
[XrpcEndpoint(Nsid = "com.example.deleteItem")]
public class DeleteItemEndpoint : IXrpcProcedureVoid<DeleteItemInput>
{
    public string Nsid => "com.example.deleteItem";

    public Task HandleAsync(DeleteItemInput input, HttpContext context, CancellationToken ct)
    {
        DeleteItem(input.Uri);
        return Task.CompletedTask;
    }
}
```

## Registration

### Individual Registration

```csharp
builder.Services.AddXrpcEndpoint<GetStatusEndpoint>();
builder.Services.AddXrpcEndpoint<CreateItemEndpoint>();
```

### Assembly Scanning

Automatically discovers all classes with the `[XrpcEndpoint]` attribute:

```csharp
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);
```

## Route Mapping

```csharp
var app = builder.Build();
app.MapXrpcEndpoints();
```

This maps:
- `IXrpcQuery` → `GET /xrpc/{nsid}`
- `IXrpcProcedure` / `IXrpcProcedureVoid` → `POST /xrpc/{nsid}`

Invalid or missing request bodies return a `400 Bad Request` with an XRPC-style error response:

```json
{
  "error": "InvalidRequest",
  "message": "Missing or invalid request body"
}
```

## Dependency Injection

Endpoint handlers are resolved from DI, so you can inject services:

```csharp
[XrpcEndpoint(Nsid = "com.example.getProfile")]
public class GetProfileEndpoint : IXrpcQuery<ProfileParams, ProfileOutput>
{
    private readonly IProfileService _profiles;
    private readonly ILogger<GetProfileEndpoint> _logger;

    public GetProfileEndpoint(IProfileService profiles, ILogger<GetProfileEndpoint> logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    public string Nsid => "com.example.getProfile";

    public async Task<ProfileOutput> HandleAsync(ProfileParams parameters, HttpContext context, CancellationToken ct)
    {
        _logger.LogInformation("Fetching profile for {Did}", parameters.Did);
        return await _profiles.GetAsync(parameters.Did, ct);
    }
}
```

## Combining with PDS Hosting

You can use XRPC endpoint handlers alongside PDS hosting:

```csharp
builder.Services.AddAtProtoPds();
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

app.MapAtProtoPds();        // Core PDS endpoints
app.MapXrpcEndpoints();     // Custom XRPC endpoints
```

## Next Steps

- [PDS Hosting](pds.md) — Build a full PDS
- [Server Integration](server.md) — Backend AT Proto access patterns
- [ASP.NET Core](aspnet-core.md) — DI integration
