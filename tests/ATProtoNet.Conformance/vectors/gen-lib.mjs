// Shared helpers for the dependency-free generators under vectors/: a canonical DRISL writer that
// can also emit deliberately broken encodings, CIDs, varints and hex. Node 22, no packages.
import { createHash } from 'node:crypto'

export const sha256 = (bytes) => new Uint8Array(createHash('sha256').update(bytes).digest())
export const hex = (bytes) => Buffer.from(bytes).toString('hex')
export const fromHex = (s) => new Uint8Array(Buffer.from(s, 'hex'))
export const concat = (...parts) => new Uint8Array(Buffer.concat(parts.map((p) => Buffer.from(p))))
export const utf8 = (s) => new Uint8Array(Buffer.from(s, 'utf8'))

export const DAG_CBOR = 0x71
export const RAW = 0x55

/** The 36-byte binary CIDv1 (sha2-256) of data. */
export const cid = (data, codec = DAG_CBOR) => concat([0x01, codec, 0x12, 0x20], sha256(data))

const ALPHABET = 'abcdefghijklmnopqrstuvwxyz234567'

/** The multibase base32 text form of a binary CID. */
export const cidString = (bin) => {
  let bits = 0, value = 0, out = 'b'
  for (const byte of bin) {
    value = (value << 8) | byte
    bits += 8
    while (bits >= 5) { out += ALPHABET[(value >>> (bits - 5)) & 31]; bits -= 5 }
  }
  if (bits > 0) out += ALPHABET[(value << (5 - bits)) & 31]
  return out
}

export const cidFromString = (s) => {
  let bits = 0, value = 0
  const out = []
  for (const c of s.slice(1)) {
    value = (value << 5) | ALPHABET.indexOf(c)
    bits += 5
    if (bits >= 8) { out.push((value >>> (bits - 8)) & 255); bits -= 8 }
  }
  return new Uint8Array(out)
}

/** Unsigned LEB128. */
export const varint = (n) => {
  let v = BigInt(n)
  const out = []
  while (v >= 0x80n) { out.push(Number((v & 0x7fn) | 0x80n)); v >>= 7n }
  out.push(Number(v))
  return new Uint8Array(out)
}

/** A CID link value for the writer. */
export class Link { constructor(bin) { this.bin = bin } }

/** Bytes written verbatim, for encodings the writer would refuse. */
export class Raw { constructor(bytes) { this.bytes = bytes } }

/** A map written in the given entry order, unsorted (duplicates allowed). */
export class Entries { constructor(pairs) { this.pairs = pairs } }

const head = (major, n) => {
  const v = BigInt(n)
  const m = major << 5
  if (v < 24n) return [m | Number(v)]
  if (v < 0x100n) return [m | 24, Number(v)]
  if (v < 0x10000n) return [m | 25, Number(v >> 8n), Number(v & 255n)]
  if (v < 0x100000000n) return [m | 26, ...[24n, 16n, 8n, 0n].map((s) => Number((v >> s) & 255n))]
  return [m | 27, ...[56n, 48n, 40n, 32n, 24n, 16n, 8n, 0n].map((s) => Number((v >> s) & 255n))]
}

const compareKeys = (a, b) => {
  const x = utf8(a), y = utf8(b)
  return x.length - y.length || Buffer.compare(Buffer.from(x), Buffer.from(y))
}

/** Canonical DRISL of a JS value (BigInt or safe integer numbers, Uint8Array, Link, Raw, Entries). */
export const encode = (value) => {
  const out = []
  const write = (v) => {
    if (v === null) return out.push(0xf6)
    if (v === true) return out.push(0xf5)
    if (v === false) return out.push(0xf4)
    if (typeof v === 'number' || typeof v === 'bigint') {
      const n = BigInt(v)
      return out.push(...(n >= 0n ? head(0, n) : head(1, -1n - n)))
    }
    if (typeof v === 'string') { const b = utf8(v); out.push(...head(3, b.length)); return out.push(...b) }
    if (v instanceof Uint8Array) { out.push(...head(2, v.length)); return out.push(...v) }
    if (v instanceof Raw) return out.push(...v.bytes)
    if (v instanceof Link) { out.push(0xd8, 0x2a); return write(concat([0x00], v.bin)) }
    if (Array.isArray(v)) { out.push(...head(4, v.length)); return v.forEach(write) }
    const pairs = v instanceof Entries ? v.pairs : Object.entries(v).sort(([a], [b]) => compareKeys(a, b))
    out.push(...head(5, pairs.length))
    for (const [k, x] of pairs) { write(k); write(x) }
  }
  write(value)
  return new Uint8Array(out)
}

/** A CARv1 file: header {roots, version: 1} (or the given header bytes) and blocks [cidBin, data]. */
export const car = (roots, blocks, header = encode({ roots: roots.map((r) => new Link(r)), version: 1 })) =>
  concat(varint(header.length), header, ...blocks.flatMap(([c, d]) => [varint(c.length + d.length), c, d]))

/** Minimal CARv1 reader for generator input: { roots, blocks: [[cidBin, data]] }. */
export const readCar = (bytes) => {
  let at = 0
  const uvarint = () => {
    let v = 0, shift = 0, b
    do { b = bytes[at++]; v += (b & 0x7f) * 2 ** shift; shift += 7 } while (b & 0x80)
    return v
  }
  const headerLength = uvarint()
  const header = bytes.slice(at, at + headerLength)
  at += headerLength
  const blocks = []
  while (at < bytes.length) {
    const length = uvarint()
    blocks.push([bytes.slice(at, at + 36), bytes.slice(at + 36, at + length)])
    at += length
  }
  return { header, blocks }
}

/** Writes a vector file as indented JSON with a trailing newline. */
export const json = (value) => JSON.stringify(value, null, 2) + '\n'

/** Marks the rows whose output is null as rejected, dropping the null. */
export const rejections = (rows) => rows.map(({ output, ...row }) => output === null ? { ...row, rejected: true } : { ...row, output })

/** Minimal DRISL reader for generator input (canonical data only): maps keep their order as Entries. */
export const decode = (bytes) => {
  let at = 0
  const uint = (info) => {
    if (info < 24) return BigInt(info)
    const size = 1 << (info - 24)
    let v = 0n
    for (let i = 0; i < size; i++) v = (v << 8n) | BigInt(bytes[at++])
    return v
  }
  const read = () => {
    const first = bytes[at++], major = first >> 5, info = first & 31
    if (major === 7) return info === 20 ? false : info === 21 ? true : null
    const n = uint(info)
    const num = Number(n)
    switch (major) {
      case 0: return n <= BigInt(Number.MAX_SAFE_INTEGER) ? num : n
      case 1: return -1 - num
      case 2: return bytes.slice(at, (at += num))
      case 3: return Buffer.from(bytes.slice(at, (at += num))).toString('utf8')
      case 4: return Array.from({ length: num }, read)
      case 5: return new Entries(Array.from({ length: num }, () => [read(), read()]))
      default: return new Link(read().slice(1)) // tag 42
    }
  }
  return read()
}
