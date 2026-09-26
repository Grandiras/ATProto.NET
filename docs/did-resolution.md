# Identity Resolution

ATProto.NET resolves DIDs (`did:plc`, `did:web`) to DID documents and handles to DIDs, checks
handles in both directions, caches documents, and fetches everything under an SSRF policy. The
pieces are interfaces, so consumers take whatever resolver an application configures:

| Interface | Default implementation | Does |
|---|---|---|
| `IDidResolver` | `CachingDidResolver` over `DidResolver` | DID → `DidDocument` |
| `IHandleResolver` | `HandleResolver` | handle → DID (DNS TXT + HTTPS well-known) |
| `IIdentityResolver` | `IdentityResolver` | DID or handle → verified `ResolvedIdentity` |

All of them live in `ATProtoNet.Identity` and report failure as one exception type,
`DidResolutionException`.

## Resolving an identity

```csharp
using ATProtoNet.Identity;

using var resolver = IdentityResolver.CreateDefault();

var identity = await resolver.ResolveAsync(AtIdentifier.Parse("atproto.com"));

Console.WriteLine(identity.Did);            // did:plc:ewvi7nxzyoun6zhxrhs64oiz
Console.WriteLine(identity.Handle);         // atproto.com, or handle.invalid
Console.WriteLine(identity.HandleVerified); // true
Console.WriteLine(identity.PdsEndpoint);    // https://…host.bsky.network
DidDocument document = identity.Document;
```

A handle is **verified** only when both directions agree: the DID document claims it in
`alsoKnownAs`, and the handle resolves back to the DID. Either half alone proves nothing —
anyone can claim any handle in their own document, and anyone can point their own domain at
someone else's DID. When verification fails, `Handle` is `Handle.Invalid` (`handle.invalid`)
and `HandleVerified` is `false`; render such an account by its DID or as `handle.invalid`, never
by the handle it claims. When the document claims no handle at all, `Handle` is `null`.

Resolving a DID never fails because the handle's authorities are unreachable: the DID is the
authoritative identifier, so the identity comes back with `handle.invalid`. Resolving a handle
that resolves to no DID throws `DidResolutionException` with `Kind == HandleNotFound`.

## Resolving DIDs

```csharp
using var didResolver = new CachingDidResolver();

DidDocument doc = await didResolver.ResolveAsync(Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur"));

Handle? claimed = doc.GetHandle();        // the claimed handle — not verified
string? signingKey = doc.GetSigningKey(); // the #atproto key as a did:key
Uri? pds = doc.GetPdsEndpoint();          // the #atproto_pds service
```

`DidResolver` dispatches on the method — `did:plc` to `PlcClient`, `did:web` to
`DidWebResolver` — and caches nothing. `CachingDidResolver` wraps any `IDidResolver`; its
parameterless constructor wraps a new `DidResolver`. Anything that resolves repeatedly (signature
verification, a service checking tokens) should use the cache.

`did:web` is hostname-level only: `did:web:example.com` fetches
`https://example.com/.well-known/did.json`. A path-based `did:web` is refused, and so is a port
on any host but `localhost` — which itself resolves only under the development opt-out below.

### Caching

| `DidCacheOptions` | Default | Meaning |
|---|---|---|
| `Capacity` | 10,000 | Documents held in memory; the least recently used goes first |
| `StaleAfter` | 1 hour | Served as is until then; after, served while refreshed in the background |
| `ExpireAfter` | 1 day | Refetched before use after this |
| `FailureTtl` | 1 minute | How long a failed resolution is remembered |
| `FailureCapacity` | 1,000 | Failures remembered, in an LRU of their own |
| `MinRefreshInterval` | 30 s | Least time between two fetches a forced refresh may start for one DID |
| `UseDistributedCache` | `false` | Back the cache with the registered `IDistributedCache` (DI only) |

Concurrent requests for a DID that is not cached share one fetch. Failures are remembered for
`FailureTtl`, so a DID that does not resolve — or a caller naming bogus DIDs — costs one fetch
per window rather than one per request. They are kept apart from documents, so a flood of bogus
DIDs pushes out older failures, never a cached document.

Two operations keep a cache honest:

- **`InvalidateAsync(did)`** drops a document. Call it when an identity changes, which the
  firehose announces as an `#identity` event; `TypedFirehoseConsumer` does this for its verifier.
- **`RefreshAsync(did)`** fetches past the cache. Call it once when a signature fails against a
  cached key, since the document may predate a key rotation — the sync spec asks for exactly
  that. Anyone can send a bad signature naming any DID, so a refresh fetches only when the
  last fetch of that DID, failed or not, is at least `MinRefreshInterval` old; within it the
  cached document (or the remembered failure) is returned.

`FirehoseVerifier`, `SpaceSyncer` and the space-server verifiers all follow this pattern
already. Both methods have default implementations on `IDidResolver`, so a resolver that caches
nothing needs neither.

To share documents between instances, pass an `IDistributedCache` to the `CachingDidResolver`
constructor, or register one and set `Cache.UseDistributedCache` (see
[dependency injection](#dependency-injection)). The distributed cache is consulted when memory
misses and written after every fetch; failures stay in memory, and distributed-cache errors are
logged and treated as misses. `InvalidateAsync` removes the shared copy too, and a write still in
flight when it runs is removed again once it lands, so an invalidated document does not come back
through the shared cache. It does not reach other instances' memory: each instance has to see
the `#identity` event itself (or wait out `StaleAfter`).

## Resolving handles

```csharp
using var handles = new HandleResolver();

Did? did = await handles.ResolveAsync(Handle.Parse("alice.bsky.social"));
```

The resolver queries both of the handle's authorities concurrently, under one
`HandleResolutionTimeout` budget (5 s by default):

1. DNS TXT at `_atproto.alice.bsky.social`, over DNS-over-HTTPS;
2. `GET https://alice.bsky.social/.well-known/atproto-did`.

The two are different trust roots — DNS and a TLS certificate — so when both answer they must
agree: a disagreement fails closed with `Kind == HandleConflict` rather than picking one, and so
do two distinct `did=` values in DNS. When only one answers, its answer is used; when neither
does, the result is `null`. The well-known request follows up to three HTTPS redirects on the
handle's own host; one to another host or to plain HTTP, a response larger than 2 KiB, or any
failure to read it counts as no answer. Handles under TLDs that never resolve (`.local`, `.localhost`,
`.internal`, `.arpa`, `.onion`, `.alt`, `.example`, `.invalid`) are not looked up; `.test` is,
under the development opt-out only.

.NET has no TXT lookup of its own, so the DNS query goes to a DNS-over-HTTPS endpoint, which
learns every handle resolved. It defaults to `https://dns.google/resolve`; point it at a resolver
you run or trust, or disable DNS resolution:

```csharp
var options = new IdentityResolverOptions
{
    DnsOverHttpsUrl = new Uri("https://cloudflare-dns.com/dns-query"), // or null for HTTPS only
};
```

## The fetch policy (SSRF)

Identity hosts come from identifiers anyone can mint, and the services that resolve them — a
space server checking a token, a firehose consumer, an OAuth login — do so on behalf of whoever
sent the identifier. Every identity fetch the SDK makes (a `did:web` document, a PLC lookup, a
handle's well-known, the DNS-over-HTTPS query) therefore follows one policy:

- **HTTPS only.**
- **Public addresses only, checked after DNS.** A connect callback resolves the host, refuses the
  connection if *any* address is loopback, private, link-local (including cloud metadata at
  `169.254.169.254`), CGNAT, multicast, documentation or otherwise reserved — IPv4 inside IPv6
  is judged by its IPv4 address — and connects to exactly the addresses it checked, so a
  rebinding DNS server cannot answer differently the second time. The hardened handler never
  uses a proxy, which would make the checked address the proxy's.
- **`did:web` is a fully qualified hostname**, with no path and no port.
- **No redirects.** A DID document is served where the identifier says or not at all; a
  redirect is an `HttpError`, and so is a response a caller's own client reached by following one.
- **Bounded responses and short timeouts.** DID documents are capped at 64 KiB
  (`MaxDidDocumentBytes`), a handle's well-known at 2 KiB, and each fetch has `RequestTimeout`
  (5 s).
- **Bounded concurrency.** At most 64 identity fetches run at once in a process; the rest wait
  for a slot within their own timeout.

A request the policy refuses fails with `Kind == Blocked` (or `InvalidDid` for an identifier AT
Protocol does not resolve) before anything is sent.

### Local development

`IdentityResolverOptions.AllowPrivateNetworks` is the explicit opt-out for a local PDS, a private
PLC mirror or a test network: it permits plain HTTP, private addresses,
`did:web:localhost%3A2583` (over `http://`) and `.test` handles.

```csharp
var options = new IdentityResolverOptions
{
    PlcDirectoryUrl = new Uri("http://localhost:2582"),
    AllowPrivateNetworks = true,
};
using var resolver = IdentityResolver.CreateDefault(options);
```

Never set it on a service that resolves identifiers it receives from other parties: that is the
request forgery the policy exists to stop. To reach one trusted private PLC mirror while keeping
the policy for everything else, give `PlcClient` your own `HttpClient`:

```csharp
var plc = new PlcClient(myHttpClient, new Uri("https://plc.internal.example"));
var didResolver = new CachingDidResolver(new DidResolver(plc, new DidWebResolver()));
```

A resolver constructed with your own `HttpClient` uses it as is: the address check lives in the
SDK's handler, while the identifier rules (hostname-only `did:web`, HTTPS) still apply.

## Dependency injection

`AddAtProtoIdentity` (in `ATProtoNet.Server`) registers `IDidResolver`, `IHandleResolver`,
`IIdentityResolver` and `ILexiconResolver` (see [Resolving lexicons](#resolving-lexicons)) as
singletons, so every consumer in the container shares one cache:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost");
builder.Services.AddAtProtoIdentity(o =>
{
    o.Cache.UseDistributedCache = true;
    o.DnsOverHttpsUrl = new Uri("https://cloudflare-dns.com/dns-query");
});
```

Registration is idempotent and the first call's options win. `AddAtProtoSpaces` calls it, and
the Blazor OAuth service picks up a registered `IIdentityResolver`, so call it first to
configure what they use. The space server fetches under these options but keeps a shorter-lived
cache of its own (`SpaceServerOptions.DidCache`: a hard 5-minute lifetime), because the documents it
caches back credential checks; see
[Permissioned Data](spaces.md).

## Errors

Every resolver throws `DidResolutionException`, an `AtProtoException`, including for a network
failure or a timeout, so a caller turning an unresolvable identity into its own error has one
thing to catch:

```csharp
try
{
    var doc = await didResolver.ResolveAsync(did);
}
catch (DidResolutionException ex) when (ex.Kind is DidResolutionErrorKind.NotFound or DidResolutionErrorKind.Deactivated)
{
    Console.WriteLine($"{ex.Did} does not exist any more");
}
```

| `DidResolutionErrorKind` | Meaning |
|---|---|
| `InvalidDid` | Not an identifier AT Protocol resolves: a path-based `did:web`, a port off `localhost`, an IP address for a host |
| `UnsupportedMethod` | Neither `did:plc` nor `did:web` |
| `Blocked` | The fetch policy refused the request (see above) |
| `NotFound` | No document (HTTP 404) |
| `Deactivated` | The DID existed but was deactivated — a tombstoned `did:plc` (HTTP 410) |
| `HttpError` | Any other unexpected status |
| `NetworkError` | The host could not be reached, or broke off mid-response |
| `Timeout` | No answer within `RequestTimeout` |
| `ResponseTooLarge` | The response exceeded the cap |
| `InvalidDocument` | Malformed JSON (including a `null` list entry), a body that does not decode, or an `id` other than the DID asked for |
| `HandleNotFound` | A handle identifier resolved to no DID |
| `HandleConflict` | The handle's authorities disagree |
| `OperationRejected` | The PLC directory rejected a submitted operation |

## The DID document model

`DidDocument` is immutable: resolvers cache and share documents.

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Did` | The DID |
| `Context` | `IReadOnlyList<string>?` | `@context`. A single string reads as one entry; inline context objects are skipped. Omitted when serializing unless set |
| `AlsoKnownAs` | `IReadOnlyList<string>` | Alternative identifiers, including `at://handle` |
| `VerificationMethod` | `IReadOnlyList<VerificationMethod>` | Public keys |
| `Service` | `IReadOnlyList<DidDocumentService>` | Services; `Endpoint` is `null` for a structured endpoint |

The three lists are never `null`: a missing or `null` list reads as empty, and a `null` entry in
one makes the document malformed.

| Method | Returns |
|---|---|
| `GetHandle()` | The handle in the first `at://` entry — claimed, not verified; `null` if that entry is not a valid handle |
| `GetSigningKey()` | The `#atproto` key as a `did:key` |
| `GetVerificationKey(fragment)` | Any verification method's key as a `did:key` |
| `GetServiceEndpoint(fragment, type?)` | A service's endpoint, when it is an absolute http(s) URL |
| `TryGetServiceEndpoint(fragment, type, out endpoint)` | The same, telling `Absent` from `Malformed` |
| `TryGetVerificationKey(fragment, out didKey)` | A key, telling `Absent` from `Malformed` (unknown type, missing or undecodable key material) |
| `GetPdsEndpoint()` | The `#atproto_pds` endpoint of type `AtprotoPersonalDataServer` |

Fragments match with or without their `#`, bare (`#atproto`) or DID-qualified
(`did:plc:…#atproto`). As in the reference implementation, the first entry with a matching id is
the one used: a later duplicate is never consulted, and a service whose type is not the one asked
for is `Malformed` rather than skipped. The key methods accept every verification-method type AT Protocol uses:
`Multikey`, whose value is already the `did:key` encoding, and the legacy
`EcdsaSecp256k1VerificationKey2019` / `EcdsaSecp256r1VerificationKey2019` forms, whose value is
a bare uncompressed point. `null` means the entry is absent or of a type the SDK does not
understand; a `FormatException` means its key material is malformed.

The `Get…` methods read an unusable entry as absent, which suits a lookup with a fallback (a space
host falling back to the PDS). Where a published entry must be used or refused instead, the
`TryGet…` methods return a `DidDocumentEntryStatus` — `Found`, `Absent` or `Malformed` — so a
broken entry does not quietly fall through to the fallback.

## The PLC directory

`PlcClient` also reads a DID's history and the directory's export:

```csharp
using var plc = new PlcClient(); // https://plc.directory, under the fetch policy
var did = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");

IReadOnlyList<PlcOperation> log = await plc.GetOperationLogAsync(did);
IReadOnlyList<PlcAuditEntry> audit = await plc.GetAuditLogAsync(did); // with CIDs, times, nullification
PlcOperation last = await plc.GetLastOperationAsync(did);
PlcOperation state = await plc.GetPlcDataAsync(did); // current state, without type/prev/sig
bool healthy = await plc.IsHealthyAsync();
```

A mirror follows every operation the directory accepts: page through `/export` by sequence
number, then follow `/export/stream` over a WebSocket from where the pages ended.

```csharp
long cursor = 0;
IReadOnlyList<PlcAuditEntry> page;
do
{
    page = await plc.ExportAsync(after: cursor, count: PlcClient.MaxExportCount);
    foreach (var entry in page)
        Apply(entry);
    if (page.Count > 0)
        cursor = page[^1].Seq!.Value;
} while (page.Count == PlcClient.MaxExportCount);

await foreach (var entry in plc.StreamExportAsync(cursor))
{
    Apply(entry);
    cursor = entry.Seq!.Value;
}
```

The stream ends quietly when the directory closes it normally or the connection drops; resume
from the last cursor. A close with a reason throws `PlcExportStreamException`: `OutdatedCursor`
(catch up with `ExportAsync` first), `FutureCursor` or `ConsumerTooSlow`.

`PlcClient(HttpClient, Uri directoryUrl)` sends everything through your client, to the directory
you name.

## Delegating resolution to a service

A client that should not resolve identities itself can ask its PDS or an AppView:

```csharp
IdentityInfo info = await client.Identity.ResolveIdentityAsync(AtIdentifier.Parse("atproto.com"));
ResolveDidResponse doc = await client.Identity.ResolveDidAsync(Did.Parse("did:plc:…"));
IdentityInfo refreshed = await client.Identity.RefreshIdentityAsync(AtIdentifier.Parse("atproto.com"));
```

These trust the service's answer. Failures are `XrpcException`s with `XrpcErrors.HandleNotFound`,
`DidNotFound` or `DidDeactivated`.

## Resolving lexicons

A Lexicon schema is published as a `com.atproto.lexicon.schema` record, keyed by its NSID, in the
repository its authority names in DNS. `LexiconResolver` resolves it the way the
[Lexicon specification](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution) and the
reference resolver (`@atproto/lex-resolver`) do:

1. **Authority.** The NSID without its name segment, reversed, is looked up as a DNS TXT record:
   `app.example.feed.post` → `_lexicon.feed.example.app`, which must hold exactly one `did=<did>`.
   There is no hierarchical walk: when that name has no record, resolution fails.
2. **Repository.** The DID resolves (through the `IDidResolver` you pass) to its PDS and signing key.
3. **Record.** `com.atproto.sync.getRecord` fetches the record with its proof, which is verified
   against the signing key (`RecordProof`), so a PDS cannot hand out a schema its account never
   committed. The record must be a `com.atproto.lexicon.schema` of language version 1 whose `id` is
   the NSID.

```csharp
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;

using var identity = IdentityResolver.CreateDefault();
using var network = new LexiconResolver(identity.DidResolver);
var lexicons = new CachingLexiconResolver(network);

ResolvedLexicon lexicon = await lexicons.ResolveAsync(Nsid.Parse("site.standard.document"));

Console.WriteLine(lexicon.Uri);             // at://did:plc:…/com.atproto.lexicon.schema/site.standard.document
Console.WriteLine(lexicon.Schema.MainType); // record
JsonElement main = lexicon.Schema.Defs!["main"];
```

The DNS query goes to `IdentityResolverOptions.DnsOverHttpsUrl` and every fetch runs under the
[fetch policy](#the-fetch-policy-ssrf), since the authority and its PDS come from records anyone can
publish. `ResolveAuthorityAsync(nsid)` does step 1 alone, and `ResolveAsync(nsid, authority)` skips
it, for a schema whose DNS record is not published yet.

Failures are `LexiconResolutionException`, with a `Kind`: `AuthorityNotFound` (no single `did=`
record), `ResolutionFailed` (DNS, the DID or the PDS failed), `NotFound` (nothing published under
the NSID), `InvalidRecord` (the proof does not verify, or it is not the schema asked for) and
`NotPermissionSet`.

`CachingLexiconResolver` caches any `ILexiconResolver`, on the model of `CachingDidResolver`:

| `LexiconCacheOptions` | Default | Meaning |
|---|---|---|
| `StaleAfter` | 5 minutes | Served as is until then; after, served while re-resolved in the background |
| `ExpireAfter` | 24 hours | Served at most this long, also while re-resolution keeps failing |
| `FailureTtl` | 1 minute | How long a failed resolution is remembered |
| `Capacity` | 1,000 | Schemas and failures held; the least recently used goes first |

The defaults follow the permission specification (re-resolve every few minutes, never serve a
stale set for more than a day) and keep DNS answers short-lived, as the Lexicon specification asks.
`InvalidateAsync(nsid)` drops a schema, for a consumer that sees its record change on the firehose.

To let a service resolve instead, use the client's `com.atproto.lexicon.resolveLexicon`: call
`client.Lexicon.ResolveLexiconAsync(nsid)`, or use `client.Lexicon` as the `ILexiconResolver`. That
trusts the service's answer.

### Permission sets

A permission set is a Lexicon whose `main` definition is a `permission-set`. An authorization server
fails a login whose `include:` scope names a set it cannot resolve, so an app can check its sets up
front:

```csharp
LexiconPermissionSet set = await lexicons.ResolvePermissionSetAsync(
    Nsid.Parse(AtProtoScopes.PermissionSets.FullApp));
Console.WriteLine(set.Title);

// Or straight from the scope string you are about to request.
await lexicons.ResolveIncludeScopeAsync(AtProtoScopes.Include(AtProtoScopes.PermissionSets.FullApp, aud));
```

Both throw `LexiconResolutionException` — `NotPermissionSet` when the Lexicon is something else.
The set's permissions are kept as published (`LexiconPermission`), since a server must ignore the
ones it cannot honor rather than reject the set.

## Next Steps

- [Cryptography](crypto.md) — Key generation, multikey encoding, did:key
- [Identity Types](identity-types.md) — DID, Handle, and other identity types
- [Firehose Streaming](firehose.md) — Commit signature verification against DID signing keys
