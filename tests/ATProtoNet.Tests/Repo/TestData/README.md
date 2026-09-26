# Repository test vectors

`record-proof.json` holds two `com.atproto.sync.getRecord` proofs produced by the reference
implementation, [`@atproto/repo`](https://www.npmjs.com/package/@atproto/repo) 0.10.14 with
`@atproto/crypto` 0.5.5: a 60-record repository signed with a fixed K-256 key, the proof of one
record in it (`presentCar`) and the proof that a neighbouring key is absent (`absentCar`). The
reference verifier (`verifyRecords` / `verifyProofs`) accepts both before the file is written.

`gen-record-proof.mjs` regenerates it: install those two packages, then run
`node gen-record-proof.mjs > record-proof.json`. The output is deterministic (fixed key,
revision and records; RFC 6979 signatures), so an unchanged generator reproduces the file byte
for byte. `RecordProofTests` reads it.
