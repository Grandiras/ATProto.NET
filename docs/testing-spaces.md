# Testing against a real space host

The unit tests for [spaces](spaces.md) stub the HTTP layer: the cryptographic constructions are pinned against the reference implementation's own outputs, and the wire shape against hand-written JSON. That proves the SDK agrees with a *reading* of the specification. It cannot prove a server accepts what the SDK sends — whether the `htu` in a DPoP proof is the one the host computes for the request, whether the delegation token it presents as a bearer grant is honoured as one, whether the LtHash the SDK folds from an oplog lands on the digest the host independently signed.

`tests/ATProtoNet.IntegrationTests/` carries that second set, behind `[RequiresSpacesFact]`. They skip unless the environment says a space host is there, so CI is unaffected.

## There is no released server yet

No PDS release serves `com.atproto.space.*`. The implementation lives on [bluesky-social/atproto#5187](https://github.com/bluesky-social/atproto/pull/5187) (branch `permissioned-data`), which is a work in progress, so there is no container image to pull and the endpoints may still change. Until there is one, the host these tests run against is built from that branch.

## Standing one up

Requires Node 22+ and pnpm 11.

```bash
git clone --depth 1 -b permissioned-data https://github.com/bluesky-social/atproto.git
cd atproto
pnpm install
pnpm build                                              # test files fail to type-check; dist/ is still emitted
pnpm --filter @atproto/oauth-provider-ui run build       # the PDS refuses to boot without this bundle
```

Then run a network — a PLC directory and a PDS, both in-process:

```js
// space-net.mjs, at the repo root
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

`ATPROTO_PLC_URL` matters more than it looks. A space credential is exchanged with, and a commit verified against, whatever the DID document says — so the tests have to resolve DIDs through the test network's own directory rather than the public one. `SpaceNetworkFixture` builds a `DidResolver` over it and hands that to `SpaceCredentialProvider`; nothing about the SDK is special-cased for the test network.

Set `ATPROTO_REQUIRE_INTEGRATION=1` to make a missing prerequisite a failure rather than a skip — `dotnet test --filter` exits 0 when every matched test skips, so a job whose environment drifted would otherwise pass while verifying nothing.

## What the tests cover

`SpaceNetworkFixture` provisions three accounts, because a space is a three-party arrangement a stub cannot tell apart: an **authority** that owns the space and mints credentials, a **member** whose repo is read across the repo boundary, and an **outsider** the authority must refuse. Every test creates its own space, so the suite is order-independent.

| Suite | What only a real host can answer |
| --- | --- |
| `SpaceCredentialTests` | The two-hop exchange end to end, and the refusals that make it worth something: a replayed delegation token, a token for another space, a proof signed by another key, a proof addressed to another host, a credential presented as a bearer token. |
| `SpaceRepoSyncTests` | The CAR round trip — write records, fetch `getRepo`, and verify the server's commit and index with `SpaceRepoCar.Verify`. This is the highest-value one: it checks the SDK's LtHash, commit-context encoding, MAC, and canonical DAG-CBOR ordering against a real implementation rather than against a reimplementation of the same spec. Then incremental sync over a real oplog, divergence detection, and the fallback to full recovery. |
| `SimpleSpacePolicyTests` | Who a real authority admits: a non-member refused under `member-list`, an app refused under `#allowList`, revocation taking effect at the next renewal, and the repo boundary holding between two accounts on the same host. |

The reference implementation's own suite, `packages/pds/tests/space/`, is a good map of what else is worth asserting.

## Two things to know about the dev network

- **Its PLC is older than production's.** It publishes `EcdsaSecp256k1VerificationKey2019` verification methods, whose `publicKeyMultibase` is a bare uncompressed point; plc.directory publishes `Multikey`, whose value is multicodec-tagged. The SDK reads both (`DidDocument.GetSigningKey()`), so `SpaceSyncer.ResolveSigningKeyAsync` works against either network and the fixture uses it directly.
- **The writer set is eventually consistent.** `listRepos` is maintained from write notifications the writing PDS sends without awaiting, so a test that has just written polls for its own entry rather than expecting it on the next request.
