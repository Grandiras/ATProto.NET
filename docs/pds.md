# PDS Hosting

The `ATProtoNet.Pds` package lets you build your own AT Protocol Personal Data Server (PDS) using ASP.NET Core. It provides core business logic for account management, session handling, record CRUD, and blob operations.

## Installation

```bash
dotnet add package ATProtoNet.Pds
```

## Quick Start

```csharp
using ATProtoNet.Pds;

var builder = WebApplication.CreateBuilder(args);

// Register PDS services with in-memory stores (suitable for development)
builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "my-pds.example.com";
    options.PublicUrl = "https://my-pds.example.com";
    options.OpenRegistration = true;
});

var app = builder.Build();

// Map all AT Protocol XRPC endpoints
app.MapAtProtoPds();

app.Run();
```

This maps the following XRPC endpoints:

### Server Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.server.createAccount` | POST | Create a new account |
| `com.atproto.server.createSession` | POST | Login (create session) |
| `com.atproto.server.getSession` | GET | Get current session info |
| `com.atproto.server.refreshSession` | POST | Refresh session tokens |
| `com.atproto.server.describeServer` | GET | Server description |

### Repository Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.repo.createRecord` | POST | Create a record |
| `com.atproto.repo.getRecord` | GET | Get a record |
| `com.atproto.repo.putRecord` | POST | Create or update a record |
| `com.atproto.repo.deleteRecord` | POST | Delete a record |
| `com.atproto.repo.listRecords` | GET | List records with pagination |

### Blob Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.repo.uploadBlob` | POST | Upload a blob |
| `com.atproto.sync.getBlob` | GET | Download a blob |

### Identity Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.identity.resolveHandle` | GET | Resolve a hosted handle to its DID |
| `/.well-known/atproto-did` | GET | Handle → DID for the request host |
| `/.well-known/did.json` | GET | The `did:web` DID document for the request host |

### Sync Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `com.atproto.sync.getRepo` | GET | The whole repository as a CAR file |
| `com.atproto.sync.getLatestCommit` | GET | The current commit CID and revision |
| `com.atproto.sync.getRepoStatus` | GET | Hosting status and revision |
| `com.atproto.sync.listRepos` | GET | Every repository hosted here, with each account's hosting status |
| `com.atproto.sync.getRecord` | GET | A record plus its MST inclusion proof, as a CAR |
| `com.atproto.sync.getBlocks` | GET | Specific blocks, as a CAR |
| `com.atproto.sync.listBlobs` | GET | Blob CIDs for a repository |
| `com.atproto.sync.subscribeRepos` | WS | The firehose |

## Federation

A PDS federates when the rest of the network can *resolve* its identities, *verify* its repositories, and *follow* its updates. `ATProtoNet.Pds` does all three out of the box — the services are registered by `AddAtProtoPds()` and the endpoints mapped by `MapAtProtoPds()`.

The firehose needs WebSockets, so add `UseWebSockets()` ahead of routing:

```csharp
var app = builder.Build();

app.UseWebSockets();      // required for com.atproto.sync.subscribeRepos
app.MapAtProtoPds();
```

### Identities

Account creation mints a real identity rather than a placeholder DID.

For `did:plc` (the default) the PDS generates a rotation key and a repo signing key, builds a `plc_operation` genesis operation naming itself as the account's service endpoint, signs it, and derives the DID from the SHA-256 hash of that signed operation — the same derivation the directory performs, so the DID verifies against the operation.

Publishing to the directory is a separate, opt-in step:

```csharp
builder.Services.AddAtProtoPds(options =>
{
    options.DidMethod = PdsDidMethod.Plc;          // default
    options.PlcDirectoryUrl = "https://plc.directory";
    options.RegisterDidsWithPlc = true;            // actually submit the genesis operation
});
```

`RegisterDidsWithPlc` defaults to `false` so that neither your test suite nor a local development host writes to a public, append-only directory as a side effect of creating an account. Turn it on for a PDS the wider network must resolve.

The `did:web` method needs no directory at all — the DID is the handle's domain, and this PDS serves the document:

```csharp
options.DidMethod = PdsDidMethod.Web;   // alice.example.com → did:web:alice.example.com
```

Each handle must then be a domain resolving to this PDS, which serves `https://<handle>/.well-known/did.json` for it. Both methods serve `/.well-known/atproto-did`, so handle→DID resolution works either way.

To mint an identity without creating an account — during a migration, say — use `PdsIdentityService` directly, or build the operation yourself with the SDK's `PlcOperationBuilder`:

```csharp
using var rotationKey = AtProtoCrypto.GenerateP256Key();
using var signingKey  = AtProtoCrypto.GenerateP256Key();

var operation = PlcOperationBuilder.CreateGenesisOperation(
    [rotationKey.ToDidKey()], signingKey.ToDidKey(), "alice.example.com", "https://pds.example.com");

var signed = PlcOperationBuilder.Sign(operation, rotationKey);
Console.WriteLine(signed.Did);          // did:plc:…

using var plc = new PlcClient();
await plc.SubmitOperationAsync(signed);
```

> **Keep the rotation key.** It is stored on `PdsAccount.RotationKey` and controls the identity: whoever holds it can move the account to another PDS. It is deliberately separate from the signing key, which only signs commits.

### Repository structure

Every write rebuilds the repository into the structure relays expect:

- records are encoded as **DAG-CBOR** and addressed by real **CIDv1** (dag-cbor codec, SHA-256 multihash) — blobs get the raw codec;
- the record set is arranged into a **Merkle Search Tree** keyed by `collection/rkey`;
- the MST root is referenced by a **commit signed** with the account's repo signing key.

`com.atproto.sync.getRepo` serves that as a CAR file whose single root is the signed commit, so any consumer — including this SDK's own `CarReader`, `MerkleSearchTree` and `FirehoseVerifier` — can walk and verify it.

The MST is rebuilt from the full record set on every commit rather than mutated in place. That keeps `IRepoStore` free of block-storage concerns at the cost of an O(n) rebuild per write, which suits the self-hosted repositories this package targets. Since the MST is a pure function of its key/value set, the result is byte-identical to an incrementally maintained tree.

Two members on `IRepoStore` support this, both with default implementations that throw:

```csharp
Task<IReadOnlyList<RepoRecord>> ListAllRecordsAsync(string did, CancellationToken ct = default);
Task<IReadOnlyList<string>> ListBlobCidsAsync(string did, CancellationToken ct = default);
```

A store written before federation support keeps compiling and keeps serving repo CRUD; only the federation surface fails, with a message naming the member to implement. `InMemoryRepoStore` implements both.

`AddAtProtoPds()` always registers the federation services, so every write goes through `PdsRepoManager.CommitAsync`. To keep the promise above, that call **degrades instead of throwing** when the store cannot enumerate a repository: the write still succeeds, the gap is logged once as a warning, and `PdsRepoManager.IsRepositoryEnumerationUnsupported` reports it. Nothing is signed and nothing reaches the firehose — the PDS serves CRUD but does not federate. The `com.atproto.sync.*` endpoints, which genuinely cannot work without the record set, still surface the `NotSupportedException`.

### Repository heads

The signed head of each repository lives in `IRepoCommitStore`. The default `InMemoryRepoCommitStore` is fine for development, but **a federating PDS should persist heads**: if the store is lost, the next commit starts a fresh revision sequence and relays see the repository rewind.

```csharp
builder.Services.AddAtProtoPds<DatabaseAccountStore, DatabaseRepoStore, DatabaseCommitStore>(options =>
{
    options.Hostname = "my-pds.example.com";
});
```

Commit revisions come from `PdsRevisionGenerator`, which guarantees they strictly increase. `Tid.Next()` alone cannot: it resolves only to the millisecond and picks a random clock identifier, so two commits landing in the same millisecond would be ordered arbitrarily. The generator clamps against both the last value it issued and the stored head's revision, so revisions keep advancing across a restart.

### Firehose

Writes publish sequenced `#commit` events on `com.atproto.sync.subscribeRepos`; account creation publishes `#account` and `#identity` first, and account deletion publishes an inactive `#account`.

Each `#commit` carries a CAR of the commit block, the **covering proof** for the touched paths — the MST root plus the nodes on the way down to each of them — and the record blocks themselves. Frame size therefore grows with the number of operations and the log of the repository size, not with the repository. Commits whose CAR still exceeds `MaxFirehoseFrameBytes` (1 MiB by default) are published with `tooBig` set and no blocks, leaving consumers to fetch the repo through `getRepo`.

`PdsSequencer` keeps a bounded in-memory backlog for cursor replay:

```csharp
options.FirehoseBacklogCapacity = 4096;   // events retained for reconnecting relays
options.MaxFirehoseFrameBytes = 2 * 1024 * 1024;
```

A relay reconnecting with a cursor older than the retained window gets an `#info` frame naming `OutdatedCursor` and resumes from the oldest event still held; a cursor ahead of the server's sequence is a terminal `FutureCursor` error frame. A subscriber that falls more than 512 events behind is dropped rather than allowed to grow the buffer without bound — it is expected to reconnect with its last cursor.

Because the backlog is in memory, pass the highest sequence number you previously emitted when constructing the sequencer yourself, so a restarted process does not reuse numbers a relay has already consumed:

```csharp
builder.Services.AddSingleton(new PdsSequencer(backlogCapacity: 4096, startSeq: lastKnownSeq));
```

### Telling relays you exist

A relay only crawls a PDS it has been told about. Configure the relays and call `PdsCrawlNotifier` once the host is listening:

```csharp
builder.Services.AddAtProtoPds(options =>
{
    options.RelayHosts = ["bsky.network"];
});

var app = builder.Build();
app.UseWebSockets();
app.MapAtProtoPds();

_ = app.Lifetime.ApplicationStarted.Register(async () =>
{
    var notifier = app.Services.GetRequiredService<PdsCrawlNotifier>();
    foreach (var result in await notifier.RequestCrawlAsync())
    {
        if (!result.Success)
            app.Logger.LogWarning("requestCrawl to {Relay} failed: {Error}", result.Relay, result.Error);
    }
});

app.Run();
```

Failures are reported rather than thrown, so one unreachable relay neither stops the others from being notified nor prevents the host from starting.

### Federation checklist

- [ ] Serve over HTTPS at a stable `PublicUrl`, and make `Hostname` match.
- [ ] Persist `IAccountStore`, `IRepoStore` **and** `IRepoCommitStore`.
- [ ] Implement `ListAllRecordsAsync` and `ListBlobCidsAsync` on your `IRepoStore`.
- [ ] Choose a DID method; for `did:plc`, set `RegisterDidsWithPlc = true`.
- [ ] Back up account signing **and rotation** keys.
- [ ] `app.UseWebSockets()` before `MapAtProtoPds()`.
- [ ] Point handle domains at the PDS so `/.well-known/atproto-did` resolves.
- [ ] Configure `RelayHosts` and call `RequestCrawlAsync()` at startup.

### What is not implemented

- **Account migration** — `com.atproto.server.createAccount` with an existing DID stores the identity, but the `importRepo` / `activateAccount` / `deactivateAccount` flow is not implemented.
- **PLC update operations** — only genesis operations are built and submitted; rotating keys or changing a handle on an existing `did:plc` means constructing the update operation yourself.
- **Partial repo sync** — `getRepo` ignores the `since` parameter and always returns the full repository.
- **Firehose backlog persistence** — the replay window is in memory and bounded.

## Excluding or Overriding Individual Endpoints

By default `MapAtProtoPds()` maps every endpoint above. Pass a configuration callback to exclude the ones you want to implement yourself — the built-in route is then never registered, so mapping your own handler on the same path doesn't produce an ambiguous-match conflict:

```csharp
app.MapAtProtoPds(options => options.Exclude(PdsEndpointNames.CreateAccount));

// Your implementation now owns the route — a real endpoint, with routing
// metadata and route-level auth, not terminal middleware.
app.MapPost("/xrpc/com.atproto.server.createAccount", async (CreateAccountRequest req, IInviteStore invites) =>
{
    if (!await invites.RedeemAsync(req.InviteCode))
        return Results.Json(new { error = "InvalidInviteCode", message = "Invalid invite code." }, statusCode: 400);

    // ... delegate to PdsService for the rest
});
```

Use the `PdsEndpointNames` constants rather than raw strings — an unknown NSID throws `ArgumentException` at startup instead of silently doing nothing.

To map only a subset (for example a read-only mirror), use `Only`:

```csharp
app.MapAtProtoPds(options => options.Only(
    PdsEndpointNames.DescribeServer,
    PdsEndpointNames.GetRecord,
    PdsEndpointNames.ListRecords));
```

`Exclude` takes precedence over `Only`.

### Applying Route Conventions

Because the built-in routes are ordinary endpoints, you can attach authorization policies, endpoint filters, rate limiting, or metadata to them:

```csharp
app.MapAtProtoPds(options => options
    .Configure(PdsEndpointNames.UploadBlob, endpoint => endpoint.RequireRateLimiting("uploads"))
    .ConfigureAll((nsid, endpoint) => endpoint.WithMetadata(new XrpcNsidMetadata(nsid))));
```

Every mapped endpoint also carries its NSID as its display name, so it shows up as `com.atproto.repo.createRecord` in logs and diagnostics.

## Configuration

```csharp
builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "my-pds.example.com";
    options.PublicUrl = "https://my-pds.example.com";
    options.OpenRegistration = false;       // Require invite codes for signup
    options.AvailableUserDomains = [".my-pds.example.com"];
    options.ContactEmail = "admin@my-pds.example.com";

    // Federation — see the Federation section above
    options.DidMethod = PdsDidMethod.Plc;
    options.RegisterDidsWithPlc = true;
    options.RelayHosts = ["bsky.network"];
    options.SigningKeyCurve = KeyCurve.P256;         // K256 matches the reference PDS; needs OpenSSL
    options.FirehoseBacklogCapacity = 1024;
    options.MaxFirehoseFrameBytes = 1024 * 1024;
    options.ServeWellKnownDidDocument = true;
    options.ServeWellKnownHandle = true;

    // Persisted HMAC-SHA256 key for session tokens — see below
    options.SessionSigningKey = builder.Configuration["Pds:SessionSigningKey"];
});
```

### Session signing key

Access and refresh tokens are signed with an HMAC-SHA256 key. Set `SessionSigningKey` to a persisted base64 key:

```csharp
options.SessionSigningKey = builder.Configuration["Pds:SessionSigningKey"];
```

Generate one once and store it as a secret (environment variable, key vault, user secrets, …):

```csharp
Console.WriteLine(PdsSessionService.GenerateSigningKey()); // 32 random bytes, base64
```

If it is left unset, the PDS generates a **random key on every process start** and logs a warning at startup. Tokens signed with that key stop validating when the process exits, so every client is silently logged out on each restart or redeploy — fine for local development, not for production. A key that is set but not valid base64 throws `InvalidOperationException` when the `PdsSessionService` is resolved; keys shorter than 32 bytes are accepted but warned about.

If you already hold the key as bytes, you can still register the service yourself — the last registration wins:

```csharp
builder.Services.AddSingleton(sp =>
    new PdsSessionService(sp.GetRequiredService<PdsOptions>(), myKeyBytes));
```

## Custom Store Implementations

The in-memory stores are suitable for development and testing. For production, implement `IAccountStore` and `IRepoStore`:

### Account Store

```csharp
public class DatabaseAccountStore : IAccountStore
{
    private readonly MyDbContext _db;

    public DatabaseAccountStore(MyDbContext db) => _db = db;

    public async Task<AccountInfo?> GetByDidAsync(string did, CancellationToken ct)
    {
        var entity = await _db.Accounts.FindAsync([did], ct);
        return entity?.ToAccountInfo();
    }

    public async Task<AccountInfo?> GetByHandleAsync(string handle, CancellationToken ct)
    {
        var entity = await _db.Accounts.FirstOrDefaultAsync(a => a.Handle == handle, ct);
        return entity?.ToAccountInfo();
    }

    public async Task CreateAsync(AccountInfo account, CancellationToken ct)
    {
        _db.Accounts.Add(AccountEntity.FromAccountInfo(account));
        await _db.SaveChangesAsync(ct);
    }

    // ... VerifyPasswordAsync, etc.
}
```

### Repository Store

```csharp
public class DatabaseRepoStore : IRepoStore
{
    // Store records, blobs, with cursor-based pagination support
    // ...
}
```

### Register Custom Stores

```csharp
builder.Services.AddAtProtoPds<DatabaseAccountStore, DatabaseRepoStore>(options =>
{
    options.Hostname = "my-pds.example.com";
});
```

## Authentication

The PDS uses JWT Bearer tokens for authentication. `PdsSessionService` handles:

- Token issuance with HMAC-SHA256 signing (see [Session signing key](#session-signing-key))
- Token validation on protected endpoints
- Session refresh

Clients authenticate by calling `com.atproto.server.createSession` with handle/password, then include the returned JWT in the `Authorization: Bearer` header on subsequent requests.

## Security

- Passwords are hashed using PBKDF2 with 100,000 iterations
- Record CIDs are real CIDv1 content addresses (dag-cbor codec, SHA-256 multihash); blob CIDs use the raw codec
- Commits are signed with a per-account ECDSA key using low-S normalized signatures
- Repo ownership is verified on all write operations — users can only write to their own repository
- Blob uploads require authentication
- Account signing and rotation keys are stored unencrypted by `PdsAccount`; encrypt them at rest in your store implementation. The rotation key controls the identity itself
- `com.atproto.sync.*` and the well-known identity endpoints are unauthenticated by design — they serve public repository data. Use `PdsEndpointOptions.Configure` to add rate limiting

## Example: PDS with Custom Auth

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "my-pds.example.com";
    options.PublicUrl = "https://my-pds.example.com";
});

var app = builder.Build();

app.MapAtProtoPds();

// You can also add custom endpoints alongside the PDS
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
```

## Combining with ATProtoNet Client

You can test your PDS using the ATProto.NET client library:

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://my-pds.example.com")
    .Build();

// Create an account on your PDS
var session = await client.Server.CreateAccountAsync(
    email: "alice@example.com",
    handle: "alice.my-pds.example.com",
    password: "secure-password");

// Login
await client.LoginAsync("alice.my-pds.example.com", "secure-password");

// Create records on your PDS
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
await todos.CreateAsync(new TodoItem { Title = "Test PDS" });
```

## Next Steps

- [XRPC Endpoint Handlers](xrpc-handlers.md) — Add custom XRPC endpoints alongside PDS routes
- [Server Integration](server.md) — Backend AT Proto access patterns
- [Aspire](aspire.md) — Deploy with .NET Aspire
