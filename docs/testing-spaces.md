# Testing against a real space host

The unit tests for [spaces](spaces.md) stub the HTTP layer: the cryptographic constructions are pinned against the reference implementation's own outputs, and the wire shape against hand-written JSON. That proves the SDK agrees with a *reading* of the specification. It cannot prove a server accepts what the SDK sends — whether the `htu` in a DPoP proof is the one the host computes for the request, whether the delegation token it presents as a bearer grant is honoured as one, whether the LtHash the SDK folds from an oplog lands on the digest the host independently signed.

`tests/ATProtoNet.IntegrationTests/` carries that second set, behind `[RequiresSpacesFact]`. They skip unless the environment says a space host is there, so CI is unaffected.

## Where the alpha is published

No stable PDS release serves `com.atproto.space.*`. The implementation lives on [bluesky-social/atproto#5187](https://github.com/bluesky-social/atproto/pull/5187) (branch `permissioned-data`), still an open draft whose endpoints may change. Bluesky does publish an alpha of it, announced in [AT Protocol Spaces (alpha)](https://atproto.com/blog/atproto-spaces-alpha):

| Artifact | What it is |
| --- | --- |
| `ghcr.io/bluesky-social/atproto:pds-spaces-alpha` | The PDS as a container image, built from the `permissioned-data-alpha` branch. The tag moves, so pin the digest: `ghcr.io/bluesky-social/atproto@sha256:4b19b376aa6164b62ede70bb35db79096c6dac9985cecb175e82022860a3d796` was built on 2026-09-15 from `158c439b` and includes the `simplespace` read/write split. It listens on port 3000 and is configured with the same `PDS_*` variables as the reference PDS image. |
| `@atproto/pds@alpha`, `@atproto/dev-env@alpha` on npm | The same code as packages. `0.0.0-spaces-alpha-20260915165437` matches the image above. |
| `https://spaces-alpha.host.bsky.network` | A hosted PDS running the alpha. Invite-only, updated weekly, and its data is not kept. |

The tests provision and delete their own accounts, so they need admin access to the PDS. They also need the PLC directory it registers those accounts with. That rules out the hosted PDS, and the container image needs a PLC directory alongside it. The simplest host is the in-process dev network, which brings its own.

## Standing one up

### From npm

Requires Node 22+. The alpha dev network installs with pnpm. Several `@atproto/lex*` packages also have a plain `0.0.0` release on npm that still carries unresolved `workspace:*` dependencies, and the alpha's `^0.0.0-spaces-alpha-…` ranges resolve to it. The overrides pin them back to the alpha snapshot:

```json
{
  "name": "space-net",
  "private": true,
  "type": "module",
  "dependencies": {
    "@atproto/dev-env": "0.0.0-spaces-alpha-20260915165437"
  },
  "pnpm": {
    "overrides": {
      "@atproto/bsync": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-builder": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-cbor": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-client": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-data": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-document": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-installer": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-json": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-password-session": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-resolver": "0.0.0-spaces-alpha-20260915165437",
      "@atproto/lex-schema": "0.0.0-spaces-alpha-20260915165437"
    }
  }
}
```

```bash
npx pnpm@9 install
```

Then run a network — a PLC directory and a PDS, both in-process:

```js
// space-net.mjs
import { TestNetworkNoAppView } from '@atproto/dev-env'

const network = await TestNetworkNoAppView.create({
  plc: { port: 2582 },
  pds: { port: 2583 },
})

console.log('ready', network.pds.url, network.plc.url)
process.on('SIGTERM', () => network.close().then(() => process.exit(0)))
setInterval(() => {}, 1 << 30)
```

```bash
node space-net.mjs
```

The dev PDS requires no invite codes and its admin password is `admin-pass`.

### From source

To test against a revision that has not been published yet, build the branch. Requires Node 22+ and pnpm. Pin the commit, since the branch moves:

```bash
git clone -b permissioned-data https://github.com/bluesky-social/atproto.git
cd atproto
git checkout 787a730fcd22ed7791e2636beb509a943017ba1c   # permissioned-data as of 2026-09-22
pnpm install
pnpm build                                              # test files fail to type-check; dist/ is still emitted
pnpm --filter @atproto/oauth-provider-ui run build       # the PDS refuses to boot without this bundle
```

Run the same `space-net.mjs` from the repository root.

## Running the tests

```bash
ATPROTO_TEST_SPACES=true \
ATPROTO_PDS_URL=http://localhost:2583 \
ATPROTO_PLC_URL=http://localhost:2582 \
ATPROTO_PDS_ADMIN_PASSWORD=admin-pass \
dotnet test tests/ATProtoNet.IntegrationTests/ -p:EnableSourceControlManagerQueries=false \
  --filter "FullyQualifiedName~Space"
```

| Variable | Meaning |
| --- | --- |
| `ATPROTO_TEST_SPACES` | `true` enables the space tests. Without it they skip. |
| `ATPROTO_SPACES_PDS_URL` | The PDS serving `com.atproto.space.*`. Falls back to `ATPROTO_PDS_URL`, then `http://localhost:2583`. |
| `ATPROTO_PLC_URL` | The PLC directory that PDS registers accounts with. Defaults to `http://localhost:2582`. |
| `ATPROTO_PDS_ADMIN_PASSWORD` | Used to provision and delete the test accounts. |

`ATPROTO_PLC_URL` matters more than it looks. A space credential is exchanged with, and a commit verified against, whatever the DID document says — so the tests have to resolve DIDs through the test network's own directory rather than the public one. `SpaceNetworkFixture` builds a `CachingDidResolver` over it, with the development opt-out (`AllowPrivateNetworks`) because the directory is on a private network, and hands that to `SpaceCredentialProvider` and `SpaceSyncer`; nothing else about the SDK is special-cased for the test network.

Set `ATPROTO_REQUIRE_INTEGRATION=1` to make a missing prerequisite a failure rather than a skip — `dotnet test --filter` exits 0 when every matched test skips, so a job whose environment drifted would otherwise pass while verifying nothing.

## What the tests cover

`SpaceNetworkFixture` provisions three accounts, because a space is a three-party arrangement a stub cannot tell apart: an **authority** that owns the space and mints credentials, a **member** whose repo is read across the repo boundary, and an **outsider** the authority must refuse. Every test creates its own space, so the suite is order-independent.

| Suite | What only a real host can answer |
| --- | --- |
| `SpaceCredentialTests` | The two-hop exchange end to end, and the refusals that make it worth something: a replayed delegation token, a token for another space, a proof signed by another key, a proof addressed to another host, a credential presented as a bearer token. |
| `SpaceRepoSyncTests` | The CAR round trip — write records, fetch `getRepo`, and verify the server's commit and index with `SpaceRepoCar.Verify`. This is the highest-value one: it checks the SDK's LtHash, commit-context encoding, MAC, and canonical DAG-CBOR ordering against a real implementation rather than against a reimplementation of the same spec. Then incremental sync over a real oplog, divergence detection, and the fallback to full recovery. |
| `SimpleSpacePolicyTests` | Who a real authority admits: a non-member refused under `member-list`, an app refused under `#allowList`, revocation taking effect at the next renewal, and the repo boundary holding between two accounts on the same host. Then the read/write split: `putMember` replacing both flags, `updateSpace` replacing only the policy it names, a read-only member getting a credential while staying out of the writer set, a write-only member being tracked while refused a credential, and a `public` write policy tracking a non-member. |
| `SpaceAccountSigningTests` | An SDK repo host notifying the reference authority of a write, as `ISpaceAccountSigner` makes it: signed as the writer (a DID the test registers in the network's PLC with a key it holds), the writer joins the authority's writer set; signed as the host's own service DID, the reference refuses it. |

The reference implementation's own suite, `packages/pds/tests/space/`, is a good map of what else is worth asserting.

## Two things to know about the dev network

- **Its PLC is older than production's.** It publishes `EcdsaSecp256k1VerificationKey2019` verification methods, whose `publicKeyMultibase` is a bare uncompressed point; plc.directory publishes `Multikey`, whose value is multicodec-tagged. The SDK reads both (`DidDocument.GetSigningKey()`), so `SpaceSyncer` works against either network, and the fixture's `ResolveSigningKeyAsync` reads the key the same way.
- **The writer set is eventually consistent.** `listRepos` is maintained from write notifications the writing PDS sends without awaiting, so a test that has just written polls for its own entry rather than expecting it on the next request. A test that asserts a writer is *absent* first waits for one that must be present — the authority, which is always admitted — so the absence means something.
