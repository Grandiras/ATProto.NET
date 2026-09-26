# Streaming test data

`jetstream-golden-header.bin` and `jetstream-golden-block.bin` are Jetstream's own golden
segment block; `JetstreamGoldenBlockTests` reads them.

`firehose-sync-frames.json` holds six raw `com.atproto.sync.subscribeRepos` frames captured from
`wss://bsky.network` on 2026-09-26, base64-encoded, with each account's `#atproto` signing key and
PDS as `plc.directory` served them that day: two consecutive commits of one repository
(`chain-first`, `chain-second`), a single-record update, a single-record delete, a commit with
three operations, and a `#sync` event. Every commit carries `prevData`, so each is a real Sync 1.1
diff to invert. `RepoSyncVerifierTests` and `TypedFirehoseConsumerSyncTests` read it; their revisions
are in the past, so the future-revision check passes without a fixed clock.

The frames are public repository data. To replace them, capture a new set with any
`subscribeRepos` client, keep it to a handful of small frames, and record the signing keys
alongside.
