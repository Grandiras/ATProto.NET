# Documentation

The 0.9 documentation is written as the rewrite lands its API: each phase adds draft guide pages
for its layer, with compiled samples ([design/05-rewrite-plan.md](../design/05-rewrite-plan.md#phase-gates)),
and phase 11 completes them along with the migration guide from 0.8.

Pages so far:

- [Identifiers and string formats](identifiers.md): the vocabulary types, TIDs, datetimes, addresses and their JSON.
- [The data model](data-model.md): DRISL-CBOR, the JSON forms, CIDs, CAR and STAR.
- [Cryptography](crypto.md): signing keys and `did:key`, JWS and JWKs, BLAKE3, key encapsulation, HTTP message signatures.
- [Repositories](repositories.md): the Merkle Search Tree, commits, record proofs, snapshots and the repository writer.
- [Spaces](spaces.md): the Spaces protocol pin, space repository hashes and commits, MACs, exports and tokens.

The 0.8 documentation is on
[`main`](https://git.grandiras.net/Grandiras/ATProto.NET/src/branch/main/docs/index.md).
