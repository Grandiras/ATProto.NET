// Writes drisl-encode.json (JSON data model -> DRISL), drisl-decode.json (DRISL -> JSON, lenient
// reading) and drisl-strict.json (DRISL -> JSON, strict reading): the cases 0.8 pasted into
// DagCborEncoderTests, DagCborDecoderTests, DagCborRoundtripTests and DagCborInteropTests, plus the
// strict-reading rules of design/specs/data-model.md (DAT-20..DAT-26).
// `node gen-drisl.mjs` (Node 22, no packages) rewrites the three files next to it.
import { readFileSync, writeFileSync } from 'node:fs'
import { Entries, Link, Raw, cid, cidFromString, cidString, concat, encode, fromHex, hex, json, rejections, utf8, RAW } from '../gen-lib.mjs'

// The JSON data model as the generator's values: {"$link"} and {"$bytes"} objects become links
// and byte strings.
const fromJson = (v) => {
  if (Array.isArray(v)) return v.map(fromJson)
  if (v === null || typeof v !== 'object') return v
  const keys = Object.keys(v)
  if (keys.length === 1 && keys[0] === '$link') return new Link(cidFromString(v.$link))
  if (keys.length === 1 && keys[0] === '$bytes') return new Uint8Array(Buffer.from(v.$bytes, 'base64'))
  return Object.fromEntries(keys.map((k) => [k, fromJson(v[k])]))
}

const unpadded = (bytes) => Buffer.from(bytes).toString('base64').replace(/=+$/, '')
const encodeCases = [], decodeCases = [], strictCases = []

// Canonical values: they encode to exactly these bytes and decode back, in either mode.
const both = (name, value, note, extra = {}) => {
  const cbor = hex(encode(fromJson(value)))
  if (extra.cbor && extra.cbor !== cbor) throw new Error(`${name}: the pasted bytes ${extra.cbor} disagree with ${cbor}`)
  if (extra.cid && cidString(cid(fromHex(cbor))) !== extra.cid) throw new Error(`${name}: CID mismatch`)
  const [input, output] = value === null ? [{ inputText: 'null' }, { outputText: 'null' }] : [{ input: value }, { output: value }]
  encodeCases.push({ name, note, ...input, output: cbor, ...(extra.cid ? { cid: extra.cid } : {}) })
  decodeCases.push({ name, note, input: cbor, ...output })
  strictCases.push({ name, note, input: cbor, ...output })
}

const zeroDigestLink = cidString(concat([0x01, 0x71, 0x12, 0x20], new Uint8Array(32)))
both('map', { key: 'value' }, 'DagCborDecoderTests.Decode_Map_ReturnsObject')
both('link', { $link: zeroDigestLink }, 'tag 42 over 0x00 and a DRISL CID (DAT-42)')
both('raw link', { $link: 'bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku' }, 'a raw-codec CID')
both('bytes', { $bytes: 'SGVsbG8' }, 'unpadded standard base64 (DAT-46)')
for (const [h, b64] of [['', ''], ['66', 'Zg'], ['666f', 'Zm8'], ['666f6f', 'Zm9v'], ['fbff', '+/8']])
  both(`bytes rfc4648 ${h || 'empty'}`, { $bytes: b64 }, 'RFC 4648 vector, standard alphabet, unpadded (DAT-46)')
both('bytes 1000', { $bytes: unpadded(Uint8Array.from({ length: 1000 }, (_, i) => i & 255)) }, 'a long byte string')
both('string', 'hello', 'a top-level text string')
both('integer', 42, 'a small integer')
both('negative integer', -1, 'a negative integer')
both('true', true, 'boolean')
both('false', false, 'boolean')
both('null', null, 'null')
both('array', [1, 2, 3], 'an array')
both('empty map', {}, 'an empty map')
both('nested maps sorted', { outer: { z: 3, a: 1 }, inner: [true, null] }, 'every map sorted (DAT-40)')
both('type preserved', { $type: 'app.bsky.feed.post', text: 'hello' }, 'DagCborEncoderTests.Encode_TypeDiscriminator_Preserved')
both('length-first key order', { ab: 1, b: 2 }, 'shorter keys first (DAT-40)', { cbor: 'a261620262616201' })
both('supplementary versus private-use key', { '\u{10000}': 2, 'a': 1 },
  'equal-length keys by UTF-8 bytes, not UTF-16 (DAT-40)', { cbor: 'a264ee8080610164f090808002' })
both('roundtrip record', { text: 'hello', count: 42, active: true, value: null, items: [[1, 2], [3, 4]], empty: [], neg: -12345 },
  'DagCborRoundtripTests')
both('real network record', {
  $type: 'app.bsky.feed.post',
  createdAt: '2024-10-17T07:06:51.491Z',
  langs: ['en'],
  text: "\u{1F44B}  Bluesky is an open social network that gives creators independence from platforms, developers the freedom to build, and users a choice in their experience. We're so excited to have you here! \n\nWe share Bluesky updates & news from this account. A quick orientation thread: \u{1F9F5}✨",
}, 'the CID a live PDS published for it (DAT-44)', { cid: 'bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq' })

// The interop fixtures, read strictly (they are canonical).
JSON.parse(readFileSync(new URL('data-model-fixtures.json', import.meta.url))).forEach((f, i) =>
  strictCases.push({ name: `interop fixture ${i}`, note: 'data-model-fixtures.json', input: hex(Buffer.from(f.cbor_base64, 'base64')), output: f.json }))

// Integers at the Int64 bounds: JSON text, since JavaScript numbers lose them.
for (const [name, text, value] of [['int64 max', '9223372036854775807', 2n ** 63n - 1n], ['int64 min', '-9223372036854775808', -(2n ** 63n)]]) {
  const cbor = hex(encode(value))
  encodeCases.push({ name, note: 'DAT-2', inputText: text, output: cbor })
  decodeCases.push({ name, note: 'DAT-2', input: cbor, outputText: text })
  strictCases.push({ name, note: 'DAT-2', input: cbor, outputText: text })
}

// Encoding only.
encodeCases.push(
  { name: 'padded bytes', note: '$bytes is read with or without padding (DAT-46)', input: { $bytes: 'SGVsbG8=' }, output: '4548656c6c6f' },
  { name: 'float', note: 'DAT-1', inputText: '3.14', output: null },
  { name: 'float in a map', note: 'DAT-1', inputText: '{"a":123.456}', output: null },
  { name: 'exponent', note: 'DAT-2', inputText: '1e3', output: null },
  { name: 'beyond int64', note: 'DAT-2', inputText: '18446744073709551615', output: null },
  { name: 'below int64', note: 'DAT-2', inputText: '-9223372036854775809', output: null },
  { name: 'duplicate key', note: 'DAT-41', inputText: '{"a":1,"b":2,"a":3}', output: null },
  { name: 'link not a blessed CID', note: 'a dag-pb CID (DAT-10)', input: { $link: 'bafybeihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku' }, output: null },
  { name: 'nesting 64', note: 'DAT-48: 64 JSON levels', inputText: '['.repeat(64) + '1' + ']'.repeat(64), output: '81'.repeat(64) + '01' },
  { name: 'nesting 65', note: 'DAT-48: 65 JSON levels', inputText: '['.repeat(65) + '1' + ']'.repeat(65), output: null },
)

// Reading, in either mode: not DRISL at all, or outside the data model.
const rejectEverywhere = [
  ['link without the 0x00 prefix', 'd82a4101'],
  ['link that is only the prefix', 'd82a4100'],
  ['tag 42 around text', 'd82a6161'],
  ['link not a blessed CID', 'd82a5825000170122000' + '00'.repeat(32)],
  ['integer map key', 'a10101'],
  ['truncated text', '62'],
  ['truncated array', '82'],
  ['empty input', ''],
  ['unsigned beyond int64', '1bffffffffffffffff'],
  ['negative beyond int64', '3b8000000000000000'],
  ['undefined', 'f7'],
  ['lone break', 'ff'],
  ['simple value 16', 'f0'],
  ['half float', 'f93c00'],
  ['single float', 'fa3f800000'],
  ['double float', 'fb40091eb851eb851f'],
  ['float after a valid entry', 'a2616101616bfb0000000000000000'],
]
for (const [name, cbor] of rejectEverywhere) {
  decodeCases.push({ name, note: 'DAT-1, DAT-2, DAT-3, DAT-23, DAT-24, DAT-34', input: cbor, output: null })
  strictCases.push({ name, note: 'DAT-1, DAT-2, DAT-3, DAT-23, DAT-24, DAT-34', input: cbor, output: null })
}

// Non-canonical input: lenient reading accepts it (DAT-26), keeping the key order as encoded
// (DAT-45); strict reading rejects it (DAT-20..DAT-25).
const nonCanonical = [
  ['unsorted keys', hex(encode(new Entries([['b', 2], ['a', 1]]))), { b: 2, a: 1 }, 'DAT-22'],
  ['bytewise instead of length-first', hex(encode(new Entries([['ab', 1], ['b', 2]]))), { ab: 1, b: 2 }, 'DAT-22'],
  ['non-shortest integer', '1801', 1, 'DAT-21'],
  ['non-shortest text length', '780161', 'a', 'DAT-21'],
  ['non-shortest map length', 'b8016161' + '01', { a: 1 }, 'DAT-21'],
  ['non-shortest tag', 'd9002a' + hex(encode(fromHex('00' + hex(cidFromString(zeroDigestLink))))), { $link: zeroDigestLink }, 'DAT-21'],
  ['indefinite array', '9f01ff', [1], 'DAT-20'],
  ['indefinite map', 'bf616101ff', { a: 1 }, 'DAT-20'],
  ['indefinite text', '7f61616162ff', 'ab', 'DAT-20'],
  ['indefinite bytes', '5f41014102ff', { $bytes: 'AQI' }, 'DAT-20'],
  ['unknown tag', 'c107', 7, 'DAT-23, DAT-26: the wrapped value in its place'],
  ['invalid UTF-8', 'a1626bff6376c328', { 'k�': 'v�(' }, 'DAT-25, DAT-26: replaced with U+FFFD'],
]
for (const [name, cbor, value, note] of nonCanonical) {
  decodeCases.push({ name, note, input: cbor, output: value })
  strictCases.push({ name, note, input: cbor, output: null })
}

// Lenient reading still rejects duplicate keys (DAT-26); strict reading only accepts canonical bytes.
const duplicate = hex(encode(new Entries([['a', 1], ['a', 2]])))
decodeCases.push({ name: 'duplicate key', note: 'DAT-26', input: duplicate, output: null })
strictCases.push(
  { name: 'duplicate key', note: 'DAT-22', input: duplicate, output: null },
  { name: 'trailing bytes', note: 'DAT-25', input: '0100', output: null },
  { name: 'link with a byte after the CID', note: 'DAT-23', input: hex(encode(new Raw(concat([0xd8, 0x2a], encode(concat([0x00], cid(utf8('x'), RAW), [0x00])))))), output: null },
)

writeFileSync(new URL('drisl-encode.json', import.meta.url), json(rejections(encodeCases)))
writeFileSync(new URL('drisl-decode.json', import.meta.url), json(rejections(decodeCases)))
writeFileSync(new URL('drisl-strict.json', import.meta.url), json(rejections(strictCases)))
