# Spaces test vectors

Pinned, until the GA specification and its reference vectors are published, to the 2026-10-22
alpha release:

- proposal [bluesky-social/proposals](https://github.com/bluesky-social/proposals) 0016 at
  `b89d0b04495a62e0e22f1ab04c665a9c4e1c655d` (2026-10-22, #125);
- reference implementation [bluesky-social/atproto](https://github.com/bluesky-social/atproto)
  branch `permissioned-data` at `3a0b8c669881efa9e75680275f8695d618c5f80f` (2026-10-22), whose
  `packages/space` is the tree `34ce1e94c49249989f8fcec0936071691339902a` (last changed in
  `7fb6fb03`). npm publishes it as `@atproto/space@0.0.0-spaces-alpha-20261022141228`, whose
  sources are byte for byte those of the pin.

- `blake3_test_vectors.json`: the official BLAKE3 vectors, unchanged from
  [BLAKE3-team/BLAKE3](https://github.com/BLAKE3-team/BLAKE3) `test_vectors/test_vectors.json` at
  `f55849f89cd85083c1c9daa2c5de20766f309c00` (last changed in `ae3e8e6b`, 2020-09-29); CC0-1.0,
  Apache-2.0 or Apache-2.0 WITH LLVM-exception. Only `hash` is asserted.
- `blake3-long-output.json`: SHA-256 of longer extended outputs (`@noble/hashes` 1.8.0, which the
  reference's LtHash uses), for the batched output paths past the official 131 bytes.
- `lthash.json`: LtHash digests and states from the reference `lthash.ts` (unchanged since the
  2026-10-15 pin).
- `http-signatures.json`: RFC 9421 credential signatures (label and `tag`
  `atproto-space-credential`, covering `("authorization" "atproto-space-audience")`) made with the
  reference `http-signature.ts` (fixed P-256 key `SHA-256("atproto.net space vector key 1")`), with
  the verdict owed; every case is checked against the reference `verifyCredentialSig`, except two
  marked `beyondReference` (a repeated label is refused, SPC-22, where the reference takes the
  last). The credential exchange is no longer signed (SPC-23), so its cases are gone.
- `structured-fields.json`: RFC 8941 dictionaries from 0.8's `StructuredFieldsTests`; where
  `structured-headers` 1.0.1 (the reference's parser) differs from RFC 8941 the case records it
  under `reference`.
- `space-tokens.json`: delegation tokens, credentials and client attestations in the reference
  encoding (`credential.ts`, with a fixed clock and `jti`), checked against the reference
  `verifySpaceToken`; cases marked `beyondReference` are refusals the spec requires (SPC-19,
  SPC-20) and the reference does not make.
- `kem.json`: DHKEM(P-256, HKDF-SHA256) from `kem.ts`, encapsulated with fixed ephemeral keys
  (and RFC 9180 §A.3.1), decapsulated with the author key, and refused keys.
- `space-mac.json`: commit and record MACs from `mac.ts` with fixed shared secrets, and the
  `Atproto-Space-Want-Mac`/`Atproto-Space-Mac` headers formatted and parsed.
- `star.json`: space repo exports (STAR-lite v1, `atproto-space-repo`) written by the reference
  `serializeRepo` and invalid ones, with the verdict of the reference `verifyRepoStarFull` and of
  its bare STAR reader.

Regenerate with these packages installed (all from the alpha release, so they match the pinned
sources' `workspace:` dependencies): `@atproto/crypto`, `@atproto/lex-data`, `@atproto/lex-cbor`,
`@atproto/car` and `@atproto/syntax` at `0.0.0-spaces-alpha-20261022141228` (the released
`@atproto/crypto` 0.5.x lacks `hkdfSha256`, `hmacSha256` and `P256Keypair.ecdh`), `@noble/hashes`
1.8.0, `structured-headers` 1.0.1 and `zod` 3.25.76; then run a copy of the script from the
directory holding `node_modules`, against the reference's `packages/space/src` at the pin:
`node --experimental-transform-types gen-space-vectors.mjs <packages/space/src> <this folder>` (Node
22.7 or later). The output is deterministic.

Not carried: 0.8's alpha vectors for the superseded space commit and space-repo CAR formats
(SPC-11…16). Pending: the GA reference vectors, which replace these once published; the next pin
moves with the 2026-10-29 release.
