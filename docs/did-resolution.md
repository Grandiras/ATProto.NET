# DID Resolution

ATProto.NET supports resolving Decentralized Identifiers (DIDs) using `did:plc` and `did:web` methods, with a unified `DidResolver` that dispatches to the correct resolver automatically.

## Unified DID Resolution

```csharp
using ATProtoNet.Identity;

var resolver = new DidResolver();

// Resolves any DID method automatically
var doc = await resolver.ResolveDidAsync("did:plc:z72i7hdynmk6r22z27h6tvur");
var doc2 = await resolver.ResolveDidAsync("did:web:alice.example.com");

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
foreach (var method in doc.VerificationMethod)
{
    Console.WriteLine($"Key: {method.Id} ({method.Type})");
    Console.WriteLine($"  Public key: {method.PublicKeyMultibase}");
}

// Access service endpoints
foreach (var service in doc.Service)
{
    Console.WriteLine($"Service: {service.Id} → {service.Endpoint}");
}
```

### PLC Operations

```csharp
// Get the operation log
var log = await plcClient.GetOperationLogAsync("did:plc:abc123");

// Get the audit log
var audit = await plcClient.GetAuditLogAsync("did:plc:abc123");

// Get the latest operation
var latest = await plcClient.GetLastOperationAsync("did:plc:abc123");

// Get current PLC data
var data = await plcClient.GetPlcDataAsync("did:plc:abc123");

// Health check
bool healthy = await plcClient.IsHealthyAsync();
```

### Error Handling

`PlcClient` surfaces directory failures as `PlcException`, with a `Kind` describing what went wrong:

```csharp
try
{
    var doc = await plcClient.ResolveDidAsync("did:plc:nonexistent");
}
catch (PlcException ex) when (ex.Kind == PlcErrorKind.NotFound)
{
    Console.WriteLine("DID not found in PLC directory");
}
catch (PlcException ex) when (ex.Kind == PlcErrorKind.Tombstoned)
{
    Console.WriteLine("DID has been deleted");
}
```

| `PlcErrorKind` | Meaning |
|---|---|
| `NotFound` | The directory returned 404 for this DID |
| `Tombstoned` | The DID has been deleted (HTTP 410) |
| `ParseError` | The response could not be parsed, or its `id` did not echo the requested DID |
| `InvalidOperation` | The directory rejected a submitted operation |

## did:web Resolution

`did:web` identifiers are resolved by fetching a JSON document from the domain's well-known path:

```csharp
using ATProtoNet.Identity;

var webResolver = new DidWebResolver();

var doc = await webResolver.ResolveDidAsync("did:web:alice.example.com");
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
    var doc = await webResolver.ResolveDidAsync("did:web:bad-host.example.com");
}
catch (DidWebException ex)
{
    switch (ex.Kind)
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
| `Context` | `List<string>?` | The `@context` field. Omitted when serializing unless set — required when *publishing* a document (e.g. a `did:web` `/.well-known/did.json`), ignorable when consuming one |
| `AlsoKnownAs` | `List<string>` | Alternative identifiers (handles) |
| `VerificationMethod` | `List<VerificationMethod>` | Public keys |
| `Service` | `List<ServiceEndpoint>` | Service endpoints |

The three list properties default to empty rather than null, so they can be enumerated directly.

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
foreach (var method in doc.VerificationMethod)
{
    Console.WriteLine($"ID: {method.Id}");         // e.g., "did:plc:abc#atproto"
    Console.WriteLine($"Type: {method.Type}");      // e.g., "Multikey"
    Console.WriteLine($"Controller: {method.Controller}");
    Console.WriteLine($"Key: {method.PublicKeyMultibase}");
}
```

To get a key as a `did:key` — ready for `AtProtoCrypto.VerifySignature` — use the document
helpers rather than reading `PublicKeyMultibase` directly. They accept every verification
method type AT Protocol uses: `Multikey`, whose value is already the `did:key` encoding, and
the legacy `EcdsaSecp256k1VerificationKey2019` / `EcdsaSecp256r1VerificationKey2019` forms,
whose value is a bare uncompressed point that gets compressed and multicodec-tagged first.

```csharp
string? signingKey = doc.GetSigningKey();            // the #atproto repo-signing key
string? spaceKey = doc.GetVerificationKey("#atproto_space");
string? sameKey = doc.VerificationMethod[0].ToDidKey();
```

`null` means the entry is absent or its type is not one the SDK understands; a `FormatException`
means the entry is present but its key material is malformed.

### Service Endpoints

```csharp
foreach (var service in doc.Service)
{
    Console.WriteLine($"ID: {service.Id}");                    // e.g., "#atproto_pds"
    Console.WriteLine($"Type: {service.Type}");                // e.g., "AtprotoPersonalDataServer"
    Console.WriteLine($"Endpoint: {service.Endpoint}");        // e.g., "https://bsky.social"
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
