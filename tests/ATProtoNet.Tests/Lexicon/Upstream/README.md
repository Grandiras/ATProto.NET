# Vendored upstream Lexicons

`lexicons/` is a pinned snapshot of the Lexicons the SDK implements. `LexiconDriftTests` checks the
SDK's hand-written surface against it (see "What the drift test checks" below).

| Source | Namespaces | Pinned at |
|---|---|---|
| [bluesky-social/atproto](https://github.com/bluesky-social/atproto) `lexicons/`, copied unchanged | `app.bsky.*`, `chat.bsky.*`, `com.atproto.*`, `tools.ozone.*` | commit `2e583a4ed26659923b2a1effd952fce937f0feeb` (2026-09-24, "adds the ability to disable accounts via oauth sessions on com.atproto.server.deactivateAccount (#5545)") |
| The `com.atproto.lexicon.schema` records of the `standard.site` Lexicon authority (`_lexicon.standard.site` → `did:plc:re3ebnp5v7ffagz6rb6xfei4`) | `site.standard.*` | the record CIDs below |

- **Fetched:** 2026-09-25.
- **Licenses:** the atproto repository is dual MIT / Apache-2.0 (its `LICENSE.txt`,
  `LICENSE-MIT.txt` and `LICENSE-APACHE.txt`). The standard.site Lexicons are MIT
  ([tangled.org/standard.site](https://tangled.org/standard.site)); their record, object and theme
  schemas are also in the atproto repository under its license.
- Permissioned data (`com.atproto.space.*`, `com.atproto.simplespace.*`) is not here: it is a
  proposal that is not on upstream `main` yet.

The `site.standard.*` files come from the network rather than the atproto repository because only
the network has the two permission sets. Each file is the record's `value` without its `$type`:

| NSID | Record CID |
|---|---|
| `site.standard.authFull` | `bafyreicwk5myf4dwjr6kpfy4qiko5mcwltk6kmnewbcvvdu2le3sbgpm4e` |
| `site.standard.authSocial` | `bafyreibdamyqel7hurrk66bbpvrqgf6cby3kmpnchvwrkjb5rijgkrdsve` |
| `site.standard.document` | `bafyreifylc5lfr4vqfzrwdtqm4pnfg7cbra7z4acbgikylqandzefwdeaq` |
| `site.standard.graph.recommend` | `bafyreibglev4uvatb5do5obqxlif5w6b63tysjb7ve6w44x54g7hfe2rgy` |
| `site.standard.graph.subscription` | `bafyreiccyb5e4vq3rwhrajeud72ztezljufzrqbo73vz6mw2jwbgsxfqta` |
| `site.standard.publication` | `bafyreiapvnarfn3nl2wvg27hsylli5zqvou7a74p7od5mu4detprtejapy` |
| `site.standard.theme.basic` | `bafyreiabm6f64zosv5jcdmwd3qvekjhoi7enevg53rxj734ney4sd6lmku` |
| `site.standard.theme.color` | `bafyreicps42z5maxdqwml7t3klsrrpk332jthyfgpeb4zjn3wlvp5vyjia` |

Do not edit the files by hand.

## Refreshing

```bash
tests/ATProtoNet.Tests/Lexicon/Upstream/refresh.sh <atproto commit>   # usually the current main
```

The script (needs `curl`, `tar` and `jq`) replaces `lexicons/` and prints the commit and the
`site.standard` record CIDs; update the tables above with them and the fetch date. Then run the
unit tests. Each drift failure is an upstream change: model it, or add a commented allow-list
entry in `LexiconDriftTests.Allowlists.cs` saying why the SDK differs.

## What the drift test checks

`LexiconDriftTests` reads the snapshot from the source tree, so a refresh needs no project change.

- **Methods:** every XRPC call in `src/` names an upstream query (sent as GET) or procedure (sent as
  POST). The calls are found in source, where each client passes a literal NSID to the internal
  `XrpcClient`.
- **NSIDs:** every NSID or `nsid#def` string literal in `src/` names an upstream def.
- **Models:** every Lexicon model maps to one upstream object: by the `$type` it writes, the
  discriminator its union declares, its name (`FooResponse` is method `foo`'s output, `FooRequest`
  its input, `FooRecord` record `foo`, any other `Foo` the def `foo` in its namespace), or an
  explicit `DefOf` entry. Its `[JsonPropertyName]`s must all exist on that def, and it must declare
  all of the def's properties.
- **Permission sets:** `AtProtoScopes.PermissionSets` holds exactly the published permission sets.
- **knownValues:** each constants class (`NotificationReasons`, `JobState`, …) matches the values
  upstream lists.

Every allow-list entry must still match something, so an entry cannot outlive its reason.
