// Writes cid.json (CID computation), car-read.json and car-write.json (CARv1): the cases 0.8 pasted
// into CidTests, CidComputationTests, CarReaderTests and CarWriterTests, plus the CAR framing rules
// of design/specs/data-model.md (DAT-55..DAT-62).
// `node gen-car.mjs` (Node 22, no packages) rewrites the three files next to it.
import { writeFileSync } from 'node:fs'
import { DAG_CBOR, Entries, RAW, car, cid, cidString, concat, encode, fromHex, hex, json, utf8, varint } from '../gen-lib.mjs'

const cids = [
  ['empty raw', RAW, '', 'bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku'],
  ['empty MST node', DAG_CBOR, 'a2616580616cf6', 'bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm'],
  ['hello raw', RAW, hex(utf8('Hello'))],
  ['hello dag-cbor', DAG_CBOR, hex(utf8('Hello'))],
  ['map a=1', DAG_CBOR, 'a1616101'],
  ['map a=2', DAG_CBOR, 'a1616102'],
].map(([name, codec, data, expected]) => {
  const computed = cidString(cid(fromHex(data), codec))
  if (expected && computed !== expected) throw new Error(`${name}: ${computed} is not ${expected}`)
  return { name, codec: codec === RAW ? 'raw' : 'dag-cbor', data, cid: computed }
})
writeFileSync(new URL('cid.json', import.meta.url), json(cids))

const block = (data, codec = RAW) => [cid(data, codec), data]
const view = ([c, d]) => [cidString(c), hex(d)]
const reads = []
const valid = (name, note, roots, blocks, { verifyBlockCids = true, bytes = car(roots, blocks) } = {}) =>
  reads.push({ name, note, car: hex(bytes), verifyBlockCids, roots: roots.map(cidString), blocks: blocks.map(view) })
const invalid = (name, note, bytes, verifyBlockCids = false) =>
  reads.push({ name, note, car: hex(bytes), ...(verifyBlockCids ? { verifyBlockCids } : {}), rejected: true })

const hello = block(utf8('Hello CAR'))
const rootBlock = block(utf8('root block'))
const one = block(utf8('block one')), two = block(utf8('block two'))
const big = block(Uint8Array.from({ length: 20000 }, (_, i) => (i * 131 + 7) & 255))
const emptyHeader = encode({ roots: [], version: 1 })
const framed = (header, ...sections) => concat(varint(header.length), header, ...sections.flatMap((s) => [varint(s.length), s]))

valid('minimal', 'one raw block, no roots', [], [hello])
valid('with root', 'a root naming the only block', [rootBlock[0]], [rootBlock])
valid('two blocks', 'blocks in file order', [], [one, two])
valid('duplicate CID', 'both occurrences kept in file order (DAT-61)', [one[0]], [one, [one[0], utf8('shadowed')]], { verifyBlockCids: false })
valid('three-byte length prefix', 'a 20,000-byte block (DAT-56)', [big[0]], [big])
valid('dag-cbor blocks', 'DRISL blocks that match their CIDs', [], [block(fromHex('a2616580616cf6'), DAG_CBOR), block(fromHex('a1616101'), DAG_CBOR)])
valid('unknown header key', 'skipped (DAT-55)', [], [hello], { bytes: car([], [hello], encode({ extra: 'x', roots: [], version: 1 })) })
valid('no blocks', 'a header and nothing else', [rootBlock[0]], [])
valid('block CID mismatch unverified', 'DAT-58: not checked unless asked', [], [[cid(utf8('other'), RAW), utf8('data')]], { verifyBlockCids: false })

invalid('empty', 'no header (DAT-55)', new Uint8Array())
invalid('zero header length', 'DAT-55', concat([0x00]))
invalid('zero block length', 'DAT-57', concat(varint(emptyHeader.length), emptyHeader, [0x00]))
invalid('header length non-minimal', 'DAT-56', concat([0x80 | emptyHeader.length, 0x00], emptyHeader))
invalid('header length truncated', 'DAT-56', concat([0x80]))
invalid('header length over nine bytes', 'DAT-56', concat(fromHex('ffffffffffffffffff01'), emptyHeader))
invalid('block length non-minimal', 'DAT-56', concat(varint(emptyHeader.length), emptyHeader, [0x80 | (hello[0].length + hello[1].length), 0x00], ...hello))
invalid('unsupported CID version', 'DAT-57', framed(emptyHeader, fromHex('02711201aabb')))
invalid('CID digest past the end of its block', 'DAT-57', framed(emptyHeader, fromHex('01711220aabbccdd')))
invalid('block longer than the file', 'DAT-57', concat(varint(emptyHeader.length), emptyHeader, varint(1000), cid(utf8('x'), RAW)))
invalid('block CID mismatch', 'DAT-58: verified', car([], [[cid(utf8('other'), RAW), utf8('data')]]), true)
const headers = [
  ['not a map', '8101'],
  ['no version', 'a165726f6f747380'],
  ['version not an integer', 'a26776657273696f6e6131' + '65726f6f747380'],
  ['version out of range', 'a26776657273696f6e1b0000000100000000' + '65726f6f747380'],
  ['roots not an array', 'a26776657273696f6e0165726f6f747301'],
  ['root not tagged', 'a26776657273696f6e0165726f6f747381420001'],
  ['root missing the 0x00 prefix', 'a26776657273696f6e0165726f6f747381d82a420101'],
  ['non-string key', 'a201016776657273696f6e01'],
  ['trailing bytes', 'a26776657273696f6e0165726f6f74738000'],
  ['truncated', 'a26776657273696f6e01'],
  ['version 2', hex(encode({ roots: [], version: 2 }))],
  ['keys out of canonical order', hex(encode(new Entries([['version', 1], ['roots', []]])))],
]
for (const [name, h] of headers) invalid(`header ${name}`, 'DAT-55', framed(fromHex(h)))

const writes = [
  ['single root and blocks', [rootBlock[0]], [rootBlock, one]],
  ['no roots', [], [one, two]],
  ['no blocks', [rootBlock[0]], []],
  ['two roots', [one[0], two[0]], [one, two]],
].map(([name, roots, blocks]) => ({ name, note: 'DAT-62', roots: roots.map(cidString), blocks: blocks.map(view), car: hex(car(roots, blocks)) }))

writeFileSync(new URL('car-read.json', import.meta.url), json(reads))
writeFileSync(new URL('car-write.json', import.meta.url), json(writes))
