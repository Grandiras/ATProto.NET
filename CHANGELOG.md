# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This is the changelog of the 0.9 rewrite on `next`. It starts at 0.9.0 and records, per phase,
the decisions users will notice; the 0.8 history is on `main`. The 0.9.0 release notes are the
migration guide.

## [Unreleased]

### Breaking changes

- **0.9 targets `net11.0` only** — every package targets .NET 11 and C# 15; there is no `net10.0` build. Migration: stay on 0.8 until the app moves to .NET 11 (#225)
- **Identity API** — `DidResolutionException` becomes `IdentityResolutionException`, `IdentityResolverOptions` splits into per-component options, keys are `DidKey` and endpoints `Uri`, and every resolver fetches through an `UntrustedHttp` the application owns. Migration: build the resolver with `IdentityResolverFactory.CreateDefault(http)` (#243)
- **Lexicon generator commands** — the 0.8 `csharp` command is `atproto-lexgen generate`, malformed Lexicons fail generation instead of being skipped, and the `lexicon` command (C# to Lexicon) is gone. Migration: run `generate` with a `lexgen.json` (#244)
- **Persistent MST** — `MerkleSearchTree` edits return a new tree and leave the old one unchanged; keys are `MstKey`, values `Cid`, and proofs and snapshots verify with a `VerificationKey` rather than a `did:key` string. Migration: assign each edit's result (`tree = tree.Insert(key, cid)`) (#240)

### Added

- **The 0.9 tree** — a fresh tree on `next`, built from the accepted design in `design/`, with a conformance suite that runs the same protocol vectors against 0.8 and the rewrite (#225)
- **Identifier types** — the atproto identifiers, `Cid` and the space addresses (`SpaceUri`, `SpaceRecordUri`, `AtAddress`) validate on construction and share `IAtIdentifier<T>`. `DecimalString` carries numbers written in Lexicon strings without changing their text (#237)
- **Data model** — a strict-by-default DRISL reader and a canonical writer, `DataModelValue` for schema-less values, `CidLink` and `BlobRef` (legacy blobs read and written back), bounded streaming CAR and STAR readers and writers, and the public `Base32`, `Base58Btc` and `UnsignedVarint` codecs (#237)
- **Cryptography** — `SigningKey`, `VerificationKey` and `DidKey` for P-256 and K-256 with low-S signatures, `SignedObject` for signed DRISL maps, JWS and JWK, BLAKE3, key encapsulation for Spaces, and RFC 9421 HTTP message signatures with RFC 8941 structured fields (#237)
- **Repositories** — a persistent `MerkleSearchTree` that re-hashes only what an edit changed and loads partial trees through `IBlockStore`, typed commits, record proofs of presence and absence, bounded `RepoSnapshot` verification and a `RepoWriter` for signed commits (#240)
- **Spaces foundation** — `LtHash`, `SpaceCommit`, the shared-secret MACs and their headers, verified STAR repository exports and the three space token types, following the 2026-10-22 Spaces alpha (#240)
- **Untrusted fetches** — `UntrustedHttp`, one hardened path to hosts that untrusted input names: public addresses only, a total time budget, capped bodies and no redirects it was not asked to follow, with a per-instance concurrency gate (#243)
- **Identity resolution** — strict-or-tolerant `DidDocument` lookups including the Spaces KEM key, `did:plc` and `did:web` resolvers, concurrent DNS-and-HTTPS handle resolution where DNS wins, bidirectional verification, a stale-while-revalidate document cache and `VerifyWithRefreshAsync` (#243)
- **PLC directory client** — `PlcClient` reads documents, logs, state and the export, and submits operations, reporting a refusal as `PlcOperationRejectedException` (#243)
- **Typed PLC operations** — `PlcUpdate` (genesis when `Prev` is null), `PlcTombstone` and the read-only legacy create, signed and read through `SignedPlcOperation` with its CID, DID derivation and verification; the directory's size and field limits fail at signing time (#243)
- **Lexicon schemas** — one typed schema model with reference resolution, lint rules with stable IDs, compatibility (versioning) rules, and `LexiconValidator`, which checks data against a catalog and reports every violation with its path (#244)
- **Generated Lexicon code** — unions are C# 15 `union` types with a shared `LexiconUnknown` case, known values are open structs, records lead with their `$type` and keep unknown fields; the `com.atproto` family is generated and checked in (#244)
- **`atproto-lexgen`** — `generate`, `lint`, `install` and `update` with fixed exit codes, replacing the 0.8 generator's `csharp` command and the vendoring script (#244)
