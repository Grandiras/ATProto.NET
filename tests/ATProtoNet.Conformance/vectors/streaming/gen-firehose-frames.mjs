// Writes firehose-frames.json: com.atproto.sync.subscribeRepos frames, each a DAG-CBOR header and
// body as the reference encoder writes them (or deliberately broken bytes), with what a consumer
// must make of the frame. Run with @atproto/lex-cbor 0.1.7 installed (it comes with
// @atproto/repo 0.10.14): `node gen-firehose-frames.mjs > firehose-frames.json`.
import { gzipSync } from 'node:zlib'
import { encode } from '@atproto/lex-cbor'
import { parseCid } from '@atproto/lex-data'

const commitCid = parseCid('bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta')
const recordCid = parseCid('bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry')
const prevData = parseCid('bafyreihlzn2lwoicy7x46zrj4ysc3eqhmpvghca5vbhcwtubpytilc6xsi')
const time = '2024-01-15T12:00:00.000Z'

const header = (op, t) => encode(t === undefined ? { op } : { op, t })
const frame = (op, t, body) => Buffer.concat([header(op, t), body instanceof Uint8Array ? body : encode(body)])
const commit = (change = {}) => ({
  repo: 'did:plc:test123', commit: commitCid, rev: '3jzfcijpj2z2a', since: null, tooBig: false, rebase: false,
  blocks: new Uint8Array(), ops: [], blobs: [], seq: 42, time, ...change,
})

const cases = []
const add = (name, spec, bytes, expect) => cases.push({
  name, spec, ...(bytes.length > 100_000 ? { frameGzip: gzipSync(bytes, { level: 9 }).toString('base64') } : { frame: Buffer.from(bytes).toString('base64') }), expect,
})
const malformed = (seq) => ({ kind: 'dropped', reason: 'malformed', ...(seq === undefined ? {} : { seq }) })
const unknownType = (seq) => ({ kind: 'dropped', reason: 'unknownType', seq })

add('commit', 'STR-35', frame(1, '#commit', commit()), {
  kind: 'event', type: '#commit', seq: 42, repo: 'did:plc:test123', commit: commitCid.toString(), rev: '3jzfcijpj2z2a',
  since: null, time, ops: [],
})
add('commit with operations', 'STR-35', frame(1, '#commit', commit({
  ops: [
    { action: 'create', path: 'app.bsky.feed.post/abc123', cid: recordCid },
    { action: 'delete', path: 'app.bsky.feed.like/def456', cid: null },
  ],
})), {
  kind: 'event', type: '#commit', seq: 42,
  ops: [
    { action: 'create', path: 'app.bsky.feed.post/abc123', cid: recordCid.toString() },
    { action: 'delete', path: 'app.bsky.feed.like/def456', cid: null },
  ],
})
add('commit with Sync 1.1 fields', 'STR-13', frame(1, '#commit', commit({ since: '3jzfcijpj2z22', prevData, blobs: [recordCid] })), {
  kind: 'event', type: '#commit', seq: 42, since: '3jzfcijpj2z22', prevData: prevData.toString(),
})
{
  const { rebase, tooBig, blobs, ...current } = commit()
  add('commit without the deprecated fields', 'STR-13', frame(1, '#commit', current), { kind: 'event', type: '#commit', seq: 42 })
}
add('commit with an unknown field', 'STR-13', frame(1, '#commit', commit({ futureField: { nested: true } })), {
  kind: 'event', type: '#commit', seq: 42,
})
add('commit whose repo is not a DID', 'STR-11', frame(1, '#commit', commit({ repo: 'not-a-did' })), malformed(42))
add('commit whose commit is not a CID link', 'STR-11', frame(1, '#commit', commit({ commit: 'bafyreinotacid' })), malformed(42))
add('commit whose rev is not a TID', 'STR-11', frame(1, '#commit', commit({ rev: 'not-a-tid' })), malformed(42))
add('commit with an unknown operation action', 'STR-13', frame(1, '#commit', commit({
  ops: [{ action: 'merge', path: 'app.bsky.feed.post/abc123', cid: recordCid }],
})), malformed(42))
add('identity', 'STR-35', frame(1, '#identity', { did: 'did:plc:identity123', handle: 'test.bsky.social', seq: 100, time }), {
  kind: 'event', type: '#identity', seq: 100, did: 'did:plc:identity123', handle: 'test.bsky.social',
})
add('account', 'STR-35', frame(1, '#account', { did: 'did:plc:account123', active: true, seq: 200, time }), {
  kind: 'event', type: '#account', seq: 200, did: 'did:plc:account123', active: true,
})
add('inactive account', 'STR-35', frame(1, '#account', { did: 'did:plc:account123', active: false, status: 'takendown', seq: 201, time }), {
  kind: 'event', type: '#account', seq: 201, active: false, status: 'takendown',
})
add('sync', 'STR-35', frame(1, '#sync', { did: 'did:plc:sync456', rev: '3jzfcijpj2z2b', blocks: new Uint8Array([1, 2, 3]), seq: 300, time }), {
  kind: 'event', type: '#sync', seq: 300, did: 'did:plc:sync456', rev: '3jzfcijpj2z2b',
})
add('info', 'STR-35', frame(1, '#info', { name: 'OutdatedCursor', message: 'Cursor is older than the retention window' }), {
  kind: 'info', name: 'OutdatedCursor', message: 'Cursor is older than the retention window',
})
add('error frame', 'STR-10', frame(-1, undefined, { error: 'FutureCursor', message: 'Cursor is in the future' }), {
  kind: 'error', error: 'FutureCursor', message: 'Cursor is in the future',
})
add('error frame without an error name', 'STR-10', frame(-1, undefined, { message: 'Something broke' }), { kind: 'error', error: 'Unknown' })
add('handle event, removed upstream', 'STR-11', frame(1, '#handle', { did: 'did:plc:handle123', handle: 'newhandle.bsky.social', seq: 500, time }), unknownType(500))
add('tombstone event, removed upstream', 'STR-11', frame(1, '#tombstone', { did: 'did:plc:handle123', seq: 501, time }), unknownType(501))
add('unknown message type', 'STR-11', frame(1, '#unknownFutureType', { foo: 'bar', seq: 1 }), unknownType(1))
add('unknown op', 'STR-11', frame(2, '#commit', commit({ seq: 43 })), malformed(43))
add('empty type', 'STR-11', frame(1, '', { seq: 44 }), malformed(44))
add('empty frame', 'STR-11', new Uint8Array(), malformed())
add('not CBOR', 'STR-11', new Uint8Array([0xff, 0xff]), malformed())
// {"junk": [[[… 100,000 levels …]]]}: a relay-supplied frame that once crashed the process.
add('deeply nested body', 'STR-11', Buffer.concat([header(1, '#identity'),
  Buffer.from([0xa1, 0x64, ...Buffer.from('junk'), ...new Array(100_000).fill(0x81), 0x01])]), malformed())
// Tag 42 around a byte string without the 0x00 multibase prefix.
add('malformed CID link', 'STR-11', Buffer.concat([header(1, '#commit'),
  Buffer.from([0xa1, 0x66, ...Buffer.from('commit'), 0xd8, 0x2a, 0x42, 0x01, 0x71])]), malformed())

console.log(JSON.stringify({ generator: { '@atproto/lex-cbor': '0.1.7' }, cases }, null, 2))
