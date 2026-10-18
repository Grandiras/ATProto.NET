# Streaming test data

## Firehose and Sync 1.1

`firehose-sync-frames.json` holds six raw `com.atproto.sync.subscribeRepos` frames captured from
`wss://bsky.network` on 2026-09-26, base64-encoded, with each account's `#atproto` signing key and
PDS as `plc.directory` served them that day: two consecutive commits of one repository
(`chain-first`, `chain-second`), a single-record update, a single-record delete, a commit with
three operations, and a `#sync` event. Every commit carries `prevData`, so each is a real Sync 1.1
diff to invert. The frames are public repository data. To replace them, capture a new set with any
`subscribeRepos` client, keep it to a handful of small frames, record the signing keys alongside,
and regenerate `sync-cases.json`.

`sync-cases.json` is what the suite runs: sequences of `#commit` and `#sync` frames against a
repository's stored state (`state`, `null` for none), each step with the outcome it must reach
(`valid`, `invalid`, `stale`, `desynchronized`) and, where it matters, the state stored after it.
`now` is the clock every case runs at. The cases are the live frames above (each valid, the two
consecutive commits chaining, a replay stale, and altered copies that must fail), and frames built
by the reference implementation, [`@atproto/repo`](https://www.npmjs.com/package/@atproto/repo)
0.10.14 with `@atproto/crypto` 0.5.5, for a repository signed with a fixed K-256 key: chains of
creates, updates and deletes, the chain rules, `#sync`, the limits, and 0.8's `Tampered` table
(STR-48), one rule broken per frame. Large frames are gzipped (`frameGzip`). A commit without
operations carries its unchanged root node (the reference PDS sends the commit block alone).

`firehose-frames.json` holds single frames, encoded by `@atproto/lex-cbor` 0.1.7 or deliberately
broken, with what a consumer must make of each: the event's fields, the `#info` notice, the error
frame, or the drop and its reason.

Regenerate both with those packages installed:
`node gen-sync-cases.mjs firehose-sync-frames.json > sync-cases.json` and
`node gen-firehose-frames.mjs > firehose-frames.json`. The output is deterministic (fixed keys,
revisions, records and times; RFC 6979 signatures).

## Jetstream

`jetstream-golden-block.bin` and `jetstream-golden-header.bin` come from
[bluesky-social/jetstream](https://github.com/bluesky-social/jetstream) (MIT or Apache-2.0),
checked against commit `f1b41ba424db21e9fb08e32a7e2a2d951946c0ad` (2026-10-15):

- `jetstream-golden-block.bin` is `segment/testdata/golden_block.bin` (last changed in
  `1d16a6e5`, 2026-05-14) with its zstd frame inflated (`zstd -dc golden_block.bin`), so the
  columnar layout can be checked without a zstd implementation. The reference encoder wrote it
  from `goldenEvents()` in `segment/block_golden_test.go`, which `jetstream-segments.json` lists
  as the expected rows.
- `jetstream-golden-header.bin` is the first 256 bytes of
  `segment/testdata/legacy_fixed4096_blooms.jss` (last changed in `04cae8c3`, 2026-07-10), a real
  sealed segment, which pins every field offset of the fixed header.

`jetstream-segments.json` lists what those decode to, plus hostile variants (an implausible
event count, truncation, a missing magic, an active segment, a footer inside the header); it is
written by hand. `jetstream-events.json` holds v1 and v2 frames, from Jetstream's README and the
`network.bsky.jetstream.subscribeEvents` Lexicons as 0.8's `JetstreamEventParserTests` and
`JetstreamV2EventParserTests` paste them, with what each must parse to; `node
gen-jetstream-events.mjs > jetstream-events.json` regenerates it (no dependencies).
