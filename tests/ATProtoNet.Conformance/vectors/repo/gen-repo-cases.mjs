// Writes commit-blocks.json (signed commit verification) and record-proof-cases.json (record proof
// verification): the reference commit and proofs of record-proof.json, and tampered copies of them,
// from 0.8 CommitBlockTests and RecordProofTests. `node gen-repo-cases.mjs` (Node 22, no packages)
// rewrites both files; the input is the reference output, so nothing here signs.
import { readFileSync, writeFileSync } from 'node:fs'
import { DAG_CBOR, Entries, Link, car, cid, cidFromString, cidString, concat, decode, encode, hex, json, readCar } from '../gen-lib.mjs'

const reference = JSON.parse(readFileSync(new URL('record-proof.json', import.meta.url)))
const present = readCar(new Uint8Array(Buffer.from(reference.presentCar, 'base64')))
const roots = (proof) => decode(proof.header).pairs.find(([k]) => k === 'roots')[1].map((l) => l.bin)
const commitCid = roots(present)[0]
const find = (proof, c) => proof.blocks.find(([b]) => Buffer.compare(Buffer.from(b), Buffer.from(c)) === 0)[1]
const commit = find(present, commitCid)
const fields = decode(commit).pairs
if (hex(encode(new Entries(fields))) !== hex(commit)) throw new Error('the reference commit does not re-encode canonically')

const otherK256 = JSON.parse(readFileSync(new URL('../crypto/w3c_didkey_K256.json', import.meta.url)))[0].publicDidKey
const otherP256 = JSON.parse(readFileSync(new URL('../crypto/w3c_didkey_P256.json', import.meta.url)))[0].publicDidKey
const K256_ORDER = 0xfffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141n

// The commit with fields replaced (undefined removes one), canonically encoded.
const commitWith = (changes) =>
  encode(Object.fromEntries(fields.filter(([k]) => !(k in changes) || changes[k] !== undefined).map(([k, v]) => [k, k in changes ? changes[k] : v])))
const signature = fields.find(([k]) => k === 'sig')[1]
const flipped = Uint8Array.from(signature, (b, i) => (i === 10 ? b ^ 1 : b))
const s = BigInt('0x' + hex(signature.slice(32)))
const highS = concat(signature.slice(0, 32), Buffer.from((K256_ORDER - s).toString(16).padStart(64, '0'), 'hex'))
const notACid = new Link(new Uint8Array([0x01, 0x71, 0x12, 0x20, 0x00]))
const key = reference.signingKey

const commits = [
  ['reference commit', 'signed by @atproto/repo 0.10.14 (REP-25, REP-26)', commit, key, 'valid'],
  ['another K-256 key', 'REP-31', commit, otherK256, 'bad-signature'],
  ['a P-256 key', 'REP-31', commit, otherP256, 'bad-signature'],
  ['signature altered', 'REP-31', commitWith({ sig: flipped }), key, 'bad-signature'],
  ['high-S signature', 'REP-31, CRY-11: the same signature with s replaced by n - s', commitWith({ sig: highS }), key, 'bad-signature'],
  ['signature 63 bytes', 'REP-31', commitWith({ sig: signature.slice(0, 63) }), key, 'bad-signature'],
  ['trailing bytes', 'REP-27: the CID covers them, the signature does not', concat(commit, [0x00]), key, 'malformed'],
  ['duplicate key', 'REP-27', encode(new Entries([...fields, ['did', 'did:plc:someoneelse']])), key, 'malformed'],
  ['keys out of canonical order', 'REP-27, DAT-28: commits are read strictly', encode(new Entries([...fields].reverse())), key, 'malformed'],
  ['no sig', 'REP-28', commitWith({ sig: undefined }), key, 'malformed'],
  ['empty sig', 'REP-28', commitWith({ sig: new Uint8Array() }), key, 'malformed'],
  ['sig as text', 'REP-28', commitWith({ sig: 'not bytes' }), key, 'malformed'],
  ['version 2', 'REP-28', commitWith({ version: 2 }), key, 'malformed'],
  ['no rev', 'REP-28', commitWith({ rev: undefined }), key, 'malformed'],
  ['data not a CID', 'REP-28, REP-30', commitWith({ data: notACid }), key, 'malformed'],
  ['prev not a CID', 'REP-28, REP-30', commitWith({ prev: notACid }), key, 'malformed'],
  ['did not a DID', 'REP-29', commitWith({ did: 'not-a-did' }), key, 'malformed'],
  ['rev not a TID', 'REP-29', commitWith({ rev: 'not-a-tid' }), key, 'malformed'],
  ['truncated', 'REP-27', commit.slice(0, 20), key, 'malformed'],
  ['not a map', 'REP-27', new Uint8Array([0x80]), key, 'malformed'],
  ['indefinite map', 'REP-27', new Uint8Array([0xbf, 0xff]), key, 'malformed'],
  ['empty', 'REP-27', new Uint8Array(), key, 'malformed'],
].map(([name, note, block, signingKey, verdict]) => ({ name, note, block: hex(block), signingKey, verdict }))
writeFileSync(new URL('commit-blocks.json', import.meta.url), json(commits))

// The JSON data model of a decoded value.
const toJson = (v) => v instanceof Link ? { $link: cidString(v.bin) }
  : v instanceof Uint8Array ? { $bytes: Buffer.from(v).toString('base64').replace(/=+$/, '') }
  : v instanceof Entries ? Object.fromEntries(v.pairs.map(([k, x]) => [k, toJson(x)]))
  : Array.isArray(v) ? v.map(toJson) : v

const recordCid = cidFromString(reference.recordCid)
const same = (a, b) => Buffer.compare(Buffer.from(a), Buffer.from(b)) === 0
const rewrite = (proof, map, newRoots = roots(proof)) =>
  car(newRoots, proof.blocks.map(map).filter(Boolean))
const presentBytes = Buffer.from(reference.presentCar, 'base64'), absentBytes = Buffer.from(reference.absentCar, 'base64')
const dataCid = fields.find(([k]) => k === 'data')[1].bin
const forged = encode({ $type: 'com.example.record', text: 'forged' })
const altered = commitWith({ sig: flipped })

const proofs = []
const proof = (name, note, bytes, status, extra = {}) => proofs.push({
  name, note, car: hex(bytes), did: reference.did, collection: reference.collection, rkey: reference.rkey, signingKey: key, status, ...extra,
})
proof('reference proof', 'REP-34', presentBytes, 'present', { recordCid: reference.recordCid, record: toJson(decode(find(present, recordCid))) })
proof('reference absence proof', 'REP-35', absentBytes, 'absent', { rkey: reference.absentRkey })
proof('proof of a record proves its neighbour absent', 'REP-35', presentBytes, 'absent', { rkey: reference.absentRkey })
proof('absence proof does not prove the record it passes by', 'REP-35', absentBytes, 'invalid')
proof('key off the proven path', 'REP-36', presentBytes, 'invalid', { rkey: '3lenax35zzz22' })
proof('another signing key', 'REP-33, REP-37', presentBytes, 'invalid', { signingKey: otherK256 })
proof('another repository', 'REP-33', presentBytes, 'invalid', { did: 'did:plc:ewvi7nxzyoun6zhxrhs64oiz' })
proof('signing key not a did:key', 'REP-37', presentBytes, 'invalid', { signingKey: 'did:key:zNotAKey' })
proof('record bytes altered', 'REP-33: the block no longer matches its CID', rewrite(present, ([c, d]) => [c, same(c, recordCid) ? Uint8Array.from(d, (b, i) => (i === 20 ? b ^ 1 : b)) : d]), 'invalid')
proof('record swapped for another with its own CID', 'REP-34', rewrite(present, ([c, d]) => (same(c, recordCid) ? [cid(forged, DAG_CBOR), forged] : [c, d])), 'invalid')
proof('tree node missing', 'REP-36', rewrite(present, ([c, d]) => (same(c, dataCid) ? null : [c, d])), 'invalid')
proof('commit block missing', 'REP-36', rewrite(present, ([c, d]) => (same(c, commitCid) ? null : [c, d])), 'invalid')
proof('signature altered', 'REP-33', rewrite(present, ([c, d]) => (same(c, commitCid) ? [cid(altered, DAG_CBOR), altered] : [c, d]), [cid(altered, DAG_CBOR)]), 'invalid')
proof('two roots', 'REP-32', rewrite(present, (b) => b, [commitCid, commitCid]), 'invalid')
proof('not a CAR', 'REP-32', new Uint8Array([1, 2, 3]), 'invalid')
writeFileSync(new URL('record-proof-cases.json', import.meta.url), json(proofs))
