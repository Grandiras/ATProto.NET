// Writes plc-genesis.json: a did:plc genesis operation built and signed by the reference
// @did-plc/lib 0.0.4 (with its @atproto/crypto 0.1.0, @ipld/dag-cbor 7.0.3 and multiformats 9.9.0)
// from fixed keys, a handle and a PDS endpoint. Install @did-plc/lib@0.0.4 in a scratch directory,
// copy this file there and run `node gen-plc-genesis.mjs > plc-genesis.json`. K-256 signing is
// RFC 6979 and low-S, so the output is reproducible byte for byte.
import { createECDH, createHash } from 'node:crypto'
import * as cbor from '@ipld/dag-cbor'
import { CID } from 'multiformats/cid'
import { sha256 } from 'multiformats/hashes/sha2'
import { Secp256k1Keypair, formatDidKey, P256_JWT_ALG } from '@atproto/crypto'
import * as plc from '@did-plc/lib'

const scalar = (label) => createHash('sha256').update(`ATProto.NET PLC genesis vector: ${label}`).digest()
const p256DidKey = (label) => {
  const ecdh = createECDH('prime256v1')
  ecdh.setPrivateKey(scalar(label))
  return formatDidKey(P256_JWT_ALG, ecdh.getPublicKey())
}
const hex = (bytes) => Buffer.from(bytes).toString('hex')

const rotation = await Secp256k1Keypair.import(scalar('rotation key'))
const recoveryKey = p256DidKey('recovery key')
const signingKey = p256DidKey('signing key')
const handle = 'alice.example.com'
const pds = 'https://pds.example.com'

const unsigned = plc.formatAtprotoOp({ signingKey, handle, pds, rotationKeys: [rotation.did(), recoveryKey], prev: null })
const signed = await plc.signOperation(unsigned, rotation)
const signedBytes = cbor.encode(signed)
const did = await plc.didForCreateOp(signed)

// The reference accepts its own operation, signed by the first rotation key.
await plc.assureValidCreationOp(did, signed)
if ((await plc.assureValidSig([rotation.did()], signed)) !== rotation.did()) throw new Error('signature')

console.log(JSON.stringify({
  source: '@did-plc/lib 0.0.4 formatAtprotoOp + signOperation, @atproto/crypto 0.1.0',
  rotationPrivateKeyHex: hex(scalar('rotation key')),
  rotationCurve: 'K256',
  rotationKeys: unsigned.rotationKeys,
  signingKey,
  handle,
  pds,
  unsignedHex: hex(cbor.encode(unsigned)),
  operation: unsigned,
  signedOperation: signed,
  signedHex: hex(signedBytes),
  cid: CID.createV1(cbor.code, await sha256.digest(signedBytes)).toString(),
  did,
}, null, 2))
