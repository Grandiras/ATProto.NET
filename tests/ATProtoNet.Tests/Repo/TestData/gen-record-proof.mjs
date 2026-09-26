// Writes record-proof.json: com.atproto.sync.getRecord proofs produced by the reference
// implementation, for RecordProofTests. Run with @atproto/repo 0.10.14 and @atproto/crypto 0.5.5
// installed: `node gen-record-proof.mjs > record-proof.json`.
import { createHash } from 'node:crypto'
import { Secp256k1Keypair } from '@atproto/crypto'
import { MemoryBlockstore, Repo, WriteOpAction, getRecords, verifyProofs, verifyRecords } from '@atproto/repo'

const did = 'did:plc:vwzwgnygau7ed7b7wt5ux7y2'
const keypair = await Secp256k1Keypair.import(
  createHash('sha256').update('ATProto.NET record proof vector').digest('hex'))

// Deterministic, TID-shaped record keys.
const ALPHA = '234567abcdefghijklmnopqrstuvwxyz'
const rkeyFor = (i) => {
  let x = BigInt(1735689600000000 + i * 7919) << 10n
  let s = ''
  for (let j = 0; j < 13; j++) { s = ALPHA[Number(x & 31n)] + s; x >>= 5n }
  return s
}

const writes = []
for (let i = 0; i < 60; i++) {
  const collection = i % 3 === 0 ? 'com.example.note' : 'com.example.record'
  writes.push({
    action: WriteOpAction.Create,
    collection,
    rkey: rkeyFor(i),
    record: { $type: collection, text: `record ${i}`, createdAt: '2026-01-01T00:00:00.000Z' },
  })
}

// The proven record: nested values, an integer, a null and bytes.
const target = writes[25]
target.record = {
  $type: 'com.example.record',
  text: 'the proven record',
  count: 42,
  flags: [true, false, null],
  nested: { a: 'b', bytes: new Uint8Array([1, 2, 3, 4]) },
  createdAt: '2026-01-02T03:04:05.678Z',
}

const storage = new MemoryBlockstore()
const repo = await Repo.createFromCommit(
  storage, await Repo.formatInitCommit(storage, did, keypair, writes, '3m7fmkxcn5d2a'))

const proof = async (collection, rkey) => {
  const chunks = []
  for await (const chunk of getRecords(storage, repo.cid, [{ collection, rkey }])) chunks.push(chunk)
  return Buffer.concat(chunks)
}

const present = await proof(target.collection, target.rkey)
const absentRkey = target.rkey.slice(0, 12) + 'z'
const absent = await proof(target.collection, absentRkey)

// The reference verifier accepts both.
if ((await verifyRecords(present, did, keypair.did())).length !== 1) throw new Error('present')
const claims = [{ collection: target.collection, rkey: absentRkey, cid: null }]
if ((await verifyProofs(absent, claims, did, keypair.did())).verified.length !== 1) throw new Error('absent')

console.log(JSON.stringify({
  did,
  signingKey: keypair.did(),
  commit: repo.cid.toString(),
  rev: repo.commit.rev,
  collection: target.collection,
  rkey: target.rkey,
  recordCid: (await repo.data.get(`${target.collection}/${target.rkey}`)).toString(),
  absentRkey,
  presentCar: present.toString('base64'),
  absentCar: absent.toString('base64'),
}, null, 2))
