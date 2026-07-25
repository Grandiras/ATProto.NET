# Managed PDS sample

A minimal API that provisions accounts on a PDS the application manages, using
`PdsAdminClient` from the core `ATProtoNet` package.

| Endpoint | What it does |
|---|---|
| `GET /pds` | Server DID, handle domains, whether invites are required |
| `POST /accounts` | Creates an account (minting an invite code if needed) |
| `GET /accounts/{did}` | Account details |
| `DELETE /accounts/{did}` | Deletes the account and its repository |
| `POST /invites` | Mints an invite code to hand to a user |

## Run it with Aspire

`../ManagedPdsSample.AppHost` is the AppHost for this sample — the whole of it:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var pds = builder.AddAtProtoPds("pds");

builder.AddProject<Projects.ManagedPdsSample>("api")
       .WithAtProtoPds(pds);

builder.Build().Run();
```

```bash
dotnet run --project ../ManagedPdsSample.AppHost
```

`WithAtProtoPds` injects `AtProto__Pds__Url` and `AtProto__Pds__AdminPassword`, which
`AddAtProtoPdsAdmin()` in `Program.cs` binds, and makes the API wait for the container
to report healthy. Needs a container runtime (podman or docker).

### Checking what a deployment would get

The AppHost can emit its manifest without starting anything, which is the quickest way
to see how the PDS is configured for a published environment:

```bash
dotnet run --project ../ManagedPdsSample.AppHost -- \
  --publisher manifest --output-path manifest.json
```

The local-only conveniences are deliberately absent there: `PDS_DEV_MODE` is unset (the
container defaults it off), and `PDS_HOSTNAME`, the JWT secret, and the PLC rotation key
become inputs the deployment must supply rather than values generated for a laptop.

## Run it standalone

Start the reference PDS yourself:

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

Then point the sample at it and run:

```bash
dotnet user-secrets set "AtProto:Pds:Url" "http://localhost:3000"
dotnet user-secrets set "AtProto:Pds:AdminPassword" "admin-password"
dotnet run
```

Create an account. Check `GET /pds` first for the domains the server accepts — with
`PDS_HOSTNAME=localhost` the reference PDS serves `.test`, not `.localhost`:

```bash
curl -X POST http://localhost:5000/accounts \
  -H 'Content-Type: application/json' \
  -d '{"handle":"alice.test","password":"correct-horse","email":"alice@example.com"}'
```
