# Spaces test vectors

Pinned, until the GA specification and its reference vectors are published, to:

- proposal [bluesky-social/proposals](https://github.com/bluesky-social/proposals) 0016 at
  `c76714ac3bc30318496d5d7f29d9341d4d8c0c69` (2026-10-15);
- reference implementation [bluesky-social/atproto](https://github.com/bluesky-social/atproto)
  branch `permissioned-data` at `619c6e8556db7dd37bb7df1d92a5eae83da8d8b0` (2026-10-15).

`packages/space` there is the tree `52bd9552ff6b6847c429ab3a19b761ecfc017093`, identical to the
one at 0.8's pin `679724ad62eb9a02f7f40429c3c4fdd65d3e4c79` (its last change is `f57eb332`,
2026-10-01), so the vectors 0.8 pasted still hold; the generator re-derives them from the pinned
sources and checks them.

- `blake3_test_vectors.json`: the official BLAKE3 vectors, unchanged from
  [BLAKE3-team/BLAKE3](https://github.com/BLAKE3-team/BLAKE3) `test_vectors/test_vectors.json` at
  `f55849f89cd85083c1c9daa2c5de20766f309c00` (last changed in `ae3e8e6b`, 2020-09-29); CC0-1.0,
  Apache-2.0 or Apache-2.0 WITH LLVM-exception. Only `hash` is asserted.
- `blake3-long-output.json`: SHA-256 of longer extended outputs (`@noble/hashes` 1.8.0, which the
  reference's LtHash uses), for the batched output paths past the official 131 bytes.
- `lthash.json`: LtHash digests and states from the reference `lthash.ts`.
- `http-signatures.json`: RFC 9421 request and credential-exchange signatures made with the
  reference `http-signature.ts` (fixed P-256 key `SHA-256("atproto.net space vector key 1")`; the
  two base signatures are byte for byte those 0.8 pinned), with the verdict owed; every case is
  checked against the reference `verifySpaceSignature`, except two marked `beyondReference`
  (a repeated label is refused, SPC-22, where the reference takes the last).
- `structured-fields.json`: RFC 8941 dictionaries from 0.8's `StructuredFieldsTests`; where
  `structured-headers` 1.0.1 (the reference's parser) differs from RFC 8941 the case records it
  under `reference`.
- `space-tokens.json`: delegation tokens, credentials and client attestations in the reference
  encoding (`credential.ts`, with a fixed clock and `jti`), checked against the reference
  `verifySpaceToken`; cases marked `beyondReference` are refusals the spec requires (SPC-19,
  SPC-20) and the reference does not make.

Regenerate with `@atproto/crypto` 0.5.5, `@atproto/lex-data` 0.1.8, `@atproto/syntax`,
`@noble/hashes` 1.8.0 and `structured-headers` 1.0.1 installed and the reference's
`packages/space/src` checked out at the pin:
`node --experimental-transform-types gen-space-vectors.mjs <packages/space/src> .` (Node 22.7 or
later). The output is deterministic.

Not carried: 0.8's alpha vectors for the superseded space commit and space-repo CAR formats
(SPC-11…16). Pending: STAR export files, KEM encapsulation and decapsulation, and the
`getRepo`/`getRecord` header MACs (SPC-54…60) have no reference vectors at the pin yet; they come
with the GA reference implementation.
