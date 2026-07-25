# Managed PDS

Run the official Bluesky PDS as a container your application owns, and administer it
from .NET. This lets your app sign users up on its own server instead of sending them
to an external provider — more PDSs on the network means more decentralization.

Three packages are involved:

| Package | Role |
|---|---|
| `ATProtoNet.Aspire.Hosting` | AppHost-side: adds the `ghcr.io/bluesky-social/pds` container to your Aspire app |
| `ATProtoNet` (core) | `PdsAdminClient` — programmatic admin access to any PDS you hold the password for |
| `ATProtoNet.Server` | `AddAtProtoPdsAdmin()` — DI registration that binds the Aspire-supplied configuration |

> ATProto.NET does **not** implement a PDS. It orchestrates the reference
> implementation and gives you a typed client for administering it.

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
using var client = pds.CreateClient();
await client.ResumeSessionAsync(account.AccessJwt, account.RefreshJwt);
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

## See also

- [.NET Aspire Integration](aspire.md) — client-side health checks and resilience
- [Server Integration](server.md) — token store and per-user client factory
- [`samples/ManagedPdsSample`](../samples/ManagedPdsSample) — signup API built on `PdsAdminClient`
- [`samples/ManagedPdsSample.AppHost`](../samples/ManagedPdsSample.AppHost) — the Aspire AppHost wiring it to a PDS container
- [PDS deployment docs](https://github.com/bluesky-social/pds) — the upstream reference implementation
