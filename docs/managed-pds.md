# Managed PDS

Run a PDS as a container your application owns, and administer it from .NET. This lets
your app sign users up on its own server instead of sending them to an external
provider — more PDSs on the network means more decentralization.

Two implementations are supported:

| | Image | Add it with |
|---|---|---|
| **Reference PDS** — Bluesky's own server | `ghcr.io/bluesky-social/pds` | `AddAtProtoPds` |
| **[Tranquil PDS](https://tangled.org/tranquil.farm/tranquil-pds)** — a community server, a superset of the reference | `atcr.io/tranquil.farm/tranquil-pds` | `AddAtProtoTranquilPds` |

Three packages are involved:

| Package | Role |
|---|---|
| `ATProtoNet.Aspire.Hosting` | AppHost-side: adds either PDS container to your Aspire app |
| `ATProtoNet` (core) | `PdsAdminClient` — programmatic admin access to any PDS you hold credentials for |
| `ATProtoNet.Server` | `AddAtProtoPdsAdmin()` — DI registration that binds the Aspire-supplied configuration |

> ATProto.NET does **not** implement a PDS. It orchestrates other people's servers and
> gives you a typed client for administering them.

Most of this page describes the reference PDS. [Tranquil PDS](#tranquil-pds) covers
where the other one differs — chiefly in how administrators authenticate.

## Add the container

```bash
dotnet add package ATProtoNet.Aspire.Hosting
```

In your AppHost:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var pds = builder.AddAtProtoPds("pds");

builder.AddProject<Projects.Web>("web")
       .WithAtProtoPds(pds);

builder.Build().Run();
```

That gives you a PDS on a random host port, in dev mode, with a persistent data volume
and generated secrets — and a `web` project that waits for it to report healthy.
`samples/ManagedPdsSample.AppHost` is a working version of exactly this.

### Local defaults are not deployment defaults

Several conveniences apply only when running locally, because carrying them into a
deployment would produce a broken or unsafe server:

| Setting | Running locally | Publishing |
|---|---|---|
| `PDS_HOSTNAME` | `localhost` | a parameter the deployment supplies |
| `PDS_DEV_MODE` | `true` | unset — the container defaults it off |
| JWT secret / PLC rotation key | generated as hex, persisted | a parameter the deployment supplies |
| `AtProto__Pds__AllowInsecureHttp` | set by `WithAtProtoPds` | not set — see below |

The hostname matters most: it fixes the server's `did:web` identity and the domain new
handles are created under, so a PDS deployed as `localhost` would issue identities
nothing can resolve. Set it explicitly with `WithHostname("pds.example.com")`, or supply
the generated `{name}-hostname` parameter at deploy time.

### Secrets persist across runs

`AddAtProtoPds` creates three Aspire parameters, persisted to the AppHost's user secrets
when running locally:

| Parameter | Environment variable |
|---|---|
| `{name}-admin-password` | `PDS_ADMIN_PASSWORD` |
| `{name}-jwt-secret` | `PDS_JWT_SECRET` |
| `{name}-plc-rotation-key` | `PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX` |

Persistence matters: the data volume outlives any single run, so a rotation key that
changed between runs would strand the `did:plc` identities already stored in it, and a
changed JWT secret would invalidate every session.

The JWT secret and rotation key are generated **only when running locally**. The PDS
reads both as hex, and an Aspire manifest can only instruct a deployment to generate an
alphanumeric string — so when publishing, these two parameters carry no default and the
value must be supplied at deploy time. Otherwise a deployment would provision a rotation
key the server rejects.

Supply your own for anything beyond local development:

```csharp
var adminPassword = builder.AddParameter("pds-admin", secret: true);
var rotationKey = builder.AddParameter("pds-rotation-key", secret: true);

var pds = builder.AddAtProtoPds("pds")
    .WithAdminPassword(adminPassword)
    .WithPlcRotationKey(rotationKey);
```

### Configuration

```csharp
var pds = builder.AddAtProtoPds("pds", port: 3000, tag: "0.4")
    .WithHostname("pds.example.com")           // PDS_HOSTNAME — required when publishing
    .WithHandleDomains(".pds.example.com")     // domains new handles may use
    .WithPlcUrl("https://plc.directory")
    .WithAppView("https://api.bsky.app", "did:web:api.bsky.app")
    .WithCrawlers("https://bsky.network")
    .WithReportService("https://mod.bsky.app")
    .WithInviteCodeRequired()                  // gate signups behind invite codes
    .WithBlobUploadLimit(10 * 1024 * 1024)
    .WithEmail("smtps://user:pass@smtp.example.com", "noreply@example.com")
    .WithDataBindMount("./pds-data")           // host directory instead of a volume
    .WithProductionMode();                     // PDS_DEV_MODE=false
```

`WithDataVolume` and `WithDataBindMount` each replace the default data mount rather than
adding to it — two mounts on `/pds` are rejected by the container runtime, and the PDS
would never start.

On an SELinux host (Fedora, RHEL) a bind-mounted directory needs the container label
before the PDS can write to it, or it exits with `SqliteError: unable to open database
file`. Aspire mounts without relabelling, so either run `chcon -Rt container_file_t
./pds-data` first, or stay on the default named volume, which the runtime labels for you.

Handle domains are worth reading back rather than assuming: the server derives them
from its own configuration, and they are not simply `.{hostname}`. A default local PDS
(`PDS_HOSTNAME=localhost`) serves `.test`, so handles look like `alice.test`. Call
`DescribeServerAsync()` and read `AvailableUserDomains` to see what the running server
actually accepts.

## Administer it

`WithAtProtoPds(pds)` injects `AtProto__Pds__Url` and `AtProto__Pds__AdminPassword` into
the project. `AddAtProtoPdsAdmin()` binds them:

```csharp
using ATProtoNet.Admin;
using ATProtoNet.Server;

var builder = WebApplication.CreateBuilder(args);

builder.AddAtProtoPdsAdmin();   // registers PdsAdminClient as a typed HttpClient

var app = builder.Build();
```

`PdsAdminClient` is registered as a typed `HttpClient`, so it is transient and the
factory rotates the underlying handler. Inject it into an endpoint or controller, where
the request scope releases it; resolving it from the root provider keeps every instance
alive until shutdown.

Outside Aspire, set the same two configuration keys yourself, or construct the client
directly:

```csharp
using var pds = new PdsAdminClient("https://pds.example.com", adminPassword);
```

### Plaintext HTTP

The admin password grants full control of every account on the server, so the client
refuses to send it in the clear: the address must be HTTPS or a loopback host. It
validates the address requests actually go to, so supplying your own `HttpClient` with a
different `BaseAddress` does not slip past the check.

The PDS container itself serves plaintext HTTP, and a *containerized* consumer reaches it
over the container network rather than at a loopback address. `WithAtProtoPds` therefore
also sets `AtProto__Pds__AllowInsecureHttp` in **run** mode, where both resources sit on
one local network.

It deliberately does not do so when publishing. In a deployed environment, either front
the PDS with TLS, or opt in yourself once you are satisfied the hop is private:

```json
{
  "AtProto": {
    "Pds": {
      "AllowInsecureHttp": true
    }
  }
}
```

### Creating accounts

```csharp
app.MapPost("/signup", async (SignupForm form, PdsAdminClient pds) =>
{
    var account = await pds.CreateAccountAsync(new CreatePdsAccountRequest
    {
        Handle = $"{form.Username}.pds.example.com",
        Email = form.Email,
        Password = form.Password,
    });

    return Results.Ok(new { account.Did, account.Handle });
});
```

If the server requires invite codes, `CreateAccountAsync` mints one with the admin
credentials before signing the account up; pass `InviteCode` explicitly to use your own.
The signup call itself is sent unauthenticated — the admin password is never attached to
a public endpoint.

The response carries `AccessJwt` and `RefreshJwt`, a ready-to-use session for the new
account:

```csharp
using ATProtoNet.Auth;

using var client = pds.CreateClient();
await client.ResumeSessionAsync(new Session
{
    Did = account.Did,
    Handle = account.Handle,
    AccessJwt = account.AccessJwt,
    RefreshJwt = account.RefreshJwt,
});
```

### Managing accounts

```csharp
var server = await pds.DescribeServerAsync();     // DID, handle domains, invite policy

var code = await pds.CreateInviteCodeAsync();     // hand to a user to self-serve
var codes = await pds.CreateInviteCodesAsync(codeCount: 10);

var account = await pds.GetAccountAsync("did:plc:...");

await pds.UpdateAccountHandleAsync("did:plc:...", "newhandle.pds.example.com");
await pds.UpdateAccountEmailAsync("did:plc:...", "new@example.com");
await pds.UpdateAccountPasswordAsync("did:plc:...", newPassword);

await pds.TakedownAccountAsync("did:plc:...", reference: "report-42");
await pds.RestoreAccountAsync("did:plc:...");

await pds.DeleteAccountAsync("did:plc:...");      // permanent
```

For endpoints these wrappers do not cover, `pds.Admin` and `pds.Server` expose the raw
`com.atproto.admin.*` and `com.atproto.server.*` clients with the admin credentials
already applied:

```csharp
var invites = await pds.Admin.GetInviteCodesAsync(sort: "recent", limit: 50);
await pds.Admin.DisableAccountInvitesAsync("did:plc:...");
```

At the lowest level, `XrpcClient` carries the same HTTP Basic admin auth directly —
`SetAdminCredentials(password, user = "admin")`, `ClearAdminCredentials()`, and
`HasAdminCredentials`. A session token still takes priority when both are set, so an
admin-authenticated client that later logs in acts as that account.

## Tranquil PDS

[Tranquil](https://tangled.org/tranquil.farm/tranquil-pds) is a community PDS
implementation: a single Rust binary rather than the reference server's Node.js runtime,
and a superset of it — passkeys and 2FA, SSO, `did:web` accounts, granular OAuth scopes
with a consent UI, app passwords with the same scope system, account delegation, and a
built-in web UI. It speaks the same `com.atproto.*` API, so ordinary clients do not need
to know which one they are talking to.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var pds = builder.AddAtProtoTranquilPds("pds");

builder.AddProject<Projects.Web>("web")
       .WithAtProtoTranquilPds(pds);

builder.Build().Run();
```

The image lives on a registry that requires a login. Run `docker login atcr.io` (or
`podman login atcr.io`) once before the AppHost first starts it.

### It brings a database

Tranquil stores its repositories in PostgreSQL, so `AddAtProtoTranquilPds` also adds a
`{name}-postgres` server with a persistent data volume and a `{name}-db` database on it.
To use one you already have:

```csharp
var shared = builder.AddPostgres("postgres").AddDatabase("pds-db");

var pds = builder.AddAtProtoTranquilPds("pds").WithDatabase(shared);
```

`WithDatabaseUrl(...)` points it at a server outside the application model instead,
taking a `postgres://user:password@host:port/database` URI (or a parameter holding one).
Either call drops the generated PostgreSQL resources from the model, so nothing starts a
container the PDS will never connect to.

### Administrators are accounts, not a password

This is the one difference an application has to care about. The reference PDS has a
single server-wide `PDS_ADMIN_PASSWORD` used with HTTP Basic; Tranquil has no such
password, and instead flags individual **accounts** as administrators, authenticated with
an ordinary session token.

`PdsAdminClient` covers both — `PdsAdminOptions.Authentication` selects which:

| | `AdminPassword` (default) | `AdminAccount` |
|---|---|---|
| Server | reference PDS | Tranquil |
| Credential | the server's admin password | an administrator account's password |
| Sent as | `Authorization: Basic` | `Authorization: Bearer` from `createSession` |
| Also needs | — | `AdminIdentifier`, the account's handle or DID |

`WithAtProtoTranquilPds(pds)` sets all of it — `AtProto__Pds__Authentication`,
`AtProto__Pds__AdminIdentifier`, `AtProto__Pds__AdminPassword` — so the consuming project
still just calls `AddAtProtoPdsAdmin()` and injects `PdsAdminClient` as usual. The client
signs in lazily on the first admin call and re-authenticates if the server later rejects
its session.

### Creating the administrator account

**The account is not created for you.** Tranquil flags the *first* account registered on
an empty instance as an administrator, so your application creates it once — with the
handle and password the AppHost configured:

```csharp
app.MapPost("/bootstrap", async (PdsAdminClient pds, IConfiguration config) =>
{
    await pds.CreateAccountAsync(new CreatePdsAccountRequest
    {
        Handle = config["AtProto:Pds:AdminIdentifier"]!,
        Password = config["AtProto:Pds:AdminPassword"]!,
        Email = "admin@example.com",
    });
});
```

Signup is a public endpoint, so this works before the client has any admin authority at
all. Every later admin call authenticates as that account.

The handle defaults to `pdsadmin.{hostname}` — not `admin`, which Tranquil rejects as a
reserved subdomain. Override it, and the password, with
`WithAdminAccount("root.pds.example.com", passwordParameter)`.

### Local development defaults

Two of Tranquil's own defaults make a local container unusable, so
`AddAtProtoTranquilPds` overrides them when running (never when publishing):

| Setting | Why |
|---|---|
| `INVITE_CODE_REQUIRED=false` | An empty instance mints a bootstrap invite code and only writes it to its log, which no program can read |
| `DISABLE_ACCOUNT_VERIFICATION_GATE=true` | Login is blocked until an account has a verified communication channel, and a container with no mail server can never get one |
| `PDS_AGE_ASSURANCE_OVERRIDE=true` | Skips the age-assurance birthday prompt |
| `ALLOW_HTTP_PROXY=true` | Traffic between AppHost containers is plaintext |
| `DISABLE_RATE_LIMITING=true` | Login is rate limited per IP, and the AppHost network shares one |

`WithDevelopmentMode(false)` turns the whole set off — do that once the AppHost has a
mail server the PDS can verify addresses through, so accounts pass the gate honestly.
Individual settings can be overridden on their own; a later `WithInviteCodeRequired()`
wins over the value above.

### Secrets

| Parameter | Environment variable | |
|---|---|---|
| `{name}-jwt-secret` | `JWT_SECRET` | signs session JWTs |
| `{name}-dpop-secret` | `DPOP_SECRET` | validates OAuth DPoP proofs |
| `{name}-master-key` | `MASTER_KEY` | encrypts every account's signing key |
| `{name}-admin-password` | — | the administrator account's password |
| `{name}-postgres-password` | `POSTGRES_PASSWORD` | the database |

All are generated at 48 alphanumeric characters — over the 32 Tranquil requires outside
dev mode — and persisted to the AppHost's user secrets when running locally. Unlike the
reference PDS's hex-parsed secrets, these are opaque strings a manifest `generate` block
describes exactly, so a published deployment can produce its own rather than being handed
one the server would reject.

Changing `MASTER_KEY` on a server that already has accounts makes their signing keys
undecryptable. Override any of them with `WithJwtSecret`, `WithDPoPSecret`,
`WithMasterKey`, or the second argument to `WithAdminAccount`.

### Configuration

```csharp
var pds = builder.AddAtProtoTranquilPds("pds", port: 3000)
    .WithHostname("pds.example.com")            // PDS_HOSTNAME — required when publishing
    .WithHandleDomains("pds.example.com")       // defaults to the hostname
    .WithPlcUrl("https://plc.directory")
    .WithPlcRecoveryKey("did:key:z...")         // a *public* did:key, unlike the reference PDS
    .WithCrawlers("https://bsky.network")
    .WithReportService("https://mod.bsky.app")
    .WithBlobUploadLimit(10 * 1024 * 1024)
    .WithBlobVolume()                           // or WithBlobBindMount / WithS3BlobStorage
    .WithEmail("noreply@example.com", "smtp.example.com", userName: "pds", password: smtpPassword)
    .WithInviteCodeRequired()
    .WithDevelopmentMode(false);
```

`WithPlcRecoveryKey` is not the reference PDS's `WithPlcRotationKey`: Tranquil keeps
signing PLC operations with each account's own key and adds this one to the rotation keys
purely so the operator can recover an identity, so it takes a public `did:key` rather than
a hex private key.

## Without Aspire

`PdsAdminClient` works against any PDS. To run the container by hand:

```bash
podman run -d --name pds -p 3000:3000 \
  -e PDS_HOSTNAME=localhost \
  -e PDS_DEV_MODE=true \
  -e PDS_DATA_DIRECTORY=/pds \
  -e PDS_BLOBSTORE_DISK_LOCATION=/pds/blocks \
  -e PDS_ADMIN_PASSWORD=admin-password \
  -e PDS_JWT_SECRET=$(openssl rand -hex 16) \
  -e PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX=$(openssl rand -hex 32) \
  -v pds-data:/pds \
  ghcr.io/bluesky-social/pds:latest
```

Keep the JWT secret and rotation key stable across restarts if you reuse the volume.

Tranquil needs a PostgreSQL server alongside it; its repository ships a
`docker-compose.prod.yaml` that runs both. Point the client at it with:

```csharp
using var pds = new PdsAdminClient(
    new PdsAdminOptions
    {
        Url = "https://pds.example.com",
        Authentication = PdsAdminAuthentication.AdminAccount,
        AdminIdentifier = "pdsadmin.pds.example.com",
        AdminPassword = adminAccountPassword,
    },
    httpClient: null,
    logger: null);
```

## See also

- [.NET Aspire Integration](aspire.md) — client-side health checks and resilience
- [Server Integration](server.md) — token store and per-user client factory
- [`samples/ManagedPdsSample`](../samples/ManagedPdsSample) — signup API built on `PdsAdminClient`
- [`samples/ManagedPdsSample.AppHost`](../samples/ManagedPdsSample.AppHost) — the Aspire AppHost wiring it to a PDS container
- [PDS deployment docs](https://github.com/bluesky-social/pds) — the upstream reference implementation
- [Tranquil PDS](https://tangled.org/tranquil.farm/tranquil-pds) — the upstream community implementation
