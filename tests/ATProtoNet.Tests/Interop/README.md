# Vendored atproto interop fixtures

`syntax/` is a verbatim copy of the `syntax/` directory of
[bluesky-social/atproto-interop-tests](https://github.com/bluesky-social/atproto-interop-tests),
the shared conformance vectors the TypeScript and Go reference implementations also run.

- **Commit:** `056e5741bb330757205d6b16db5266fffcae937b` (2026-07-01, "Merge pull request #17 from nnabeyang/lang-tag-bcp47-fixtures")
- **License:** CC0 1.0 Universal (public domain dedication); the upstream repository's `LICENSE-CC0` has the full text. No attribution is required; this note records provenance.

Do not edit the files by hand. Whitespace is significant (several invalid cases have a leading
or trailing space), which `Interop/.editorconfig` protects. To update, copy the upstream
`syntax/*.txt` over these files, record the new commit above, and run the unit tests.

## Format

One test value per line. Blank lines and lines starting with `#` are skipped; nothing is
trimmed. `SyntaxFixtures` reads the files from the source tree, so adding a file needs no
project change.

## Coverage

`Identity/SyntaxInteropTests.cs` asserts every line of:

| Files | Type |
|---|---|
| `tid_syntax_*` | `Tid` |
| `aturi_syntax_*` | `AtUri` |
| `did_syntax_*` | `Did` |
| `nsid_syntax_*` | `Nsid` |
| `handle_syntax_*` | `Handle` |
| `recordkey_syntax_*` | `RecordKey` |
| `atidentifier_syntax_*` | `AtIdentifier` |
| `cid_syntax_invalid.txt` | `Cid` |

Not asserted:

- `cid_syntax_valid.txt` lists generic multiformats CIDs (base58, base16, dag-pb and others).
  `Cid` accepts only the CIDv1 subset the atproto data model blesses, which none of those
  lines is in.
- `datetime_*`, `language_*` and `uri_*` cover the Lexicon `datetime`, `language` and `uri`
  string formats. The SDK has no types for these yet; the files are kept so that such types can
  be tested against them.
