# Identity

`ATProtoNet.Identity` resolves DIDs to their documents and handles to DIDs, checks a handle in
both directions, caches what it resolves, and talks to the PLC directory. Everything it fetches
goes through `ATProtoNet.Net.UntrustedHttp`, the one path to hosts that untrusted input names.
See the [DID](https://atproto.com/specs/did) and [handle](https://atproto.com/specs/handle)
specifications.

## Resolving an identity

Create one `UntrustedHttp` per application and build the default resolver over it. It caches
documents, resolves `did:plc` through `https://plc.directory` and `did:web` from the host's
`/.well-known/did.json`, and resolves handles over DNS (DNS-over-HTTPS) and HTTPS at once, DNS
winning when they disagree.

```csharp
using ATProtoNet;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Identity.Plc;
using ATProtoNet.Net;

using var http = new UntrustedHttp();
var resolver = IdentityResolverFactory.CreateDefault(http);

ResolvedIdentity alice = await resolver.ResolveAsync(Handle.Parse("alice.example.com"));
Did did = alice.Did;
Uri? pds = alice.Pds;
bool verified = alice.HandleVerified;            // the document claims the handle, and it resolves back
```

A handle is verified only when both directions agree. Resolving a DID never fails on its handle:
an unverifiable claim comes back as `Handle.Invalid` (`handle.invalid`), and a document claiming no
handle as `null`. Failures are `IdentityResolutionException` with an `Error` (`NotFound`,
`Deactivated`, `InvalidDocument`, `HandleNotFound`, `Unavailable`, …); a fetch failure is its inner
`UntrustedFetchException`.

`IdentityResolveOptions.BypassCache` resolves past every cache, for decisions the local user
initiates. `InvalidateAsync` forgets a DID's document or a handle's answer, for example on an
`#identity` event.

## DID documents

`DidDocument.Parse` tolerates the shapes real documents use and refuses values that cannot be
trusted. Lookups take the first entry with an id; `Get*` lookups read an unusable entry as absent,
`TryGet*` lookups report `Found`, `Absent` or `Malformed`, so a security decision can refuse a
broken entry rather than fall back.

```csharp continued
DidDocument document = alice.Document;
var signingKey = document.GetSigningKey();                                    // #atproto
var labelKey = document.TryGetVerificationKey(DidDocument.LabelKeyId);       // never falls back to #atproto
var kem = document.TryGetKemKey();                                           // the Spaces KEM key
if (kem.Status == DidDocumentEntryStatus.Found) { /* kem.Value is a KemPublicKey */ }
```

A space authority resolves like any account: its host is `GetPdsEndpoint()` and its credential key
`GetSigningKey()`.

## Verifying a signature

A cached document may predate a key rotation, so a signature is not declared bad before one
refresh. `VerifyWithRefreshAsync` refreshes at most once, and only retries when the key changed;
the caching resolver rate-limits refreshes per DID.

```csharp continued
var dids = new CachingDidResolver(new DidWebResolver(http));
byte[] message = [], signature = [];
var result = await dids.VerifyWithRefreshAsync(did, d => d.GetSigningKey(), key => key.Verify(message, signature));
```

## The PLC directory

`PlcClient` reads documents, operation logs, the audit log, state and the export, and submits
operations. Operations are typed (`PlcUpdate`, `PlcTombstone`, the read-only `PlcLegacyCreate`)
and signed with a rotation key; a genesis operation's DID derives from its signed bytes.

```csharp continued
using var rotation = SigningKey.Generate(KeyCurve.K256);
var genesis = SignedPlcOperation.Sign(new PlcUpdate
{
    RotationKeys = [rotation.PublicKey.DidKey],
    VerificationMethods = new Dictionary<string, DidKey> { ["atproto"] = rotation.PublicKey.DidKey },
    AlsoKnownAs = ["at://alice.example.com"],
    Services = new Dictionary<string, PlcService> { ["atproto_pds"] = new("AtprotoPersonalDataServer", new Uri("https://pds.example.com")) },
}, rotation);
Did created = genesis.DeriveDid();
var plc = new PlcClient(http);
var log = await plc.GetAuditLogAsync(did);
```

## The fetch policy

`UntrustedHttp` fetches only `https` on the default port, refuses connections to any non-public
address (checking every address a name resolves to), follows no redirects unless a caller allows
same-host ones, caps every body and runs each fetch under a total budget
(`UntrustedFetchPolicy.RequestTimeout`). `UntrustedFetchPolicy.AllowPrivateNetworks` lifts the
address and scheme rules for local development and logs a warning. In browser WebAssembly the
address check cannot run; `IsEnforced` says so.

Handle resolution over DNS-over-HTTPS discloses every looked-up handle to the DoH provider
(`https://dns.google/resolve` by default). Pass another `IDnsTxtResolver` in `IdentityDefaults.Dns`
to avoid that.
