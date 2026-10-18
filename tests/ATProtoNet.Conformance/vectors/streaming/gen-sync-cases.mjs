// Writes sync-cases.json: Sync 1.1 #commit and #sync frames built with the reference
// implementation, for the conformance suite's Sync 1.1 area. Run with @atproto/repo 0.10.14 and
// @atproto/crypto 0.5.5 installed:
// `node gen-sync-cases.mjs firehose-sync-frames.json > sync-cases.json`.
// The output is deterministic: fixed keys, revisions, records and times; RFC 6979 signatures.
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { gzipSync } from 'node:zlib'
import { Secp256k1Keypair } from '@atproto/crypto'
import { decode, decodeAll, encode } from '@atproto/lex-cbor'
import { cidForCbor, parseCid } from '@atproto/lex-data'
import { BlockMap, DataDiff, MemoryBlockstore, Repo, blocksToCarFile, readCarWithRoot, signCommit } from '@atproto/repo'

const key = (seed) => Secp256k1Keypair.import(createHash('sha256').update(seed).digest('hex'))
const repoKey = await key('ATProto.NET sync vector key')
const forger = await key('ATProto.NET sync vector forger')
const did = 'did:plc:synctestrepoaaaaaaaaaaaa'
const now = '2026-10-01T00:00:00.000Z'
const nowMicros = Date.parse(now) * 1000
const time = '2026-09-26T12:00:00.000Z'
const foreignCid = parseCid('bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm')
const notTheRoot = parseCid('bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta')

const ALPHA = '234567abcdefghijklmnopqrstuvwxyz'
const tid = (micros, clock) => {
  let x = (BigInt(micros) << 10n) | BigInt(clock)
  let s = ''
  for (let j = 0; j < 13; j++) { s = ALPHA[Number(x & 31n)] + s; x >>= 5n }
  return s
}

// 2026-01-01T00:00:00Z: revisions tick up a second per commit from here.
const EPOCH = 1767225600000000
let clock = EPOCH
let next = 0
const nextRev = (micros) => tid(clock = micros ?? clock + 1_000_000, 7)
const newPath = (collection = 'com.example.record') => `${collection}/${tid(EPOCH + ++next * 7919, 1)}`

const frame = (type, body) => Buffer.concat([encode({ t: type, op: 1 }), encode(body)])

/**
 * The next commit of `repo` applying `ops` ([action, path] pairs), and the #commit event a Sync 1.1
 * PDS emits for it (the covering proof of every op, the new records and the signed commit), with
 * `tweaks` getting one thing wrong. Returns the repository after the commit and the event.
 */
const commit = async (repo, ops, tweaks = {}) => {
  const leaves = new BlockMap()
  const repoOps = []
  let data = repo.data
  for (const [action, path] of ops) {
    const previous = await data.get(path)
    let cid = null
    if (action !== 'delete') cid = await leaves.add({ n: ++next, $type: 'com.example.record' })
    data = action === 'create' ? await data.add(path, cid) : action === 'update' ? await data.update(path, cid) : await data.delete(path)
    repoOps.push({ action, path, cid, ...(previous ? { prev: previous } : {}) })
  }

  const dataCid = await data.getPointer()
  const diff = await DataDiff.of(data, repo.data)
  const newBlocks = diff.newMstBlocks
  newBlocks.addMap(leaves.getMany(diff.newLeafCids.toList()).blocks)
  const rev = nextRev(tweaks.revMicros)
  const signed = await signCommit(
    { did: tweaks.signedDid ?? did, version: tweaks.version ?? 3, rev: tweaks.signedRev ?? rev, prev: null, data: dataCid },
    tweaks.signWith ?? repoKey)
  const commitBytes = encode(signed)
  const commitCid = await cidForCbor(commitBytes)
  newBlocks.set(commitCid, commitBytes)

  const blocks = new BlockMap()
  blocks.set(commitCid, commitBytes)
  // A commit without ops carries the unchanged root node, so its tree can still be loaded.
  if (ops.length === 0) blocks.set(dataCid, await repo.storage.getBytes(dataCid))
  else if (tweaks.rootOnly) blocks.set(dataCid, (await data.getCoveringProof(ops[0][1])).get(dataCid))
  else for (const [, path] of ops) blocks.addMap(await data.getCoveringProof(path))
  if (!tweaks.omitRecords) blocks.addMap(leaves)

  const next_ = await repo.applyCommit({
    cid: commitCid, rev, since: repo.commit.rev, prev: repo.cid, newBlocks, relevantBlocks: blocks, removedCids: diff.removedCids,
  })
  const body = {
    seq: next,
    rebase: false,
    tooBig: false,
    repo: did,
    commit: tweaks.commit ?? commitCid,
    rev,
    since: tweaks.omitSince ? null : tweaks.since ?? repo.commit.rev,
    blocks: await blocksToCarFile(commitCid, blocks),
    ops: tweaks.ops ? tweaks.ops(repoOps) : repoOps,
    blobs: [],
    time,
  }
  if (!tweaks.omitPrevData) body.prevData = tweaks.prevData ?? repo.data.pointer ?? await repo.data.getPointer()
  return { repo: next_, event: body, data: dataCid.toString(), rev }
}

/** A #sync event asserting the repository's current tree at a new revision. */
const sync = async (repo, tweaks = {}) => {
  const rev = nextRev(tweaks.revMicros)
  const signed = await signCommit(
    { did, version: 3, rev, prev: null, data: tweaks.data ?? await repo.data.getPointer() }, tweaks.signWith ?? repoKey)
  const bytes = encode(signed)
  const cid = await cidForCbor(bytes)
  const blocks = new BlockMap()
  blocks.set(cid, bytes)
  return { event: { seq: ++next, did, rev, blocks: await blocksToCarFile(cid, blocks), time }, rev }
}

const b64 = (bytes) => Buffer.from(bytes).toString('base64')
const step = (type, event, expect, after) => {
  const bytes = frame(type, event)
  const out = bytes.length > 100_000 ? { frameGzip: b64(gzipSync(bytes, { level: 9 })) } : { frame: b64(bytes) }
  return { ...out, expect, ...(after ? { after } : {}) }
}
const commitStep = (c, expect) =>
  step('#commit', c.event, expect, expect === 'valid' || expect === 'desynchronized'
    ? { rev: c.rev, data: c.data, synchronized: expect === 'valid' } : undefined)
const stateOf = async (repo, synchronized = true) => ({ rev: repo.commit.rev, data: (await repo.data.getPointer()).toString(), synchronized })

const fresh = async () => {
  const storage = new MemoryBlockstore()
  return Repo.createFromCommit(storage, await Repo.formatInitCommit(storage, did, repoKey, [], nextRev()))
}

const cases = []
const add = (name, spec, state, steps, extra = {}) => cases.push({ name, spec, state, ...extra, steps })

// ── The chain ────────────────────────────────────────────
{
  let repo = await fresh()
  const [a, b, c] = [newPath(), newPath(), newPath('com.example.other')]
  const steps = []
  for (const ops of [[['create', a], ['create', b]], [['update', a], ['create', c]], [['delete', b]], [['delete', a], ['update', c]], []]) {
    const made = await commit(repo, ops)
    steps.push(commitStep(made, 'valid'))
    repo = made.repo
  }
  add('creates, updates and deletes chain across commits', 'STR-42', null, steps)
}
{
  let repo = await fresh()
  for (let i = 0; i < 30; i++)
    repo = (await commit(repo, Array.from({ length: 10 }, () => ['create', newPath()]))).repo
  const existing = []
  for await (const entry of repo.data.walk()) if (entry.isLeaf()) existing.push(entry.key)
  const made = await commit(repo, [['delete', existing[3]], ['update', existing[150]], ['create', newPath()]])
  add('many records chain over the covering proof', 'STR-42', await stateOf(repo), [commitStep(made, 'valid')])
}
{
  let repo = await fresh()
  const first = await commit(repo, [['create', newPath()]])
  const missed = await commit(first.repo, [['create', newPath()]])
  const broken = await commit(missed.repo, [['create', newPath()]])
  const after = await commit(broken.repo, [['create', newPath()]])
  add('missed commit desynchronizes once and holds the repository', 'STR-44', null,
    [commitStep(first, 'valid'), commitStep(broken, 'desynchronized'), step('#commit', after.event, 'desynchronized')])
}
{
  const first = await commit(await fresh(), [['create', newPath()]])
  const second = await commit(first.repo, [['create', newPath()]], { since: '3aaaaaaaaaaaa' })
  add('since mismatch but prevData matches', 'STR-44', await stateOf(first.repo), [commitStep(second, 'valid')])
}
{
  const [kept, deleted] = [newPath(), newPath()]
  const first = await commit(await fresh(), [['create', kept], ['create', deleted]])
  const second = await commit(first.repo, [['update', kept], ['delete', deleted]], { omitPrevData: true, ops: (ops) => [ops[0]] })
  add('prevData stripped and an operation dropped', 'STR-44', await stateOf(first.repo), [commitStep(second, 'desynchronized')])
}
{
  const first = await commit(await fresh(), [['create', newPath()]])
  const second = await commit(first.repo, [['create', newPath()]], { omitPrevData: true })
  add('commit without prevData after a chain', 'STR-44', await stateOf(first.repo), [commitStep(second, 'desynchronized')])
}
{
  const first = await commit(await fresh(), [['create', newPath()]], { omitPrevData: true, omitSince: true })
  add('first commit without prevData starts the chain', 'STR-44', null, [commitStep(first, 'valid')])
}
{
  const a = await commit(await fresh(), [['create', newPath()]])
  const b = await commit(a.repo, [['create', newPath()]])
  const c = await commit(b.repo, [['create', newPath()]])
  add('unknown repository starts its chain', 'STR-44', null, [commitStep(c, 'valid')])
}
{
  const first = await commit(await fresh(), [['create', newPath()]])
  const later = await commit(first.repo, [['create', newPath()]], { revMicros: 1_780_000_000_000_000 })
  add('revision not after the last is stale', 'STR-41', null, [commitStep(later, 'valid'), step('#commit', first.event, 'stale')])
}

// ── #sync ────────────────────────────────────────────────
{
  const first = await commit(await fresh(), [['create', newPath()]])
  const same = await sync(first.repo)
  const after = await commit(first.repo, [['create', newPath()]])
  add('sync event of the same tree moves the revision on', 'STR-45', await stateOf(first.repo), [
    step('#sync', same.event, 'valid', { rev: same.rev, data: (await first.repo.data.getPointer()).toString(), synchronized: true }),
    commitStep(after, 'valid'),
  ])
}
{
  const first = await commit(await fresh(), [['create', newPath()]])
  const moved = await commit(first.repo, [['create', newPath()]])
  const other = await sync(moved.repo)
  add('sync event of another tree desynchronizes', 'STR-45', await stateOf(first.repo), [step('#sync', other.event, 'desynchronized')])
}
{
  const repo = await fresh()
  add('sync event with a forged signature', 'STR-45', null, [step('#sync', (await sync(repo, { signWith: forger })).event, 'invalid')])
}
{
  // Anyone holding the signing key can sign any data link; it must not end the consumer.
  const rev = nextRev()
  const signed = await signCommit({ did, version: 3, rev, prev: null, data: new Uint8Array([0x01, 0x71, 0x12, 0x20, 0x00]) }, repoKey)
  const bytes = encode(signed)
  const cid = await cidForCbor(bytes)
  const blocks = new BlockMap()
  blocks.set(cid, bytes)
  add('sync event whose signed data is not a CID', 'STR-45', null,
    [step('#sync', { seq: ++next, did, rev, blocks: await blocksToCarFile(cid, blocks), time }, 'invalid')])
}

// ── The checklist, one rule at a time (0.8's Tampered table) ──
{
  let base = await fresh()
  const existing = newPath()
  base = (await commit(base, [['create', existing], ['create', newPath()]])).repo
  for (let i = 0; i < 20; i++) base = (await commit(base, [['create', newPath()]])).repo
  const state = await stateOf(base)
  const copy = (op, change) => {
    const out = { ...op, ...change }
    if (out.prev === undefined) delete out.prev
    return out
  }
  const update = (tweaks) => commit(base, [['update', existing], ['create', newPath()]], tweaks)
  const remove = (tweaks) => commit(base, [['delete', existing]], tweaks)
  const create = (tweaks) => commit(base, [['create', newPath()]], tweaks)
  const rules = {
    'version 2': () => create({ version: 2 }),
    'signed by another key': () => create({ signWith: forger }),
    'signed for another DID': () => create({ signedDid: 'did:plc:someoneelseaaaaaaaaaaaa' }),
    'signed with another revision': () => create({ signedRev: '3jzfcijpj2z2a' }),
    'commit is not the root': () => create({ commit: notTheRoot }),
    'record block missing': () => create({ omitRecords: true }),
    'operation missing': () => update({ ops: (ops) => [ops[0]] }),
    'operation not in the tree': () => update({
      ops: (ops) => [...ops, { action: 'create', path: 'com.example.record/3zzzzzzzzzzzz', cid: ops[1].cid }],
    }),
    'two operations on one path': () => update({ ops: (ops) => [ops[0], ops[1], ops[1]] }),
    'create with prev': () => update({ ops: (ops) => [ops[0], copy(ops[1], { prev: ops[0].prev })] }),
    'update without prev': () => update({ ops: (ops) => [copy(ops[0], { prev: undefined }), ops[1]] }),
    'delete without prev': () => remove({ ops: (ops) => [copy(ops[0], { prev: undefined })] }),
    'delete with cid': () => remove({ ops: (ops) => [copy(ops[0], { cid: ops[0].prev })] }),
    'proof missing': () => update({ rootOnly: true }),
    'invalid path': () => update({ ops: (ops) => [ops[0], copy(ops[1], { path: 'com.example.record/not a key' })] }),
  }
  for (const [rule, make] of Object.entries(rules))
    add(`tampered: ${rule}`, 'STR-48', state, [commitStep(await make(), 'invalid')], { rule })
}

// ── Limits ───────────────────────────────────────────────
{
  const repo = await fresh()
  const most = await commit(repo, Array.from({ length: 200 }, () => ['create', newPath()]))
  const tooMany = await commit(repo, Array.from({ length: 201 }, () => ['create', newPath()]))
  add('the maximum number of operations', 'STR-49', null, [commitStep(most, 'valid')])
  add('one operation over the maximum', 'STR-49', null, [commitStep(tooMany, 'invalid')])
  const made = await commit(repo, [['create', newPath()]])
  add('blocks one byte over the limit', 'STR-49', null, [step('#commit', { ...made.event, blocks: new Uint8Array(2_000_001) }, 'invalid')])
  const soon = await commit(repo, [['create', newPath()]], { revMicros: nowMicros + 4 * 60_000_000 })
  const later = await commit(repo, [['create', newPath()]], { revMicros: nowMicros + 6 * 60_000_000 })
  add('revision four minutes ahead', 'STR-49', null, [commitStep(soon, 'valid')])
  add('revision six minutes ahead', 'STR-49', null, [commitStep(later, 'invalid')])
  const unusable = await commit(repo, [['create', newPath()]])
  add('account publishing no usable key', 'STR-47', null, [commitStep(unusable, 'invalid')], { signingKey: null })
}

// ── Live frames, altered ────────────────────────────────
const live = JSON.parse(readFileSync(process.argv[2], 'utf8'))
const liveFrame = (name) => decodeAll(Buffer.from(live.frames.find((f) => f.name === name).frame, 'base64'))
const liveCase = (name, from, alter, expect = 'invalid') => {
  const [header, body] = liveFrame(from)
  alter(body)
  add(name, 'STR-42', null, [{ frame: b64(Buffer.concat([encode(header), encode(body)])), expect }],
    { signingKey: live.signingKeys[body.repo] })
}
const liveStep = async (name, expect = 'valid') => {
  const { frame: text } = live.frames.find((f) => f.name === name)
  const [, body] = liveFrame(name)
  if (expect !== 'valid') return { frame: text, expect }
  const car = await readCarWithRoot(body.blocks)
  const signed = decode(car.blocks.get(car.root))
  return { frame: text, expect, after: { rev: body.rev, data: signed.data.toString(), synchronized: true } }
}
const keyOf = (name) => { const [, body] = liveFrame(name); return live.signingKeys[body.repo ?? body.did] }
for (const { name } of live.frames)
  add(`live frame: ${name}`, 'STR-50', null, [await liveStep(name)], { signingKey: keyOf(name) })
add('live consecutive commits chain', 'STR-50', null, [await liveStep('chain-first'), await liveStep('chain-second')],
  { signingKey: keyOf('chain-first') })
add('live commit replayed', 'STR-50', null, [await liveStep('chain-first'), await liveStep('chain-first', 'stale')],
  { signingKey: keyOf('chain-first') })
liveCase('live commit missing an operation', 'multi-op', (b) => { b.ops = b.ops.slice(1) })
liveCase('live commit with a forged prevData', 'update', (b) => { b.prevData = foreignCid })
liveCase('live delete claimed as another record', 'delete', (b) => {
  b.ops = [{ action: 'delete', path: b.ops[0].path + 'x', cid: null, prev: b.ops[0].prev }]
})
{
  const [, body] = liveFrame('chain-second')
  add('live commit after a missed one', 'STR-44', { rev: '3mwgnaaaaaaa2', data: foreignCid.toString(), synchronized: true },
    [{ frame: live.frames.find((f) => f.name === 'chain-second').frame, expect: 'desynchronized' }],
    { signingKey: live.signingKeys[body.repo] })
}
{
  const [, body] = liveFrame('chain-first')
  add('live commit under another key', 'STR-47', null,
    [{ frame: live.frames.find((f) => f.name === 'chain-first').frame, expect: 'invalid' }], { signingKey: forger.did() })
}

console.log(JSON.stringify({
  generator: { '@atproto/repo': '0.10.14', '@atproto/crypto': '0.5.5' },
  now,
  did,
  signingKey: repoKey.did(),
  cases,
}, null, 2))
