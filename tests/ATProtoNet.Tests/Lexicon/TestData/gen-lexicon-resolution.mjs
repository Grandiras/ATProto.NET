// Writes lexicon-resolution.json: an interop vector for NSID -> lexicon resolution
// (DNS-authority hook -> did:plc document -> com.atproto.sync.getRecord proof -> lexicon
// validation), produced by the reference implementation. Run with @atproto/repo 0.10.14,
// @atproto/crypto 0.5.5 and @atproto/lex-resolver 0.3.0 installed:
// `node gen-lexicon-resolution.mjs > lexicon-resolution.json`.
import { createHash } from 'node:crypto'
import { Secp256k1Keypair } from '@atproto/crypto'
import { MemoryBlockstore, Repo, WriteOpAction, getRecords } from '@atproto/repo'
import { LexResolver } from '@atproto/lex-resolver'

const did = 'did:plc:lexiconvectorlexiconvect'
const pds = 'https://pds.example.com'
const keypair = await Secp256k1Keypair.import(
  createHash('sha256').update('ATProto.NET lexicon resolution vector').digest('hex'))

// Deterministic, TID-shaped record keys (same scheme as gen-record-proof.mjs).
const ALPHA = '234567abcdefghijklmnopqrstuvwxyz'
const rkeyFor = (i) => {
  let x = BigInt(1735689600000000 + i * 7919) << 10n
  let s = ''
  for (let j = 0; j < 13; j++) { s = ALPHA[Number(x & 31n)] + s; x >>= 5n }
  return s
}

// ~40 filler records so the MST spans more than one layer.
const writes = []
for (let i = 0; i < 40; i++) {
  writes.push({
    action: WriteOpAction.Create,
    collection: 'com.example.note',
    rkey: rkeyFor(i),
    record: { $type: 'com.example.note', text: `note ${i}`, createdAt: '2026-01-01T00:00:00.000Z' },
  })
}

// com.atproto.lexicon.schema records, rkey = the NSID being published (the publishing
// convention). `com.example.lexicon.missing` is intentionally left out of the repo.
const validPostBody = {
  type: 'object',
  required: ['text', 'createdAt'],
  properties: {
    text: { type: 'string', maxLength: 300 },
    createdAt: { type: 'string', format: 'datetime' },
  },
}

const lexiconRecords = {
  'com.example.lexicon.post': {
    $type: 'com.atproto.lexicon.schema',
    lexicon: 1,
    id: 'com.example.lexicon.post',
    description: 'A test post record.',
    defs: { main: { type: 'record', key: 'tid', record: validPostBody } },
  },
  'com.example.lexicon.authBasic': {
    $type: 'com.atproto.lexicon.schema',
    lexicon: 1,
    id: 'com.example.lexicon.authBasic',
    defs: {
      main: {
        type: 'permission-set',
        title: 'Basic posting',
        'title:lang': { de: 'Einfaches Posten' },
        detail: 'Create and delete posts.',
        permissions: [
          { type: 'permission', resource: 'repo', collection: ['com.example.lexicon.post'] },
          { type: 'permission', resource: 'rpc', inheritAud: true, lxm: ['com.example.lexicon.getPosts'] },
        ],
      },
    },
  },
  // Valid lexicon document, but its `id` doesn't match its own rkey/NSID.
  'com.example.lexicon.mismatch': {
    $type: 'com.atproto.lexicon.schema',
    lexicon: 1,
    id: 'com.example.lexicon.other',
    description: 'A test post record.',
    defs: { main: { type: 'record', key: 'tid', record: validPostBody } },
  },
  // Otherwise-valid lexicon document, but `$type` isn't `com.atproto.lexicon.schema`.
  'com.example.lexicon.badType': {
    $type: 'com.example.notASchema',
    lexicon: 1,
    id: 'com.example.lexicon.badType',
    description: 'A test post record.',
    defs: { main: { type: 'record', key: 'tid', record: validPostBody } },
  },
}

for (const [rkey, record] of Object.entries(lexiconRecords)) {
  writes.push({ action: WriteOpAction.Create, collection: 'com.atproto.lexicon.schema', rkey, record })
}

const storage = new MemoryBlockstore()
const repo = await Repo.createFromCommit(
  storage, await Repo.formatInitCommit(storage, did, keypair, writes, '3m7fmkxcn5d2a'))

const proof = async (rkey) => {
  const chunks = []
  for await (const chunk of getRecords(storage, repo.cid, [{ collection: 'com.atproto.lexicon.schema', rkey }])) {
    chunks.push(chunk)
  }
  return Buffer.concat(chunks)
}

// One CAR per rkey: four present-record proofs plus one absence proof for "missing".
const nsids = [
  'com.example.lexicon.post',
  'com.example.lexicon.authBasic',
  'com.example.lexicon.mismatch',
  'com.example.lexicon.badType',
  'com.example.lexicon.missing',
]
const cars = {}
const recordCids = {}
for (const rkey of nsids) {
  cars[rkey] = await proof(rkey)
  recordCids[rkey] = lexiconRecords[rkey]
    ? (await repo.data.get(`com.atproto.lexicon.schema/${rkey}`)).toString()
    : null
}

// The DID document plc.directory would serve for `did`. The @context array matches what
// plc.directory actually returns for a did:plc document with a Multikey atproto key.
const didDocument = {
  '@context': [
    'https://www.w3.org/ns/did/v1',
    'https://w3id.org/security/multikey/v1',
    'https://w3id.org/security/suites/secp256k1-2019/v1',
  ],
  id: did,
  alsoKnownAs: ['at://lexicons.example.com'],
  verificationMethod: [
    {
      id: `${did}#atproto`,
      type: 'Multikey',
      controller: did,
      publicKeyMultibase: keypair.did().replace('did:key:', ''),
    },
  ],
  service: [
    { id: '#atproto_pds', type: 'AtprotoPersonalDataServer', serviceEndpoint: pds },
  ],
}

// Fetch stub used by both the DID resolver and the XRPC agent (LexResolver's `fetch` option
// covers both). Serves only the two URLs the resolution flow needs; anything else throws so
// scope creep (or a resolver bug hitting the network) is caught immediately.
async function customFetch(input) {
  const url = new URL(input instanceof Request ? input.url : input)

  if (url.origin === 'https://plc.directory' && decodeURIComponent(url.pathname.slice(1)) === did) {
    return new Response(JSON.stringify(didDocument), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })
  }

  if (url.origin === pds && url.pathname === '/xrpc/com.atproto.sync.getRecord') {
    const rkey = url.searchParams.get('rkey')
    if (
      url.searchParams.get('did') === did &&
      url.searchParams.get('collection') === 'com.atproto.lexicon.schema' &&
      cars[rkey]
    ) {
      return new Response(cars[rkey], {
        status: 200,
        headers: { 'content-type': 'application/vnd.ipld.car' },
      })
    }
  }

  throw new Error(`Unexpected fetch: ${url.toString()}`)
}

const resolver = new LexResolver({
  fetch: customFetch,
  plcDirectoryUrl: 'https://plc.directory',
  hooks: { onResolveAuthority: () => did },
})

const verdict = async (nsid) => {
  try {
    const { uri, cid, lexicon } = await resolver.get(nsid)
    return { ok: true, uri: uri.toString(), cid: cid.toString(), lexiconId: lexicon.id }
  } catch (err) {
    return { ok: false, error: err.message, cause: err.cause?.message }
  }
}

// Sequential (not Promise.all) so output ordering stays deterministic across runs.
const reference = {}
for (const nsid of nsids) reference[nsid] = await verdict(nsid)

const lexicons = {}
for (const nsid of nsids) {
  lexicons[nsid] = {
    recordCid: recordCids[nsid],
    car: cars[nsid].toString('base64'),
    reference: reference[nsid],
  }
}

console.log(JSON.stringify({
  generator: {
    '@atproto/repo': '0.10.14',
    '@atproto/crypto': '0.5.5',
    '@atproto/lex-resolver': '0.3.0',
  },
  did,
  signingKey: keypair.did(),
  pds,
  didDocument,
  commit: repo.cid.toString(),
  rev: repo.commit.rev,
  lexicons,
}, null, 2))
