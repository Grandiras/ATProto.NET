# Lexicon resolution test vectors

`lexicon-resolution.json` holds `com.atproto.sync.getRecord` proofs of `com.atproto.lexicon.schema`
records, produced by the reference implementation
([`@atproto/repo`](https://www.npmjs.com/package/@atproto/repo) 0.10.14 with
`@atproto/crypto` 0.5.5): a 44-record repository signed with a fixed K-256 key, holding a record
Lexicon (`post`), a permission set (`authBasic`), a schema whose `id` is not its record key
(`mismatch`) and one whose `$type` is wrong (`badType`), plus an absence proof (`missing`). Each
entry records what the reference resolver,
[`@atproto/lex-resolver`](https://www.npmjs.com/package/@atproto/lex-resolver) 0.3.0, made of
it when served the DID document and the proof: `post` and `authBasic` resolve, the others fail.
`foreignKey` is the reference verdict on `post` when the DID document names another signing key
(the proof no longer verifies), and `dnsNames` the DNS names `@atproto/syntax` derives for the
Lexicon spec's example NSIDs (it keeps the authority's case, which DNS ignores).

`gen-lexicon-resolution.mjs` regenerates it: install those three packages, then run
`node gen-lexicon-resolution.mjs > lexicon-resolution.json`. The output is deterministic (fixed
key, revision and records; RFC 6979 signatures).

`interop/` holds the `lexicon/` suites of
[bluesky-social/atproto-interop-tests](https://github.com/bluesky-social/atproto-interop-tests) at
`056e5741bb330757205d6b16db5266fffcae937b` (CC0-1.0), unchanged: `lexicon-valid.json` and
`lexicon-invalid.json` (schema documents), `record-data-valid.json` and
`record-data-invalid.json` (records against the schemas in `catalog/`). To update them, copy the
upstream `lexicon/` directory over `interop/` and change the commit here and in `MANIFEST.md`.
