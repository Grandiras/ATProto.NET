// Writes identifier-cases.json and tid-generation.json: the identifier cases 0.8 pasted into its
// tests (IdentifierLineBreakTests, DidTests, NsidTests, RecordKeyTests, TidTests, HandleTests,
// TidGeneratorTests), expanded into one row per input.
// `node gen-identifier-cases.mjs` (Node 22, no packages) rewrites both files next to it.
import { writeFileSync } from 'node:fs'
import { json } from '../gen-lib.mjs'

const cases = []
const add = (kind, input, valid, note, normalized) =>
  cases.push({ kind, input, valid, ...(valid && normalized !== undefined ? { normalized } : {}), note })

// SYN-2: a line break anywhere refuses the value (0.8 IdentifierLineBreakTests).
const lineBreakBases = [
  ['Did', 'did:plc:z72i7hdynmk6r22z27h6tvur'],
  ['Did', 'did:web:example.com'],
  ['Handle', 'alice.bsky.social'],
  ['Nsid', 'app.bsky.feed.post'],
  ['RecordKey', 'self'],
  ['Tid', '3jzfcijpj2z2a'],
  ['AtIdentifier', 'alice.bsky.social'],
  ['AtIdentifier', 'did:plc:z72i7hdynmk6r22z27h6tvur'],
  ['AtUri', 'at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3jzfcijpj2z2a'],
  ['Cid', 'bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm'],
  ['Datetime', '1985-04-12T23:20:50.123Z'],
]
for (const [kind, value] of lineBreakBases) {
  add(kind, value, true, 'line-break base value')
  const middle = value.length >> 1
  for (const [variant, input] of [
    ['trailing LF', value + '\n'],
    ['trailing CRLF', value + '\r\n'],
    ['trailing CR', value + '\r'],
    ['leading LF', '\n' + value],
    ['inner LF', value.slice(0, middle) + '\n' + value.slice(middle)],
  ]) add(kind, input, false, `SYN-2 ${variant}`)
}

// SYN-11, SYN-12: DIDs (0.8 DidTests and the length limits of IdentifierSyntaxTests).
add('Did', 'did:method:val%', false, 'SYN-12 ends in %')
add('Did', 'did:web:example.com%3A8080', true, 'SYN-12 percent escape inside')
add('Did', 'did:a:b', true, 'SYN-11 minimum length 7')
add('Did', 'did:plc:' + 'a'.repeat(2040), true, 'SYN-11 2,048 characters')
add('Did', 'did:plc:' + 'a'.repeat(2041), false, 'SYN-11 2,049 characters')
add('Did', 'did:plc:abc:', false, 'SYN-11 ends in :')
add('Did', 'did:PLC:abc', false, 'SYN-11 upper-case method')

// SYN-15, SYN-16: handles.
const label63 = (c) => c.repeat(63)
add('Handle', [label63('a'), label63('b'), label63('c'), 'd'.repeat(61)].join('.'), true, 'SYN-15 253 characters',
  [label63('a'), label63('b'), label63('c'), 'd'.repeat(61)].join('.'))
add('Handle', [label63('a'), label63('b'), label63('c'), 'd'.repeat(62)].join('.'), false, 'SYN-15 254 characters')
add('Handle', label63('a') + '.com', true, 'SYN-15 63-character label', label63('a') + '.com')
add('Handle', 'a'.repeat(64) + '.com', false, 'SYN-15 64-character label')
add('Handle', 'Alice.Bsky.Social', true, 'SYN-16 lower-cased', 'alice.bsky.social')
add('Handle', '@alice.bsky.social', true, 'SYN-16 one leading @ stripped', 'alice.bsky.social')
add('Handle', '@@alice.bsky.social', false, 'SYN-16 exactly one @ stripped')
add('Handle', 'xn--ls8h.test', true, 'SYN-18 punycode, reserved TLD', 'xn--ls8h.test')
add('Handle', 'laptop.local', true, 'SYN-18 special-use TLD is syntactically valid', 'laptop.local')

// SYN-21, SYN-22: NSIDs (0.8 NsidTests).
add('Nsid', 'com.example.' + 'o'.repeat(63), true, 'SYN-22 63-character name')
add('Nsid', 'com.example.' + 'o'.repeat(64), false, 'SYN-22 64-character name')
add('Nsid', 'com.example.1abc', false, 'SYN-22 name starts with a digit')
const nsid317 = [label63('a'), label63('b'), label63('c'), label63('d'), 'e'.repeat(61)].join('.')
add('Nsid', nsid317, true, 'SYN-21 317 characters')
add('Nsid', nsid317 + 'e', false, 'SYN-21 318 characters')
add('Nsid', 'com.example', false, 'SYN-21 two segments')

// SYN-25: record keys (0.8 RecordKeyTests).
for (const value of ['@handle', 'any+space', 'number(3)', 'dHJ1ZQ==', 'a$b', 'a/b', 'a b', 'café', '.', '..', ''])
  add('RecordKey', value, false, 'SYN-25 outside the allowed set')
for (const value of ['~1.2-3_', 'pre:fix', '...', 'self', 'a'.repeat(512)])
  add('RecordKey', value, true, 'SYN-25 allowed')
add('RecordKey', 'a'.repeat(513), false, 'SYN-25 513 characters')

// SYN-27, SYN-29: TIDs (0.8 TidTests).
add('Tid', 'jzzzzzzzzzzzz', true, 'SYN-27 largest TID, high bit clear')
add('Tid', 'kjzfcijpj2z2a', false, 'SYN-27 first character sets the high bit')
add('Tid', '2222222222222', true, 'SYN-29 TID zero')
add('Tid', '3jzfcijpj2z2', false, 'SYN-27 12 characters')
add('Tid', '3jzfcijpj2z2aa', false, 'SYN-27 14 characters')
add('Tid', '3JZFCIJPJ2Z2A', false, 'SYN-27 upper case')
add('Tid', '3jzfcijpj2z1a', false, 'SYN-27 digit outside the alphabet')

writeFileSync(new URL('identifier-cases.json', import.meta.url), json(cases))

// SYN-30..SYN-34: a generator over a scripted clock (0.8 TidGeneratorTests). TID value =
// microseconds << 10 | clock id, the timestamp being max(now, last + 1), never below 0.
const SORTABLE = '234567abcdefghijklmnopqrstuvwxyz'
const tid = (micros, clockId) => {
  let value = (BigInt(micros) << 10n) | BigInt(clockId), text = ''
  for (let i = 0; i < 13; i++) { text = SORTABLE[Number(value & 31n)] + text; value >>= 5n }
  return text
}
const run = (clockId, clockMicros) => {
  let last = -1
  return clockMicros.map((now) => tid(last = Math.max(Math.max(0, now), last + 1), clockId))
}
const generation = [
  { name: 'spec example', note: 'SYN-34: 2023-06-30T15:03:01.887007Z, clock id 6', clockId: 6, clockMicros: [1688137381887007] },
  { name: 'clock steps backwards', note: 'SYN-31', clockId: 0, clockMicros: [1790251200000000, 1790251195000000] },
  { name: 'frozen clock', note: 'SYN-31', clockId: 513, clockMicros: [1790251200000000, 1790251200000000, 1790251200000000] },
  { name: 'largest clock id', note: 'SYN-32', clockId: 1023, clockMicros: [1790251200000123] },
  { name: 'clock before the epoch', note: 'SYN-33: counts as the epoch', clockId: 1, clockMicros: [-5000000, -1] },
].map((c) => ({ ...c, tids: run(c.clockId, c.clockMicros) }))
if (generation[0].tids[0] !== '3jzfcijpj2z2a') throw new Error('TID encoding disagrees with the spec example')

writeFileSync(new URL('tid-generation.json', import.meta.url), json(generation))
