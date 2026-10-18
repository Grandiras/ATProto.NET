# Crypto interop fixtures

These files are copied unchanged from the `crypto/` directory of
[bluesky-social/atproto-interop-tests](https://github.com/bluesky-social/atproto-interop-tests),
commit `056e5741bb330757205d6b16db5266fffcae937b`:

- `signature-fixtures.json`: P-256 and K-256 signatures over one message, including high-S
  and DER-encoded signatures that atproto rejects.
- `w3c_didkey_P256.json`, `w3c_didkey_K256.json`: private keys and the `did:key` each one
  must produce.

They are licensed under the Creative Commons Zero 1.0 Universal (CC0-1.0) public domain
dedication, which allows copying them without restriction or attribution. The upstream
repository carries the full license text in `LICENSE-CC0`.

To update them, copy the files over from a newer upstream commit and change the commit above.
`AtProtoInteropCryptoTests` reads them.
