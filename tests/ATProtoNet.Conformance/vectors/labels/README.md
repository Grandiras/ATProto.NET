# Label test vectors

`label-vectors.json` holds label signatures and `subscribeLabels` frames made by the reference
implementations, as 0.8's `LabelSigningInteropTests` and `LabelStreamFramesTests` pasted them:

- indigo's `atproto/labeling` test vector (`label_test.go`, `TestVerifyLabel`);
- two labels served by Bluesky's moderation service (`queryLabels` on `mod.bsky.app`, 2025-03-13)
  and the DID document of `did:plc:ar7c4by46qjdydhdevvrndac`, whose `#atproto_label` key signs them;
- labels signed by `@atproto/ozone`'s `signLabel` (with `@atproto/common`'s `cborEncode` and
  `@atproto/crypto`) with a fixed K-256 and P-256 key, one with every optional field and one
  with `neg: false`, which the reference signer drops;
- the `#labels`, `#info` and error frames `cborEncode` writes.

From those, `gen-label-vectors.mjs` derives the rest: high-S forms of valid signatures, tampered
and unversioned labels, and labeler DID documents (label key, account key, rotations, a malformed
key). It re-checks every pasted encoding and signature with `@atproto/lex-cbor` 0.1.7 and
`@atproto/crypto` 0.5.5 before writing; run `node gen-label-vectors.mjs > label-vectors.json`
with those installed.
