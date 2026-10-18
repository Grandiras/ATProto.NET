// Writes the hostile-input vectors for DRISL and CAR reading (the "Repository and data model" items
// of the regression corpus in design/04-testing.md): drisl.json (lenient reading), drisl-strict.json
// (strict reading) and car.json. Large inputs are written as segments, a hex string or
// {"repeat": hex, "count": n}, concatenated in order. CAR rows have the shape of cid-car/car-read.json.
// `node gen-hostile.mjs` (Node 22, no packages) rewrites the three files next to it.
import { writeFileSync } from 'node:fs'
import { DAG_CBOR, RAW, cid, cidString, concat, encode, fromHex, hex, json, rejections, sha256, utf8, varint } from '../gen-lib.mjs'

const repeat = (h, count) => ({ repeat: h, count })
const nested = (depth, inner = '01') => [repeat('81', depth), inner]
const arrays = (depth, value) => { for (let i = 0; i < depth; i++) value = [value]; return value }
const emptyMap = cid(fromHex('a0'), DAG_CBOR)
const link = hex(concat([0xd8, 0x2a, 0x58, 0x25, 0x00], emptyMap))

const lenient = [], strict = []
// Deep expected values are given as compact JSON text (outputText).
const both = (name, note, input, output) => {
  const expected = output === null || typeof output !== 'object' ? { output } : { outputText: JSON.stringify(output) }
  lenient.push({ name, note, input, ...expected })
  strict.push({ name, note, input, ...expected })
}
both('nesting at the limit', 'DAT-30: 64 levels', nested(64), arrays(64, 1))
both('nesting past the limit', 'DAT-30: 65 levels', nested(65), null)
both('100k nested arrays', 'DAT-32: rejected, not a stack overflow', nested(100000), null)
both('100k nested maps', 'DAT-32: {"a": {"a": ...}}', [repeat('a16161', 100000), '01'], null)
both('link wrapper inside the limit', 'DAT-30: a link at array depth 63', nested(63, link), arrays(63, { $link: cidString(emptyMap) }))
both('link wrapper past the limit', 'DAT-30: the {"$link"} wrapper is the 65th level', nested(64, link), null)
both('huge declared text length', 'DAT-33: 2^32 bytes declared, 1 present', ['7b0000000100000000', '61'], null)
both('huge declared array length', 'DAT-33: 2^63 - 1 items declared', ['9b7fffffffffffffff', '01'], null)
both('huge declared map length', 'DAT-33: 2^31 entries declared', ['ba80000000', '616101'], null)
lenient.push({ name: '100k unknown tags', note: 'DAT-26: skipped iteratively', input: [repeat('c1', 100000), '07'], output: 7 })
strict.push({ name: '100k unknown tags', note: 'DAT-23', input: [repeat('c1', 100000), '07'], output: null })

const header = (h) => [hex(varint(h.length)), hex(h)]
const emptyHeader = encode({ roots: [], version: 1 })
const junkHeader = (depth) => {
  // {"junk!": [[…[0]…]], "roots": [], "version": 1}: canonical order, the unknown key first.
  const prefix = concat([0xa3, 0x65], utf8('junk!'))
  const suffix = concat([0x00, 0x65], utf8('roots'), [0x80, 0x67], utf8('version'), [0x01])
  const length = prefix.length + depth + suffix.length
  return [hex(varint(length)), hex(prefix), repeat('81', depth), hex(suffix)]
}
const v0data = utf8('cidv0')
const v0 = concat([0x12, 0x20], sha256(v0data))
const cars = [
  { name: 'CIDv0 block', note: 'DAT-12: a bare multihash implies dag-pb', car: [...header(emptyHeader), hex(varint(v0.length + v0data.length)), hex(v0), hex(v0data)], rejected: true },
  { name: 'header value nested to the limit', note: 'DAT-31: 64 levels, the header map included', car: junkHeader(63), roots: [], blocks: [] },
  { name: 'header value nested past the limit', note: 'DAT-31: rejected by the SDK limit, not a library default', car: junkHeader(64), rejected: true },
  { name: 'header value nested 100k levels', note: 'DAT-31, DAT-32', car: junkHeader(100000), rejected: true },
  { name: 'truncated deeply nested header', note: 'DAT-55', car: [hex(varint(7 + 100000)), 'a1656a756e6b21', repeat('81', 100000)], rejected: true },
]
for (const [label, length] of [['2^31', 2n ** 31n], ['2^32', 2n ** 32n], ['2^63 - 1', 2n ** 63n - 1n]]) {
  cars.push({ name: `header length ${label}`, note: 'DAT-33: a declared length over 2 GiB is not trusted or cast', car: [hex(varint(length)), hex(emptyHeader)], rejected: true })
  cars.push({ name: `block length ${label}`, note: 'DAT-33, DAT-57', car: [...header(emptyHeader), hex(varint(length)), hex(cid(utf8('x'), RAW))], rejected: true })
}

writeFileSync(new URL('drisl.json', import.meta.url), json(rejections(lenient)))
writeFileSync(new URL('drisl-strict.json', import.meta.url), json(rejections(strict)))
writeFileSync(new URL('car.json', import.meta.url), json(cars))
