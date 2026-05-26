# DID Resolution

ATProto.NET supports resolving Decentralized Identifiers (DIDs) using `did:plc` and `did:web` methods, with a unified `DidResolver` that dispatches to the correct resolver automatically.

## Unified DID Resolution

```csharp
using ATProtoNet.Identity;

var resolver = new DidResolver();

// Resolves any DID method automatically
var doc = await resolver.ResolveAsync("did:plc:z72i7hdynmk6r22z27h6tvur");
var doc2 = await resolver.ResolveAsync("did:web:alice.example.com");

Console.WriteLine($"Handle: {doc.GetHandle()}");
Console.WriteLine($"PDS: {doc.GetPdsEndpoint()}");
```

The `DidResolver` dispatches:
- `did:plc:*` → `PlcClient` (PLC directory lookup)
- `did:web:*` → `DidWebResolver` (HTTPS well-known document)

## did:plc Resolution

`did:plc` identifiers are resolved via the PLC directory server:

```csharp
using ATProtoNet.Identity;

var plcClient = new PlcClient();

// Resolve a DID document
var doc = await plcClient.ResolveDidAsync("did:plc:z72i7hdynmk6r22z27h6tvur");

Console.WriteLine($"Handle: {doc.GetHandle()}");
Console.WriteLine($"PDS endpoint: {doc.GetPdsEndpoint()}");

// Access verification methods (signing keys)
foreach (var method in doc.VerificationMethod ?? [])
{
    Console.WriteLine($"Key: {method.Id} ({method.Type})");
    Console.WriteLine($"  Public key: {method.PublicKeyMultibase}");
}

// Access service endpoints
foreach (var service in doc.Service ?? [])
{
    Console.WriteLine($"Service: {service.Id} → {service.ServiceEndpoint}");
}
```

### PLC Operations

```csharp
// Get the operation log
var log = await plcClient.GetOperationLogAsync("did:plc:abc123");

// Get the audit log
var audit = await plcClient.GetAuditLogAsync("did:plc:abc123");

// Get the latest operation
var latest = await plcClient.GetLatestOperationAsync("did:plc:abc123");

// Get current PLC data
var data = await plcClient.GetPlcDataAsync("did:plc:abc123");

// Health check
bool healthy = await plcClient.IsHealthyAsync();
```

### Error Handling

```csharp
try
{
    var doc = await plcClient.ResolveDidAsync("did:plc:nonexistent");
}
catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    Console.WriteLine("DID not found in PLC directory");
}
```

## did:web Resolution

`did:web` identifiers are resolved by fetching a JSON document from the domain's well-known path:

```csharp
using ATProtoNet.Identity;

var webResolver = new DidWebResolver();

var doc = await webResolver.ResolveAsync("did:web:alice.example.com");
// Fetches https://alice.example.com/.well-known/did.json

Console.WriteLine($"Handle: {doc.GetHandle()}");
Console.WriteLine($"PDS: {doc.GetPdsEndpoint()}");
```

### Security

The `did:web` resolver includes several security protections:

- **SSRF prevention** — IP addresses (private ranges, loopback, link-local, CGN) are blocked
- **HTTPS enforcement** — Only HTTPS is used (localhost exception for development)
- **Document validation** — The `id` field in the document must match the requested DID
- **IPv6 bracket blocking** — All bracketed IP addresses are rejected

### Error Types

```csharp
using ATProtoNet.Identity;

try
{
    var doc = await webResolver.ResolveAsync("did:web:bad-host.example.com");
}
catch (DidWebException ex)
{
    switch (ex.ErrorKind)
    {
        case DidWebErrorKind.InvalidDid:
            Console.WriteLine("Invalid did:web format");
            break;
        case DidWebErrorKind.NotFound:
            Console.WriteLine("DID document not found (404/410)");
            break;
        case DidWebErrorKind.HttpError:
            Console.WriteLine($"HTTP error: {ex.Message}");
            break;
        case DidWebErrorKind.NetworkError:
            Console.WriteLine($"Network error: {ex.Message}");
            break;
        case DidWebErrorKind.ParseError:
            Console.WriteLine("Failed to parse DID document");
            break;
        case DidWebErrorKind.ValidationError:
            Console.WriteLine("DID document ID mismatch");
            break;
    }
}
```

## DID Document Model

The `DidDocument` model represents a resolved DID document:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | The DID |
| `AlsoKnownAs` | `List<string>?` | Alternative identifiers (handles) |
| `VerificationMethod` | `List<VerificationMethod>?` | Public keys |
| `Service` | `List<ServiceEndpoint>?` | Service endpoints |

### Convenience Methods

```csharp
// Get the handle from alsoKnownAs
string? handle = doc.GetHandle();
// Extracts from "at://alice.bsky.social" format

// Get the PDS endpoint
string? pdsUrl = doc.GetPdsEndpoint();
// Finds the #atproto_pds service endpoint
```

### Verification Methods

```csharp
foreach (var method in doc.VerificationMethod ?? [])
{
    Console.WriteLine($"ID: {method.Id}");         // e.g., "did:plc:abc#atproto"
    Console.WriteLine($"Type: {method.Type}");      // e.g., "Multikey"
    Console.WriteLine($"Controller: {method.Controller}");
    Console.WriteLine($"Key: {method.PublicKeyMultibase}");
}
```

### Service Endpoints

```csharp
foreach (var service in doc.Service ?? [])
{
    Console.WriteLine($"ID: {service.Id}");                    // e.g., "#atproto_pds"
    Console.WriteLine($"Type: {service.Type}");                // e.g., "AtprotoPersonalDataServer"
    Console.WriteLine($"Endpoint: {service.ServiceEndpoint}"); // e.g., "https://bsky.social"
}
```

## Handle Resolution

Resolve a handle to a DID (used internally by OAuth and identity resolution):

```csharp
// Via OAuth discovery (part of the OAuth flow)
var discovery = new AuthorizationServerDiscovery(httpClient, logger);
var did = await discovery.ResolveHandleToDidAsync("alice.bsky.social");
```

Handle resolution tries:
1. HTTPS well-known: `GET https://alice.bsky.social/.well-known/atproto-did`
2. DNS TXT fallback: `_atproto.alice.bsky.social`

## Next Steps

- [Cryptography](crypto.md) — Key generation, multikey encoding, did:key
- [Identity Types](identity-types.md) — DID, Handle, and other identity types
- [Firehose Streaming](firehose.md) — Commit signature verification against DID signing keys
