// Writes signature-scalar-range.json: the valid signatures of signature-fixtures.json with r or s
// replaced by a scalar outside [1, n - 1] (0, the curve order n, 2^256 - 1), and cut to the wrong
// length. Every one must verify as false without throwing (0.8 SignatureScalarRangeTests, CRY-13,
// CRY-14). `node gen-signature-scalar-range.mjs` (Node 22, no packages) rewrites it.
import { readFileSync, writeFileSync } from 'node:fs'
import { json } from '../gen-lib.mjs'

const ORDERS = {
  ES256: 'ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551',
  ES256K: 'fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141',
}
const unpadded = (bytes) => Buffer.from(bytes).toString('base64').replace(/=+$/, '')
const fixtures = JSON.parse(readFileSync(new URL('signature-fixtures.json', import.meta.url)))

const cases = []
for (const f of fixtures.filter((f) => f.validSignature)) {
  const signature = Buffer.from(f.signatureBase64, 'base64')
  const curve = f.algorithm === 'ES256' ? 'P-256' : 'K-256'
  const add = (name, bytes) => cases.push({
    name: `${curve} ${name}`, algorithm: f.algorithm, publicKeyDid: f.publicKeyDid,
    messageBase64: f.messageBase64, signatureBase64: unpadded(bytes), validSignature: false,
  })
  for (const [index, part] of [[0, 'r'], [1, 's']])
    for (const [label, scalar] of [['zero', '00'.repeat(32)], ['the order', ORDERS[f.algorithm]], ['2^256 - 1', 'ff'.repeat(32)]]) {
      const bytes = Buffer.from(signature)
      Buffer.from(scalar, 'hex').copy(bytes, index * 32)
      add(`${part} is ${label}`, bytes)
    }
  add('63 bytes', signature.subarray(0, 63))
  add('65 bytes', Buffer.concat([signature, Buffer.from([0])]))
  add('empty', Buffer.alloc(0))
}

writeFileSync(new URL('signature-scalar-range.json', import.meta.url), json(cases))
