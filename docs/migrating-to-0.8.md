# Migrating to 0.8

0.8 catches the SDK up with the protocol: the public Lexicons as of upstream `main` in October 2026,
and the Spaces alpha of 2026-10-01, whose wire format changed. This page lists every breaking change
with what to do about it. The [changelog](../CHANGELOG.md) has the same changes alongside the
additions and fixes.

## Lexicon methods

New optional parameters follow the [parameter order](../CONTRIBUTING.md#lexicon-models-and-clients):
filters before `limit` and `cursor`. A call that passes `limit` or `cursor` positionally stops
compiling or binds the wrong parameter; name them.

| Method | New parameter |
|---|---|
| `Feed.GetTimelineAsync` | `since`, after `algorithm` |
| `Feed.GetListFeedAsync` | `since`, after `list` |
| `Feed.GetQuotesAsync` | `sort` (`latest` or `top`) |
| `Ozone.Queue.CreateQueueAsync`, `UpdateQueueAsync` | `recommendedLabels` |

`Ozone.Report.GetLiveStatsAsync` returns its counts as a `LiveStats`, a `QueueStats` with the
fields upstream added to live statistics. Code that reads `Stats` as a `QueueStats` keeps working.

## Spaces

Spaces are an alpha protocol: a 0.8 client or server only interoperates with hosts that run the
2026-10-01 alpha or later (reference implementation `679724ad`, proposal 0016 at `0c9c2e88`), and not
with the one 0.7 targeted.

### `repoRev`, `spaceRev` and `listRepos` checkpoints

The permissioned-data protocol distinguishes a repo's own revision (`repoRev`) from the space-wide
one (`spaceRev`), which the space authority assigns to every update it accepts.

| Before | After |
| --- | --- |
| `SpaceRepoView.Rev` | `SpaceRepoView.RepoRev`, plus `SpaceRepoView.SpaceRev` |
| `NotifyWriteRequest.Rev` | `NotifyWriteRequest.RepoRev`, plus optional `SpaceRev` and `PrevSpaceRev` (set only on forwards to syncers) |
| `listRepos` pages by DID; `cursor` is a DID | `listRepos` is ordered by `spaceRev`; `cursor` is an exclusive `spaceRev` checkpoint, returned on every non-empty page and omitted on an empty one |
| `ISpaceAuthorityStore.RecordWriteAsync(space, repoDid, rev, hash)` returns `Task` | `RecordWriteAsync(space, repoDid, repoRev, hash)` returns `Task<SpaceWriteSequence?>`: the assigned `SpaceRev` and `PrevSpaceRev`, or `null` for a `repoRev` the repo has already reached (ignored) |
| `SpaceWriteNotifier.ForwardWriteAsync(space, repoDid, rev, hash)` | `ForwardWriteAsync(space, repoDid, repoRev, hash, sequence)` |
| `SpaceWriteNotifier.NotifyWriteAsync(…, rev, …)` | the same call with `repoRev`; new `NotifyWriteInBackground` coalesces per repo and space and retries transient failures for `RetryWindow` |

New: `SpaceErrors.FutureRev` (a `repoRev` more than five minutes ahead of the authority's clock), and
the authority answers `SpaceNotFound` for an unknown or deleted space. `SpaceSyncer.NeedsCatchUp` and
`SpaceSyncer.ListChangedReposAsync` are the syncer's gap check and catch-up from a checkpoint.

**Compile errors.** Rename `.Rev` to `.RepoRev` on `SpaceRepoView` and `NotifyWriteRequest`, rename the
property in your own `ISpaceAuthorityStore` (and return the `SpaceWriteSequence` it assigns), and pass
the sequence to `ForwardWriteAsync`.

**Behaviour changes that compile.**

- A `listRepos` cursor saved before 0.8 is a DID, not a revision; the authority refuses it with
  `InvalidRequest`. Start the catch-up again from `null`, then keep the last `spaceRev` you have safely processed per space.
- A `notifyWrite` for a `repoRev` at or below the recorded one is now ignored, including the same
  revision with a different hash; it used to overwrite the hash.
- The in-process retry queue of `NotifyWriteInBackground` is not persisted. The reference PDS persists
  and retries for 24 hours; here a restart drops what is pending, and the repo is caught up by its next
  write.

**EF Core.** `AtProtoSpaceWriters.Rev` is now `RepoRev`, `SpaceRev` is a new required column unique
with `Space`, and `AtProtoSpaces` gains a nullable `LastSpaceRev`. Generate a migration, clear the
writer rows (the set is rebuilt by each repo host's next `notifyWrite`), and give `SpaceRev` an ordinal
collation. The full migration is in [Spaces](spaces.md#upgrading-the-writer-set-from-07).

### `type` is `spaceType`

`com.atproto.simplespace.createSpace` takes `spaceType` in its body, and
`com.atproto.space.listSpaces` takes it as a query parameter, where both said `type`. The SDK
follows the wire name:

- `SimpleSpaceClient.CreateSpaceAsync(type, …)` and `SpaceClient.ListSpacesAsync(type: …)` take a
  `spaceType` parameter instead. A call that names the argument stops compiling.
- `CreateSimpleSpaceRequest.Type` is `CreateSimpleSpaceRequest.SpaceType`.
- A host built on `ATProtoNet.Server` reads `spaceType` from `createSpace` and rejects a body that
  only carries the old `type` with `InvalidRequest`.
- `atproto-lexgen` maps the new `space-ref` string format to `SpaceUri` (and back).

```csharp before
var created = await client.SimpleSpace.CreateSpaceAsync(type: Nsid.Parse("com.example.forum"));
var spaces = await client.Space.ListSpacesAsync(type: Nsid.Parse("com.example.forum"));
```

```csharp
var created = await client.SimpleSpace.CreateSpaceAsync(spaceType: Nsid.Parse("com.example.forum"));
var spaces = await client.Space.ListSpacesAsync(spaceType: Nsid.Parse("com.example.forum"));
```

A positional call (`CreateSpaceAsync(Nsid.Parse("com.example.forum"))`) compiles unchanged.
