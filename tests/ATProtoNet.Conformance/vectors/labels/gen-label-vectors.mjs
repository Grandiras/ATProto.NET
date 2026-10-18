// Writes label-vectors.json: label signatures from the reference implementations (indigo's
// atproto/labeling test vector, labels served by Bluesky's moderation service, labels signed by
// @atproto/ozone's signLabel) and subscribeLabels frames from @atproto/common's cborEncode, as
// 0.8's LabelSigningInteropTests and LabelStreamFramesTests paste them, plus the variants derived
// from them here (high-S signatures, tampered values, issuer documents). Every pasted encoding and
// signature is re-checked with @atproto/lex-cbor and @atproto/crypto before the file is written.
// Run with @atproto/crypto 0.5.5 and @atproto/lex-cbor 0.1.7 installed:
// `node gen-label-vectors.mjs > label-vectors.json`.
import { verifySignature } from '@atproto/crypto'
import { encode } from '@atproto/lex-cbor'

const hex = (bytes) => Buffer.from(bytes).toString('hex')
const b64 = (text) => Buffer.from(text, 'base64')
const unpadded = (bytes) => Buffer.from(bytes).toString('base64').replace(/=+$/, '')

// indigo atproto/labeling/label_test.go, TestVerifyLabel.
const indigoKey = 'did:key:zQ3shcnfWLQN1bY4d2patsEAYFzy4xp1zdckEvHsV7S4ocTnC'
const indigo = {
  ver: 1, src: 'did:plc:n3timvoib5nau7gvwd6cshap', uri: 'did:plc:44ybard66vv44zksje25o7dz', val: 'bladerunner',
  cts: '2024-10-23T17:51:19.128Z', sig: { $bytes: 'uCRNA5mTzh078T5xZtkvLEt/O+z0gsKM3aqRI/lVAB8ZtbMnznwS/JwHopZE40JhNNDj80z8gsDLAp/hWqG5Pg' },
}
const indigoBytes = 'a5636374737818323032342d31302d32335431373a35313a31392e3132385a6373726378206469643a706c633a6e3374696d766f6962356e617537677677643663736861706375726978206469643a706c633a343479626172643636767634347a6b736a6532356f37647a6376616c6b626c61646572756e6e65726376657201'

// Bluesky's moderation service (queryLabels on mod.bsky.app) and its DID document.
const moderationDid = 'did:plc:ar7c4by46qjdydhdevvrndac'
const moderationLabelKey = 'did:key:zQ3shmV1BNcX17coaDbfen6zArEad6SCLT3jVWCbC6Y9iinTa'
const moderationAccountKey = 'did:key:zQ3shoG4QW9B3zvKSSiRwwc1De7MFQLNBT9A71gr12GKwMgHu'
const moderationDocument = {
  '@context': ['https://www.w3.org/ns/did/v1', 'https://w3id.org/security/multikey/v1', 'https://w3id.org/security/suites/secp256k1-2019/v1'],
  id: moderationDid,
  alsoKnownAs: ['at://moderation.bsky.app'],
  verificationMethod: [
    { id: `${moderationDid}#atproto`, type: 'Multikey', controller: moderationDid, publicKeyMultibase: 'zQ3shoG4QW9B3zvKSSiRwwc1De7MFQLNBT9A71gr12GKwMgHu' },
    { id: `${moderationDid}#atproto_label`, type: 'Multikey', controller: moderationDid, publicKeyMultibase: 'zQ3shmV1BNcX17coaDbfen6zArEad6SCLT3jVWCbC6Y9iinTa' },
  ],
  service: [
    { id: '#atproto_pds', type: 'AtprotoPersonalDataServer', serviceEndpoint: 'https://inkcap.us-east.host.bsky.network' },
    { id: '#atproto_labeler', type: 'AtprotoLabeler', serviceEndpoint: 'https://mod.bsky.app' },
  ],
}
const moderationLabel = {
  ver: 1, src: moderationDid, uri: 'did:plc:qx7s5vdotumqa4h52ejojeby', val: '!takedown', cts: '2025-03-13T13:23:33.592Z',
  sig: { $bytes: 'Rc8wJ5NUsP3jx6idbILYW549QE+4ym8p0nfdWxDDbR1bZgf1tqO5MiBKxkFz//hqdbaJrujVUkLby+IejB/LiQ' },
}
const moderationNegation = {
  ver: 1, src: moderationDid, uri: 'did:plc:jhp75pzoruahzoiyyjujcv63', val: '!takedown', neg: true, cts: '2025-03-13T13:54:41.618Z',
  sig: { $bytes: 'yOzNIKIe/zY5OKCySwjAIO9cQfQmWUc1cX4jxTh7KQI7TZWS9uIoVJzaMn4omMiTvPHBAgEPGGxMXIBF9bpMMA' },
}
const moderationNegationBytes = 'a6636374737818323032352d30332d31335431333a35343a34312e3631385a636e6567f56373726378206469643a706c633a617237633462793436716a6479646864657676726e6461636375726978206469643a706c633a6a68703735707a6f727561687a6f6979796a756a637636336376616c692174616b65646f776e6376657201'

// The keys @atproto/ozone's signLabel signed with, and its labels: every optional field set, and neg: false (dropped).
const keys = {
  k256: { curve: 'K-256', privateKey: '9085d2bef69286a6cbb51623c8fa258629945cd55ca705cc4e66700396894e0c', didKey: 'did:key:zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme' },
  p256: { curve: 'P-256', privateKey: 'd8e0c7a1f6c1b1f1c0e4d7b3a2f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a291', didKey: 'did:key:zDnaepfg2wiYzJMG65JaGtFztRv8CBpcerNEoNsWe5WbgcvrG' },
}
const ozoneLabeler = 'did:plc:ewvi7nxzyoun6zhxrhs64oiz'
const ozone = {
  ver: 1, src: ozoneLabeler, uri: 'at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3l6oveex3ii2l',
  cid: 'bafyreifxykqhed72s26cr4i64rxvrtofeqrly3j4vjzbkvo3ckkjbxjqtq', val: 'spam', neg: true,
  cts: '2024-10-23T17:51:19.128Z', exp: '2025-07-28T23:53:19.804Z',
}
const ozoneBytes = 'a863636964783b626166797265696678796b71686564373273323663723469363472787672746f666571726c79336a34766a7a626b766f33636b6b6a62786a717471636374737818323032342d31302d32335431373a35313a31392e3132385a636578707818323032352d30372d32385432333a35333a31392e3830345a636e6567f56373726378206469643a706c633a65777669376e787a796f756e367a687872687336346f697a63757269784661743a2f2f6469643a706c633a65777669376e787a796f756e367a687872687336346f697a2f6170702e62736b792e666565642e706f73742f336c366f76656578336969326c6376616c647370616d6376657201'
const ozoneSigs = {
  k256: 'B6And1ZqjHses9lTKJIUIgVHkjuBeo8JovlaVCpFANVcjjY4MJGOrJL500jHCSXoL56ToOfK2+a9TqVZ0nr4Rg',
  p256: '5+BoFN8IR8YyD7qsRx2eWDEabbLXS51kjuzfOk0JYgQ53liXV+93QNmcGDwdTebo6kZBpldd8NAgj9UuIk+rbQ',
}
const plain = { ver: 1, src: ozoneLabeler, uri: 'did:plc:44ybard66vv44zksje25o7dz', val: 'good', cts: '2024-10-23T17:51:19.128Z' }
const plainBytes = 'a5636374737818323032342d31302d32335431373a35313a31392e3132385a6373726378206469643a706c633a65777669376e787a796f756e367a687872687336346f697a6375726978206469643a706c633a343479626172643636767634347a6b736a6532356f37647a6376616c64676f6f646376657201'
const plainSigs = {
  k256: 'bXk5FxYpB22bpBZA0DbF7wlZ+gF0rn7I3nauaNkl6V55CmewyPLQ2NpAIxa1FWnuuygFc6IQzOaWl84BbY1Q4w',
  p256: 'KWqP19ovQgMZNQMRX9bhnDdSjRqV06u69XGhXl18Y4E2shvFSs2JkAv7yp+F4qJ5sVxis6JC+EQYund9ssQxfw',
}

/** The bytes a label's signature covers: its DAG-CBOR without sig, the cid as a string. */
const signingBytes = (label) => {
  const { sig, ...rest } = label
  return encode(rest)
}
const signed = (label, sig) => ({ ...label, sig: { $bytes: sig } })
const check = async (label, key, bytes) => {
  if (bytes && hex(signingBytes(label)) !== bytes) throw new Error(`signing bytes of ${label.val}`)
  if (!(await verifySignature(key, signingBytes(label), b64(label.sig.$bytes)))) throw new Error(`signature of ${label.val}`)
}
await check(indigo, indigoKey, indigoBytes)
await check(moderationLabel, moderationLabelKey)
await check(moderationNegation, moderationLabelKey, moderationNegationBytes)
for (const k of ['k256', 'p256']) {
  await check(signed(ozone, ozoneSigs[k]), keys[k].didKey, ozoneBytes)
  await check(signed(plain, plainSigs[k]), keys[k].didKey, plainBytes)
}

const ORDER = { k256: 0xfffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141n, p256: 0xffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551n }
/** The other valid form of an ECDSA signature: s replaced by n − s. */
const highS = (sig, curve) => {
  const bytes = b64(sig)
  const s = BigInt('0x' + hex(bytes.subarray(32)))
  return unpadded(Buffer.concat([bytes.subarray(0, 32), Buffer.from((ORDER[curve] - s).toString(16).padStart(64, '0'), 'hex')]))
}

const signatures = []
const sig = (name, label, key, expect, extra = {}) => signatures.push({ name, label, key, expect, ...extra })
sig('indigo vector', indigo, indigoKey, 'valid', { signingBytes: indigoBytes })
sig('indigo vector with a changed value', { ...indigo, val: 'wrong' }, indigoKey, 'invalidSignature')
sig('indigo vector with $type and undeclared fields', { ver: 1, $type: 'com.atproto.label.defs#label', extra: 7, ...indigo }, indigoKey, 'valid', { signingBytes: indigoBytes })
sig('bluesky moderation label', moderationLabel, moderationLabelKey, 'valid')
sig('bluesky moderation negation', moderationNegation, moderationLabelKey, 'valid', { signingBytes: moderationNegationBytes })
sig('bluesky moderation label against the account key', moderationLabel, moderationAccountKey, 'invalidSignature')
for (const k of ['k256', 'p256']) {
  sig(`ozone ${keys[k].curve} label`, signed(ozone, ozoneSigs[k]), keys[k].didKey, 'valid', { signingBytes: ozoneBytes })
  sig(`ozone ${keys[k].curve} label without neg`, signed(plain, plainSigs[k]), keys[k].didKey, 'valid', { signingBytes: plainBytes })
  sig(`ozone ${keys[k].curve} label, high-S`, signed(ozone, highS(ozoneSigs[k], k)), keys[k].didKey, 'invalidSignature')
}
sig('no signature', ozone, keys.k256.didKey, 'unsigned', { signingBytes: ozoneBytes })
sig('empty signature', signed(ozone, ''), keys.k256.didKey, 'unsigned')
{
  const { ver, ...unversioned } = indigo
  sig('no version', unversioned, indigoKey, 'unsupportedVersion')
}
sig('version 2', { ...indigo, ver: 2 }, indigoKey, 'unsupportedVersion')
sig('version 0', { ...indigo, ver: 0 }, indigoKey, 'unsupportedVersion')
// indigo encodes a neg: false that is present; only ozone's signer drops it before signing.
sig('explicit neg false', { ...plain, neg: false }, keys.k256.didKey, 'unsigned', { signingBytes: hex(signingBytes({ ...plain, neg: false })) })

// The labeler's DID document decides the key: #atproto_label, never #atproto.
const document = (label, account) => ({
  id: ozoneLabeler,
  verificationMethod: [
    ...(account ? [{ id: `${ozoneLabeler}#atproto`, type: 'Multikey', controller: ozoneLabeler, publicKeyMultibase: account.slice(8) }] : []),
    ...(label ? [{ id: `${ozoneLabeler}#atproto_label`, type: 'Multikey', controller: ozoneLabeler, publicKeyMultibase: label.slice(8) }] : []),
  ],
})
const byK256 = signed(ozone, ozoneSigs.k256)
const byP256 = signed(ozone, ozoneSigs.p256)
const issuers = []
const issuer = (name, label, documents, expect) => issuers.push({ name, label, ...documents, expect })
issuer('bluesky moderation label', moderationLabel, { document: moderationDocument }, 'valid')
issuer('bluesky moderation negation', moderationNegation, { document: moderationDocument }, 'valid')
issuer('signed with the published label key', byK256, { document: document(keys.k256.didKey, keys.p256.didKey) }, 'valid')
issuer('signed with the account key', byP256, { document: document(keys.k256.didKey, keys.p256.didKey) }, 'invalidSignature')
issuer('no label key published', byK256, { document: document(null, keys.k256.didKey) }, 'noLabelKey')
issuer('label key rotated since cached', byK256,
  { document: document(keys.p256.didKey), freshDocument: document(keys.k256.didKey) }, 'valid')
issuer('label key published since cached', byK256,
  { document: document(null, keys.p256.didKey), freshDocument: document(keys.k256.didKey, keys.p256.didKey) }, 'valid')
issuer('signed before a rotation', byK256, { document: document(keys.p256.didKey) }, 'invalidSignature')
issuer('tampered label', { ...byK256, val: '!hide' }, { document: document(keys.k256.didKey) }, 'invalidSignature')
issuer('malformed label key', byK256, {
  document: { id: ozoneLabeler, verificationMethod: [{ id: `${ozoneLabeler}#atproto_label`, type: 'Multikey', controller: ozoneLabeler, publicKeyMultibase: 'z1111111111111111111' }] },
}, 'noLabelKey')
issuer('issuer does not resolve', byK256, { document: null }, 'issuerUnresolved')
issuer('unsigned label', ozone, { document: document(keys.k256.didKey) }, 'unsigned')

// Signing with the reference keys must sign the reference bytes, dropping a neg: false.
const signing = [
  { name: 'every optional field, K-256', label: ozone, key: 'k256', signingBytes: ozoneBytes },
  { name: 'every optional field, P-256', label: ozone, key: 'p256', signingBytes: ozoneBytes },
  { name: 'neg false is dropped, K-256', label: { ...plain, neg: false }, key: 'k256', signingBytes: plainBytes },
  { name: 'neg false is dropped, P-256', label: { ...plain, neg: false }, key: 'p256', signingBytes: plainBytes },
]

// subscribeLabels frames: cborEncode of the header, then of the body.
const indigoLabel = { ...indigo, sig: b64(indigo.sig.$bytes) }
const frames = [
  { name: 'labels', message: { t: '#labels', seq: 42, labels: [indigo] }, frame: 'a2617467236c6162656c73626f7001' + hex(encode({ seq: 42, labels: [indigoLabel] })) },
  { name: 'info', message: { t: '#info', name: 'OutdatedCursor', message: 'too old' }, frame: 'a261746523696e666f626f7001a2646e616d656e4f75746461746564437572736f72676d65737361676567746f6f206f6c64' },
  { name: 'error', message: { error: 'FutureCursor', message: 'Cursor in the future.' }, frame: 'a1626f7020a2656572726f726c467574757265437572736f72676d65737361676575437572736f7220696e20746865206675747572652e' },
  { name: 'unsigned label', message: { t: '#labels', seq: 1, labels: [{ ver: 1, src: indigo.src, uri: indigo.uri, val: 'spam', cts: indigo.cts }] }, frame: null },
  { name: 'info without a name', message: { t: '#info', name: '' }, frame: null },
]
const pastedLabelsFrame = 'a2617467236c6162656c73626f7001a263736571182a666c6162656c7381a6636374737818323032342d31302d32335431373a35313a31392e3132385a637369675840b8244d039993ce1d3bf13e7166d92f2c4b7f3becf482c28cddaa9123f955001f19b5b327ce7c12fc9c07a29644e3426134d0e3f34cfc82c0cb029fe15aa1b93e6373726378206469643a706c633a6e3374696d766f6962356e617537677677643663736861706375726978206469643a706c633a343479626172643636767634347a6b736a6532356f37647a6376616c6b626c61646572756e6e65726376657201'
if (frames[0].frame !== pastedLabelsFrame) throw new Error('labels frame')
if (frames[1].frame !== hex(encode({ t: '#info', op: 1 })) + hex(encode({ name: 'OutdatedCursor', message: 'too old' }))) throw new Error('info frame')
if (frames[2].frame !== hex(encode({ op: -1 })) + hex(encode({ error: 'FutureCursor', message: 'Cursor in the future.' }))) throw new Error('error frame')

console.log(JSON.stringify({
  generator: { '@atproto/crypto': '0.5.5', '@atproto/lex-cbor': '0.1.7' },
  keys,
  signatures,
  issuers,
  signing,
  frames,
}, null, 2))
