# Token test vectors

- `service-auth.json`: inter-service auth tokens minted the way `@atproto/xrpc-server`'s
  `createServiceJwt` does (a compact JWS signed with `@atproto/crypto`), each rewritten to break
  one rule, with the verdict a receiver owes it and the reference error name (`BadJwt`,
  `BadJwtSignature`, …) — 0.8's `ServiceAuthVerifierTests` as data. Every case runs at the file's
  `now` (plus a step's `at` seconds) against the file's `defaults` and `documents`, which a case's
  `options` override; the steps of one case share a replay store. `gen-service-auth.mjs`
  regenerates it with `@atproto/crypto` 0.5.5 installed (deterministic: fixed keys, claims and
  clock, RFC 6979 signatures).
- `dpop-pkce.json`: the RFC 9449 key thumbprint and `ath` examples, the `htu` normalisation table
  and proof requests of 0.8's `DPoPTests` and `DPoPProofGeneratorTests` (with a fixed P-256 key,
  and a K-256 key a proof must refuse), and the RFC 7636 appendix B PKCE pair.
  `gen-dpop-pkce.mjs` regenerates it (Node's own crypto only).
- `oauth-metadata.json`: a PDS's protected-resource metadata and its authorization server's
  metadata, one member changed per case, with the verdict of the AT Protocol OAuth profile — 0.8's
  `AuthorizationServerMetadataValidationTests` as data. `gen-oauth-metadata.mjs` regenerates it
  (no dependencies).
