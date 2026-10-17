# Stores — spec

**Status:** Draft · **Packages:** interfaces and in-memory implementations next to their
consumers (`ATProtoNet`, `ATProtoNet.Streaming`); file and distributed-cache implementations in
`ATProtoNet.Hosting`; EF Core implementations in `ATProtoNet.EntityFrameworkCore` (Spaces stores
in `ATProtoNet.Spaces.EntityFrameworkCore`) · **Phase:** interfaces with their consumers
(5, 6, 7); file, distributed and EF implementations in 9 ·
**Normative references:** [D-15](../06-decisions.md#d-15-stores-are-interfaces-in-the-package-that-uses-them-implementations-live-elsewhere),
[D-22](../06-decisions.md#d-22-session-stores-support-compare-and-swap),
[ASP.NET Core Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/introduction)

## Scope

Every persistence seam of the stable packages: what each interface promises, which
implementations ship, how they encrypt, and the contract test suites every implementation runs.

| Interface | Defined in | Consumer | In-memory | File | Distributed cache | EF Core |
|---|---|---|---|---|---|---|
| `ISessionStore` | `ATProtoNet` (`ATProtoNet.Auth`) | sessions, client factory, OAuth login | yes | yes | **no** (STO-5) | yes |
| `IOAuthStateStore` | `ATProtoNet` (`ATProtoNet.Auth.OAuth`) | `OAuthClient` | yes | no | yes | yes |
| `IReplayStore` | `ATProtoNet` (`ATProtoNet.Auth.ServiceAuth`) | `ServiceAuthVerifier`, Spaces | yes | no | no | yes |
| `ICursorStore` | `ATProtoNet.Streaming` | stream runtime, stream workers | yes | yes | no | yes |
| `IRepoSyncStateStore` | `ATProtoNet.Streaming` (`.Firehose`) | Sync 1.1 verifier and resync | yes | no | no | yes |

Spaces stores (authority, `simplespace`, credential revocation) are specified in
[spaces.md](spaces.md); they follow the rules of this spec and live in the Spaces packages
([D-08](../06-decisions.md#d-08-spaces-is-quarantined-as-experimental)).

Out of scope: caches that are not stores (DID document cache, handle cache, DPoP nonce cache),
which are specified with their consumers.

## Public API sketch

```csharp
// ── Sessions ──────────────────────────────────────────────────────────
public readonly record struct StoreVersion(long Value);
public sealed record StoredSession(SessionData Data, StoreVersion Version);

public interface ISessionStore
{
    ValueTask<StoredSession?> GetAsync(Did account, CancellationToken cancellationToken = default);

    // expected == null: succeed only if no session is stored. next == null: remove.
    ValueTask<StoreWriteResult> TryReplaceAsync(
        Did account, StoreVersion? expected, SessionData? next, CancellationToken cancellationToken = default);
}

public readonly record struct StoreWriteResult(bool Succeeded, StoredSession? Current);

public static class SessionDataSerializer          // public, so custom stores need no internals
{
    public static byte[] Serialize(SessionData data);
    public static SessionData Deserialize(ReadOnlySpan<byte> utf8Json);
}

// ── OAuth state ───────────────────────────────────────────────────────
public interface IOAuthStateStore
{
    ValueTask SetAsync(OAuthPendingAuthorization authorization, CancellationToken cancellationToken = default);
    ValueTask<OAuthPendingAuthorization?> TakeAsync(string state, CancellationToken cancellationToken = default);
}

// ── Replay ────────────────────────────────────────────────────────────
public interface IReplayStore
{
    // true: first presentation (now recorded until retainUntil); false: already spent.
    ValueTask<bool> TryConsumeAsync(Did issuer, string tokenId, DateTimeOffset retainUntil,
        CancellationToken cancellationToken = default);
}

// ── Streams ───────────────────────────────────────────────────────────
public interface ICursorStore                                // defined by STR; cursors are opaque strings
{
    ValueTask<string?> GetAsync(string streamId, CancellationToken cancellationToken = default);
    ValueTask SetAsync(string streamId, string cursor, CancellationToken cancellationToken = default);
}

public interface IRepoSyncStateStore
{
    ValueTask<RepoSyncState?> GetAsync(Did repo, CancellationToken cancellationToken = default);
    ValueTask SetAsync(RepoSyncState state, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(Did repo, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<RepoSyncState>> ListUnsynchronizedAsync(int limit, CancellationToken cancellationToken = default);
}

// ── Registration (ATProtoNet.Hosting / ATProtoNet.EntityFrameworkCore) ──
builder.Services.AddAtProto()
    .UseFileSessionStore(o => o.Directory = "/var/lib/app/sessions")
    .UseDistributedOAuthStateStore()
    .UseEfCoreReplayStore<AppDbContext>()
    .UseEfCoreCursorStore<AppDbContext>()
    .UseEfCoreRepoSyncStateStore<AppDbContext>();

// Each EF feature ships a DbContext and a model helper for the application's own context:
modelBuilder.ConfigureAtProtoSessions();      // also ConfigureAtProtoOAuthState(), …Replay(), …Cursors(), …RepoSyncState()
```

## Requirements

### General

- **STO-1** (MUST) Every store interface method is safe to call concurrently from many threads
  and, for out-of-process implementations (file, distributed cache, EF), from many processes,
  with the atomicity each interface states. *Source:* **Decision** ([D-22](../06-decisions.md#d-22-session-stores-support-compare-and-swap))
- **STO-2** (MUST) Implementations read the clock only from an injected `TimeProvider`
  (timestamps, expiry, sweeps). *Source:* [AG-28](../03-api-guidelines.md#resources-and-lifetimes) ·
  0.8 EF stores use `DateTimeOffset.UtcNow` (`EfCoreAtProtoSessionStore.cs:66,74`)
- **STO-3** (MUST NOT) No store starts background work on its own. Expired-row sweeps run inline
  on writes (rate-limited) or through an explicit `PurgeAsync`; `ATProtoNet.Hosting` offers an
  opt-in hosted service that calls `PurgeAsync` periodically. *Source:* [AG-27](../03-api-guidelines.md#resources-and-lifetimes)
- **STO-4** (MUST) Stores never log secrets (tokens, keys, PKCE verifiers, state values); they
  log DIDs, stream IDs and hashed keys only. *Source:* [AG-29](../03-api-guidelines.md#security)

### Sessions

- **STO-5** (MUST) `ISessionStore` is keyed by account `Did` and offers exactly two operations:
  read (`GetAsync`, returning the session and its version) and compare-and-swap
  (`TryReplaceAsync`). Insert is CAS with `expected == null`; removal is CAS with
  `next == null`. There is no unconditional write. No `IDistributedCache` implementation ships,
  because `IDistributedCache` cannot compare-and-swap; Redis or other shared stores are
  application implementations against the contract suite. *Source:*
  [D-22](../06-decisions.md#d-22-session-stores-support-compare-and-swap)
- **STO-6** (MUST) `TryReplaceAsync` succeeds only if the stored version equals `expected`
  (or nothing is stored and `expected` is `null`); on success the new version is strictly greater
  than every version previously stored for that account; on failure nothing changes and
  `Current` holds what is stored now. *Source:* **Decision**
- **STO-7** (MUST) Concurrent `TryReplaceAsync` calls with the same `expected` for one account
  produce exactly one success, including two concurrent first inserts (`expected == null`).
  *Source:* **Decision** · fixes 0.8's EF first-write race (`EfCoreAtProtoSessionStore.cs:62-78`:
  find-then-add with no `DbUpdateException` handling)
- **STO-8** (MUST) Accounts are isolated: an operation on one DID never reads or changes
  another's entry. *Source:* 0.8 · test `SessionStoreContractTests.Accounts_AreIsolated`
- **STO-9** (MUST) `SessionDataSerializer` is public and defines the one stored format: UTF-8
  JSON with a format version (`"v": 1`) and a session kind discriminator (`"$kind"`), carrying
  every field a session needs to resume (tokens, expiry, service endpoint, DPoP private key as
  PKCS#8, authorization-server grant data, client key ID). Unknown `v` values fail with
  `FormatException`. *Source:* 0.8 `AtProtoSessionJson.cs` (internal, reached through
  `InternalsVisibleTo`) · public is **Decision**
- **STO-10** (MUST) File and EF session stores encrypt the serialized session with ASP.NET Core
  Data Protection under purpose `ATProtoNet.Sessions.v1`. *Source:* 0.8 purpose
  `ATProtoNet.TokenStore` · new purpose is **Decision** (see Open points on migration)
- **STO-11** (MUST) An entry that cannot be decrypted or deserialized reads as absent (`null`)
  and is logged as a warning. It is removed only by a CAS against the version that was read, so
  a concurrent valid write is never deleted. *Source:* 0.8 file store re-check under lock ·
  test `FileAtProtoSessionStoreTests.GetAsync_CorruptedFile_ReturnsNullAndRemovesIt` · 0.8 EF
  removes unconditionally (`EfCoreAtProtoSessionStore.cs:103`), which this fixes

### File session store

- **STO-12** (MUST) One file per account named `{lowercase hex SHA-256 of the DID}.session` in
  the configured directory, created if missing. *Source:* 0.8 `FileAtProtoSessionStore.cs:174-177`
  (`.dat`) · test `StoredSession_IsOneEncryptedFile`
- **STO-13** (MUST) Writes go to a temporary file in the same directory followed by an atomic
  rename, so readers always see a whole session. *Source:* 0.8 `FileAtProtoSessionStore.cs:83-86` ·
  test `ReadsDuringRewrites_AlwaysSeeAWholeSession`
- **STO-14** (MUST) On Unix the directory is mode 0700 and files 0600. *Source:* 0.8
  `FileAtProtoSessionStore.cs:48-61`
- **STO-15** (MUST) CAS is serialized per account by an exclusive lock file
  (`{hash}.lock`, `FileShare.None`) held for the read-compare-write, so processes on one host
  share the store safely; the store is not supported on network file systems.
  *Source:* **Decision** · 0.8 test `ConcurrentWritesAndRemovals_OfOneAccount_LeaveOneOutcome`
  (in-process only)

### EF Core session store

- **STO-16** (MUST) Table `AtProtoSessions` with key `Did`, the encrypted payload, a `Version`
  column used as the concurrency token, and `UpdatedAt`. CAS is an `ExecuteUpdate` filtered by
  `Did` and `Version` (affected rows = 1 means success); insert is an `INSERT` whose unique-key
  violation is a CAS failure, confirmed by re-reading the row before reporting it.
  *Source:* **Decision** (D-22) · 0.8 table `AtProtoTokens`
- **STO-17** (MUST) `PurgeAsync(olderThan)` removes sessions not updated since `olderThan`.
  *Source:* 0.8 documents `UpdatedAt` "used for cleanup" (`AtProtoTokenEntity.cs:21`) but none exists

### OAuth state

- **STO-18** (MUST) `TakeAsync` is single-use: of any number of concurrent takes of one state,
  at most one returns the entry, and after a take the state is gone. Unknown, expired or
  over-long (`OAuthStateLimits.MaxStateChars`) states return `null` without a store round trip
  for the over-long case. *Source:* 0.8 `OAuthClient.cs:294` · tests
  `OAuthStateStoreContractTests.TakeAsync_IsSingleUse`, `TakeAsync_UnknownState_ReturnsNull`
- **STO-19** (MUST) `SetAsync` for an existing state keeps the latest entry. Entries expire at
  the pending authorization's `ExpiresAt`. *Source:* 0.8 · test `SetAsync_TheSameStateTwice_KeepsTheLatest`
- **STO-20** (MUST) The in-memory store bounds entries per requester
  (`OAuthStateLimits.MaxPerRequester`, evicting that requester's oldest) and in total
  (`OAuthStateLimits.Capacity`, evicting the oldest overall), evicting rather than refusing;
  IPv6 requesters are grouped by /64 (`OAuthAuthorizationOptions.RequesterIdFor`). *Source:* 0.8
  `OAuthStateStore.cs:138-205`
- **STO-21** (MUST) The distributed-cache store refuses to store entries in the clear unless
  `StoreSecretsUnencrypted` is set explicitly; by default it protects them with Data Protection
  purpose `ATProtoNet.OAuthState.v1`. Keys are `{prefix}{lowercase hex SHA-256 of the state}`;
  a read whose decrypted entry names another state reads as `null`. *Source:* 0.8
  `OAuthStateStore.cs:313-395`
- **STO-22** (MUST) The distributed-cache store's take is a read followed by a remove; it
  documents that two instances racing on one state can both read it, which is acceptable
  because the authorization server exchanges a code once and the login checks the browser
  binding ([server.md](server.md) SRV-45). It is therefore excluded from the concurrent-take
  contract case. *Source:* 0.8 `OAuthStateStore.cs:300-307`
- **STO-23** (MUST) The EF store's take reads the row and then deletes it with `ExecuteDelete`
  filtered by key; only the caller whose delete affected one row receives the entry. Payloads are
  encrypted as in STO-21. *Source:* **Decision**

### Replay

- **STO-24** (MUST) `TryConsumeAsync(issuer, tokenId, retainUntil)` returns `true` exactly once
  per `(issuer, tokenId)` while the entry is retained, and `false` for every later presentation;
  the same token ID from another issuer is not a collision. `retainUntil` is computed by the
  caller (token `exp` plus allowed skew, [auth.md](auth.md)). *Source:* 0.8 · tests
  `JtiReplayStoreContractTests` (all)
- **STO-25** (MUST) Concurrent presentations of one `(issuer, tokenId)` yield exactly one `true`,
  across store instances. *Source:* 0.8 · tests `TryConsumeAsync_ConcurrentPresentations_YieldExactlyOneSuccess`,
  `EfCoreJtiReplayStoreTests.TryConsumeAsync_AcrossTwoStoreInstances_SpendsAnIdentifierOnce`
- **STO-26** (MUST) In the EF store the insert is the check: a unique-key violation is a replay
  only after re-reading confirms the row exists, so a storage fault surfaces as an exception
  instead of refusing a valid token. *Source:* 0.8 `EfCoreJtiReplayStore.cs:80-100`
- **STO-27** (MUST) Entries are retained at least until `retainUntil`. Sweeps delete only rows
  whose retention ended in an earlier whole second than the sweep instant (rows store whole
  seconds rounded down). *Source:* 0.8 · test `TryConsumeAsync_EntryDueLaterWithinTheSecond_SurvivesTheSweep`
- **STO-28** (MUST) Table `AtProtoReplay` (0.8: `AtProtoJtiReplay`) with key `(Issuer, TokenId)`
  and `RetainUntil`. *Source:* 0.8 · test `ConfigureJtiReplayModel_MapsTheAtProtoJtiReplayTable`

### Cursors

- **STO-29** (MUST) `ICursorStore` stores one opaque cursor string per stream ID; it does not
  interpret cursors (STR's codecs encode and decode them, [streaming.md](streaming.md) STR-25).
  *Source:* **Decision**: 0.8's `IStreamCursorStore` stores a `long`, which cannot hold Jetstream
  v1 microsecond cursors and seq cursors under one meaning, or chat-moderation revs
- **STO-30** (MUST) Writes for one stream ID are applied in call order: a store never applies an
  older `SetAsync` after a newer one that has completed. The stream runtime is the only writer per
  stream ID (HST-23) and only writes forward. *Source:* 0.8 `CursorTracker` (monotonic,
  coalescing) · test `StreamCursorStoreTests`
- **STO-31** (MUST) File (`{streamId hash}.cursor`, atomic rename as in STO-13) and EF (table
  `AtProtoStreamCursors`, key `StreamId`) implementations ship. *Source:* **Decision**: 0.8 ships
  only the in-memory store, so a durable consumer must write its own

### Repository sync state

- **STO-32** (MUST) `RepoSyncState` holds the repo `Did`, the last revision (`Tid?`), the commit
  data CID (`Cid?`), the status (`Synchronized`, `Desynchronized`, `InProgress`) and the update
  time; unknown revision and tree round-trip as `null`. `SetAsync` replaces. *Source:* 0.8 ·
  tests `RepoSyncStateStoreContractTests` (all)
- **STO-33** (MUST) `ListUnsynchronizedAsync(limit)` returns states whose status is not
  `Synchronized`, oldest update first, at most `limit`. *Source:* 0.8 · test
  `EfCoreRepoSyncStateStoreTests.ListUnsynchronizedAsync_ReturnsTheOldestFirst_UpToTheLimit`
- **STO-34** (MUST) The EF implementation (table `AtProtoRepoSyncStates`, status stored by name)
  lives in `ATProtoNet.EntityFrameworkCore`, which depends on `ATProtoNet.Hosting`, not on the
  ASP.NET Core shared framework, so a firehose worker can use it. *Source:* 0.8 · test
  `ConfigureRepoSyncStateModel_MapsTheTableAndStoresStatusByName` · dependency is **Decision**

### EF Core packaging

- **STO-35** (MUST) Each EF feature ships a standalone `DbContext` and a public
  `ModelBuilder.ConfigureAtProto…()` helper so the tables can live in the application's own
  context; the store resolves `IDbContextFactory<TContext>` registered by the application.
  *Source:* 0.8 pattern
- **STO-36** (MUST) Model helpers configure only their own tables: no helper silently includes
  another feature's table. *Source:* **Decision**: 0.8 `SpaceDbContext.ConfigureSpaceModel`
  includes the replay table (`SpaceDbContext.cs:72-77`)
- **STO-37** (MUST) Stores use `ExecuteUpdate`/`ExecuteDelete`, so their tests run on SQLite
  in memory, not the EF in-memory provider. *Source:* 0.8 practice (CLAUDE.md)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `OAuthStateLimits.MaxStateChars` | 256 | longer states are not looked up |
| `OAuthStateLimits.MaxPerRequester` | 10 | in-memory pending logins per requester |
| `OAuthStateLimits.Capacity` | 10,000 | in-memory pending logins in total |
| `ReplayStoreLimits.InMemoryCapacity` | 100,000 | in-memory replay entries (oldest evicted only after retention ends; beyond capacity, consume fails closed with `false` and a warning) |
| `StoreLimits.SweepInterval` | 1 min | minimum interval between inline sweeps |
| `SessionStoreLimits.MaxPayloadBytes` | 64 KiB | serialized session size, enforced on write and read |

## Errors

| Failure | Result |
|---|---|
| CAS conflict | `StoreWriteResult.Succeeded == false` with `Current`; never an exception |
| Undecryptable or malformed stored session/state | `null`, warning logged (STO-11) |
| Storage fault (I/O, database) | the underlying exception propagates (`IOException`, `DbUpdateException`, …) |
| Replay key violation not confirmed by re-read | the `DbUpdateException` propagates (STO-26) |
| Distributed state store without protection and without `StoreSecretsUnencrypted` | `ArgumentException` at construction |
| Unknown serialized session format version | `FormatException` from the serializer; the store treats it as STO-11 |

## Conformance and tests

Contract base classes in `ATProtoNet.TestKit` ([04 — Testing](../04-testing.md#testkit-one-of-each-fake));
every implementation derives a test class from the matching contract:

- **Session contract:** round trip of password and OAuth sessions; `null` when absent; CAS
  success and failure; version monotonicity; insert-only-if-absent; remove via CAS; account
  isolation; argument validation; **concurrency:** N parallel first inserts → one success,
  N parallel replaces with one `expected` → one success, interleaved remove and replace → one
  outcome; corrupt entry handling. Ported from 0.8 `SessionStoreContractTests` plus the new
  cases.
- **OAuth state contract:** single-use take; unknown state; latest set wins; expiry;
  over-long state; **concurrency:** parallel takes → at most one entry (skipped for the
  distributed store, STO-22). Ported from 0.8 `OAuthStateStoreContractTests`.
- **Replay contract:** first use, second use, cross-issuer, retention, sweep boundary;
  **concurrency** across instances. Ported from 0.8 `JtiReplayStoreContractTests` and
  `EfCoreJtiReplayStoreTests`.
- **Cursor contract (new):** round trip, ordering of sequential writes, opaque values
  (long sequences, microsecond timestamps, TID strings).
- **Repo sync state contract:** ported from 0.8 `RepoSyncStateStoreContractTests`.
- File-store specifics: atomic rewrite under concurrent reads, Unix modes, lock file across two
  processes. EF specifics: SQLite in memory, two store instances over one database.

## Changes from 0.8

| Change | Reason |
|---|---|
| Session store is read + CAS only, with versions | D-22; removes `SessionManager`'s hand-built optimistic concurrency |
| No distributed-cache session store | `IDistributedCache` cannot CAS |
| Serializer public | Custom stores without `InternalsVisibleTo` |
| Data Protection purposes renamed and versioned | New stored format; see migration open point |
| EF first-write race fixed; corrupt-row removal by CAS | Bugs in 0.8 |
| Opaque string cursor instead of `long` | Cursor kinds differ per stream |
| Durable cursor stores ship (file, EF) | 0.8 ships only in-memory |
| Repo sync state EF store no longer needs ASP.NET Core | Firehose workers |
| `TimeProvider` everywhere; explicit `PurgeAsync` | AG-27, AG-28 |
| Table names `AtProtoSessions`, `AtProtoReplay`, `AtProtoStreamCursors`, `AtProtoOAuthStates` | Names match the concepts (0.8 says "Token" for sessions) |

## Open points

- **0.6/0.8 stored-data compatibility.** Proposed: the hot path reads only the 1.0 format; a
  one-off `AtProtoStoreMigration` helper (in `ATProtoNet.Hosting` and
  `ATProtoNet.EntityFrameworkCore`) reads 0.8 files (purpose `ATProtoNet.TokenStore`, `.dat`,
  including 0.6-format payloads) and 0.8 tables (`AtProtoTokens`, `AtProtoJtiReplay`,
  `AtProtoRepoSyncStates`) and writes 1.0 entries, so signed-in users survive the upgrade. The
  alternative is keeping 0.8 purposes and table names and reading the old format forever.
- **In-memory replay store at capacity** (fail closed with `false`) vs evicting unexpired
  entries (which would reopen replay). Proposed: fail closed, as in the limits table.
- **EF OAuth state store** is new in 1.0. Proposed: ship it, since multi-instance deployments
  without a distributed cache need one.
