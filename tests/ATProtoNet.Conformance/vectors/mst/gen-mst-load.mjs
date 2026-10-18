// Writes mst-load.json (loading an untrusted MST from its blocks) and ../hostile/mst.json (the MST
// items of the regression corpus): hand-built trees that break one rule each, from 0.8
// MerkleSearchTreeTests, MstNodeDataTests and RecordProofTests, plus the well-formed trees they
// are variations of. `node gen-mst-load.mjs` (Node 22, no packages) rewrites both files.
import { writeFileSync } from 'node:fs'
import { DAG_CBOR, Entries, Link, Raw, cid, cidString, encode, fromHex, hex, json, sha256, utf8 } from '../gen-lib.mjs'

// A key's layer: leading zero 2-bit pairs of SHA-256(key) (REP-4).
const height = (key) => {
  let pairs = 0
  for (const byte of sha256(utf8(key))) {
    if (byte === 0) { pairs += 4; continue }
    return pairs + (byte < 0x04 ? 3 : byte < 0x10 ? 2 : byte < 0x40 ? 1 : 0)
  }
  return pairs
}
const value = (name) => cid(utf8(name), DAG_CBOR)
const keyAt = (layer, stem) => {
  for (let i = 0; ; i++) if (height(`${stem}${i}`) === layer) return `${stem}${i}`
}

// A node {e: [{k, p, t, v}], l}; entries [{key, value, tree?, p?, k?}] are prefix-compressed
// unless p or k is given. omitNulls leaves absent links out instead of writing null.
const node = (left, entries, { omitNulls = false } = {}) => {
  let previous = new Uint8Array()
  const e = entries.map((entry) => {
    const key = entry.key instanceof Uint8Array ? entry.key : utf8(entry.key)
    let shared = 0
    while (shared < previous.length && shared < key.length && previous[shared] === key[shared]) shared++
    previous = key
    const pairs = [['k', entry.k ?? key.slice(shared)], ['p', entry.p ?? shared]]
    if (entry.tree || !omitNulls) pairs.push(['t', entry.tree ? new Link(entry.tree) : null])
    pairs.push(['v', new Link(entry.value)])
    return new Entries(pairs)
  })
  const pairs = [['e', e]]
  if (left || !omitNulls) pairs.push(['l', left ? new Link(left) : null])
  return encode(new Entries(pairs))
}

const rows = [], hostile = []
// A tree: blocks are node bytes, the first is the root. entries null = rejected.
const tree = (target, name, note, blocks, entries, root = cid(blocks[0])) => target.push({
  name, note, root: cidString(root),
  blocks: Object.fromEntries(blocks.map((b) => [cidString(cid(b)), hex(b)])),
  ...(entries ? { entries: entries.map(([k, v]) => [k, cidString(v)]) } : { rejected: true }),
})

const a = keyAt(0, 'com.example.record/a'), b = keyAt(0, 'com.example.record/b'), m = keyAt(0, 'com.example.record/m')
const z = keyAt(0, 'com.example.record/z'), top = keyAt(1, 'com.example.record/n')
const leaf = (k) => ({ key: k, value: value(k) })

tree(rows, 'empty tree', 'REP-6', [fromHex('a2616580616cf6')], [])
tree(rows, 'one entry', 'REP-5', [node(null, [leaf(a)])], [[a, value(a)]])
tree(rows, 'two layers', 'REP-5, REP-13: a layer-1 key over a layer-0 subtree', [node(cid(node(null, [leaf(a)])), [leaf(top)]), node(null, [leaf(a)])], [[a, value(a)], [top, value(top)]])
const first = keyAt(0, 'com.example.record/abc'), extended = (() => { for (let i = 0; ; i++) if (height(`${first}d${i}`) === 0) return `${first}d${i}` })()
tree(rows, 'prefix equal to the previous key', 'REP-11: valid', [node(null, [leaf(first), { ...leaf(extended), p: first.length }])], [[first, value(first)], [extended, value(extended)]])
tree(rows, 'prefix longer than the previous key', 'REP-11', [node(null, [leaf(first), { ...leaf(extended), p: first.length + 1, k: utf8('d') }])], null)
for (const [label, key] of [['non-ASCII byte', new Uint8Array([...utf8('com.example.record/caf'), 0xe9])], ['no rkey', utf8('com.example.record')], ['a space', utf8('com.example.record/a b')], ['invalid UTF-8', new Uint8Array([...utf8('com.example.record/'), 0xc3, 0x28])]])
  tree(rows, `invalid key: ${label}`, 'REP-12', [node(null, [{ key, value: value(label) }])], null)
tree(rows, 'keys out of order', 'REP-12', [node(null, [leaf(b), { ...leaf(a), p: 0, k: utf8(a) }])], null)
const outside = node(null, [leaf(z)])
tree(rows, 'subtree key outside its range', 'REP-12: the left subtree holds a key after the first entry', [node(cid(outside), [leaf(m)]), outside], null)
const shared = node(null, [leaf(a)])
tree(rows, 'subtree linked twice', 'REP-14', [node(cid(shared), [{ ...leaf(top), tree: cid(shared) }]), shared], null)
const empty = node(null, [])
tree(rows, 'empty inner node', 'REP-14', [node(cid(empty), [leaf(top)]), empty], null)
tree(rows, 'root with only a link', 'REP-14', [node(cid(node(null, [leaf(a)])), []), node(null, [leaf(a)])], null)
tree(rows, 'node mixing layers', 'REP-13', [node(null, [leaf(a), leaf(top)].sort((x, y) => (x.key < y.key ? -1 : 1)))], null)
tree(rows, 'child on its parent layer', 'REP-13: a layer-0 key under a layer-0 key', [node(cid(node(null, [leaf(a)])), [leaf(m)]), node(null, [leaf(a)])], null)
tree(rows, 'missing block', 'REP-16', [node(cid(node(null, [leaf(a)])), [leaf(top)])], null)
for (const [label, h] of [['empty', ''], ['break', 'ff'], ['an array', '80'], ['e not an array', 'a1616501'], ['l not a link', 'a2616580616c01'],
  ['entry with neither k nor v', 'a2616581a0616cf6'], ['entry without v', 'a1616581a1616b4161'], ['v not a link', 'a1616581a2616b416161760a'],
  ['truncated', 'a161658161'], ['indefinite-length map', 'bf6165806161f6ff'], ['a float', 'fb400921fb54442d18']])
  rows.push({ name: `malformed node: ${label}`, note: 'REP-10', root: cidString(cid(fromHex(h))), blocks: { [cidString(cid(fromHex(h)))]: h }, rejected: true })

// Hostile: prefix lengths that once drove allocation (indigo SEC-4, REP-11), a chain deeper than any
// real tree (REP-14), and the omitted-null-links encoding that is read but not canonical (REP-7).
for (const [label, p] of [['100', 100], ['-1', -1], ['2^31 - 1', 2 ** 31 - 1], ['2^40', 2n ** 40n], ['-2^63', -(2n ** 63n)]])
  tree(hostile, `first entry prefix ${label}`, 'REP-11: rejected before it is used to slice or allocate', [node(null, [{ ...leaf(a), p }])], null)
tree(hostile, 'first entry prefix 2^64 - 1', 'REP-11: fits no signed integer', [node(null, [{ ...leaf(a), p: 2n ** 64n - 1n }])], null)
tree(hostile, 'prefix past the previous key', 'REP-11', [node(null, [leaf(first), { ...leaf(extended), p: 2 ** 31 - 1, k: utf8('d') }])], null)
const chain = [node(null, [leaf(a)])]
for (let i = 0; i < 1000; i++) chain.unshift(node(cid(chain[0]), []))
tree(hostile, 'chain of 1000 wrapper nodes', 'REP-14: depth is capped, the walk does not recurse 1000 levels', chain, null)
tree(hostile, 'omitted null links', 'REP-7, REP-15: read, but not the canonical encoding', [node(null, [leaf(a)], { omitNulls: true })], null)
tree(hostile, 'canonical empty node', 'REP-6: a2616580616cf6', [fromHex('a2616580616cf6')], [])

writeFileSync(new URL('mst-load.json', import.meta.url), json(rows))
writeFileSync(new URL('../hostile/mst.json', import.meta.url), json(hostile))
