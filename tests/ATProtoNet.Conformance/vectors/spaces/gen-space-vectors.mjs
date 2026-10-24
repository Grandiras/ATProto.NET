// Writes the Spaces vectors (blake3-long-output.json, lthash.json, http-signatures.json,
// structured-fields.json, space-tokens.json, kem.json, space-mac.json, star.json) from the
// reference implementation pinned in MANIFEST.md: bluesky-social/atproto `packages/space/src`,
// run as TypeScript, with the packages of the same alpha release installed (README.md):
//   node --experimental-transform-types gen-space-vectors.mjs <packages/space/src> <out dir>
// The reference sources are copied next to the working directory's node_modules with their
// relative `.js` imports pointed at the `.ts` files. Every case carries the reference verdict; a
// case whose expectation goes beyond the reference says so (`beyondReference`) and why (`note`).
// Deterministic: fixed keys (SHA-256 of fixed strings), claims, clock and ephemeral KEM keys;
// RFC 6979 signatures.
import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { encodeVarInt } from '@atproto/car'
import { P256Keypair, Secp256k1Keypair, formatDidKey, parseDidKey } from '@atproto/crypto'
import { encode } from '@atproto/lex-cbor'
import { cidForCbor, parseCid } from '@atproto/lex-data'
import { blake3 } from '@noble/hashes/blake3'
import { parseDictionary, serializeInnerList, serializeItem, isInnerList } from 'structured-headers'
import { toString as multibase } from 'uint8arrays'

const [source, out] = process.argv.slice(2)
const copy = resolve('.space-ref')
const copyTree = (from, to) => {
  mkdirSync(to, { recursive: true })
  for (const entry of readdirSync(from, { withFileTypes: true })) {
    if (entry.isDirectory()) copyTree(join(from, entry.name), join(to, entry.name))
    else if (entry.name.endsWith('.ts') && !entry.name.endsWith('.test.ts'))
      writeFileSync(join(to, entry.name), readFileSync(join(from, entry.name), 'utf8').replace(/(from '\.{1,2}\/[\w/-]+)\.js'/g, "$1.ts'"))
  }
}
copyTree(source, copy)
const { LtHash } = await import(join(copy, 'lthash.ts'))
const { createSpaceSig, createSpaceSigHeaders, verifyCredentialSig } = await import(join(copy, 'http-signature.ts'))
const { verifySpaceToken } = await import(join(copy, 'credential.ts'))
const { encapsulate, decapsulate } = await import(join(copy, 'kem.ts'))
const mac = await import(join(copy, 'mac.ts'))
const { STAR_MAGIC, readStar } = await import(join(copy, 'star.ts'))
const { RepoCommit } = await import(join(copy, 'repo-commit.ts'))
const { serializeRepo, serializeRecord } = await import(join(copy, 'sync/provider.ts'))
const { verifyRepoStarFull } = await import(join(copy, 'sync/consumer.ts'))

const sha256 = (data) => createHash('sha256').update(data).digest()
const hex = (bytes) => Buffer.from(bytes).toString('hex')
const write = (name, value) => writeFileSync(join(out, name), JSON.stringify(value, null, 2) + '\n')
const attempt = async (f) => { try { return { ok: true, value: await f() } } catch (err) { return { ok: false, error: `${err.name}: ${err.message}`.replace(/\s+/g, ' ').slice(0, 200) } } }
const assert = (condition, message) => { if (!condition) throw new Error(message) }

// ── BLAKE3 past the official vectors' 131 bytes: the batched output paths ──
{
  const input = (length) => Uint8Array.from({ length }, (_, i) => i % 251)
  const cases = [[0, 2048], [1, 2048], [64, 2048], [65, 1000], [1024, 2048], [1025, 2048], [2049, 1000], [8193, 4099], [102400, 2048]]
    .map(([inputLength, outputLength]) => ({
      name: `${inputLength} bytes in, ${outputLength} out`, inputLength, outputLength,
      sha256: hex(sha256(blake3(input(inputLength), { dkLen: outputLength }))),
    }))
  write('blake3-long-output.json', {
    comment: 'BLAKE3 extended output past the official vectors: SHA-256 of @noble/hashes 1.8.0 output for inputs of i % 251.',
    cases,
  })
}

// ── LtHash ───────────────────────────────────────────────
{
  const A = 'com.example.note/aaa/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq'
  const B = 'com.example.note/bbb/bafyreihrjacrc7vmmyiuxka7uio7ximst76ippf6xuqa6c256olh3mxojq'
  const C = 'com.example.other/c/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq'
  const single = 'app.bsky.feed.post/3l6oveex3ii2l/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq'
  const cases = []
  const add = (name, ops, withState = false) => {
    const hash = new LtHash()
    for (const [op, element] of ops) op === 'add' ? hash.add(element) : hash.remove(element)
    cases.push({ name, ops: ops.map(([op, element]) => ({ op, element })), digest: hex(hash.digest()), ...(withState ? { state: hex(hash.state()) } : {}) })
  }
  add('empty', [])
  add('one element', [['add', single]], true)
  add('three elements', [['add', A], ['add', B], ['add', C]])
  add('three elements backwards', [['add', C], ['add', B], ['add', A]])
  add('non-ASCII element', [['add', 'com.example.nøte/é/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq']])
  add('remove undoes add', [['add', A], ['add', B], ['remove', A], ['remove', B]], true)
  add('remove before add', [['remove', A], ['add', B], ['add', A]])
  add('one element added twice', [['add', A], ['add', A]])
  add('added twice, removed once', [['add', A], ['add', A], ['remove', A]])
  for (const length of [0, 1, 90, 600, 1500]) {
    const element = 'x'.repeat(length) + '/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq'
    add(`element of ${length} + 60 characters added`, [['add', A], ['add', B], ['add', element]], true)
    add(`element of ${length} + 60 characters removed`, [['add', A], ['add', B], ['remove', element]], true)
  }
  write('lthash.json', { comment: 'LtHash digests (SHA-256 of the 2,048-byte state) and states, from the reference lthash.ts.', cases })
}

// ── HTTP message signatures (SPC-22, SPC-24) ─────────────
{
  const LABEL = 'atproto-space-credential'
  const TAG = `;tag="${LABEL}"`
  const key = await P256Keypair.import(hex(sha256('atproto.net space vector key 1')))
  const other = await P256Keypair.import(hex(sha256('atproto.net space vector key 2')))
  const k256 = await Secp256k1Keypair.import(hex(sha256('atproto.net space vector key 3')))
  const KEY = key.did()
  const credential = 'eyJhbGciOiJFUzI1NiIsInR5cCI6ImF0cHJvdG8tc3BhY2UtY3JlZGVudGlhbCtqd3QifQ.eyJzdWIiOiJhdDovL2RpZDpwbGM6YXV0aG9yaXR5L3NwYWNlL2NvbS5leGFtcGxlLmZvcnVtL2RlZmF1bHQifQ.c2ln'
  const AUTH = `Atproto-Space-Credential ${credential}`
  const AUDIENCE = 'did:plc:repoowner'
  const N = 0xffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551n

  const b64 = (bytes) => Buffer.from(bytes).toString('base64')
  const base = (authorization, audience, params) => `"authorization": ${authorization.trim()}\n"atproto-space-audience": ${audience.trim()}\n"@signature-params": ${params}`
  const canonical = (input) => serializeInnerList(parseDictionary(`x=${input}`).get('x'))
  const signBase = async (signer, input, authorization = AUTH, audience = AUDIENCE) => b64(await signer.sign(Buffer.from(base(authorization, audience, canonical(input)))))
  const label = (sig, name = LABEL) => `${name}=:${sig}:`

  const ref = await createSpaceSig(key, { authorization: AUTH, audience: AUDIENCE })
  assert(ref.signatureInput === `("authorization" "atproto-space-audience")${TAG}`, 'reference signature input')
  const refInput = `${LABEL}=${ref.signatureInput}`
  const refSig = label(b64(ref.signature))
  const raw = Buffer.from(ref.signature)
  const highS = Buffer.concat([raw.subarray(0, 32), Buffer.from((N - BigInt('0x' + hex(raw.subarray(32)))).toString(16).padStart(64, '0'), 'hex')])
  const der = (() => {
    const int = (v) => { let t = v; while (t.length > 1 && t[0] === 0) t = t.subarray(1); if (t[0] & 0x80) t = Buffer.concat([Buffer.from([0]), t]); return Buffer.concat([Buffer.from([2, t.length]), t]) }
    const body = Buffer.concat([int(raw.subarray(0, 32)), int(raw.subarray(32))])
    return Buffer.concat([Buffer.from([0x30, body.length]), body])
  })()

  const cases = []
  const add = async (name, c, expect, extra = {}) => {
    const headers = {}
    if (c.authorization !== null) headers.authorization = c.authorization
    if (c.audience !== null) headers['atproto-space-audience'] = c.audience
    if (c.signatureInput !== null) headers['signature-input'] = c.signatureInput
    if (c.signature !== null) headers.signature = c.signature
    const reference = await attempt(() => verifyCredentialSig(headers, c.keyId))
    if (!extra.beyondReference && reference.ok !== expect.ok) throw new Error(`${name}: the reference says ${JSON.stringify(reference)}`)
    cases.push({ name, ...c, expect, reference: reference.ok, ...(reference.ok ? {} : { referenceError: reference.error }), ...extra })
  }
  const request = (change = {}) => ({ authorization: AUTH, audience: AUDIENCE, signatureInput: refInput, signature: refSig, keyId: KEY, ...change })
  const signed = async (input, change = {}) => request({ signatureInput: `${LABEL}=${input}`, signature: label(await signBase(key, input, change.authorization, change.audience)), ...change })
  const ok = { ok: true, key: KEY }
  const no = { ok: false }

  await add('reference signature', request(), ok)
  await add('high-S signature', request({ signature: label(b64(highS)) }), ok)
  {
    const input = `("authorization" "atproto-space-audience");alg="ecdsa-p256-sha256";keyid="${KEY}"${TAG}`
    await add('optional parameters and other labels', request({
      signatureInput: `other=("authorization");keyid="other", ${LABEL}=${input}`,
      signature: `other=:YWJj:, ${label(await signBase(key, input))}`,
    }), ok)
  }
  await add('tag before the other parameters', await signed(`("authorization" "atproto-space-audience")${TAG};keyid="${KEY}";alg="ecdsa-p256-sha256"`), ok)
  await add('input spelled loosely, signed canonically', request({
    signatureInput: `${LABEL}=(  "authorization"   "atproto-space-audience"  );  keyid="${KEY}";tag="${LABEL}"`,
    signature: label(await signBase(key, `("authorization" "atproto-space-audience");keyid="${KEY}"${TAG}`)),
  }), ok)
  await add('every parameter type, signed in canonical form',
    await signed(`("authorization" "atproto-space-audience");keyid="${KEY}";created=1618884473;nonce="a\\"b\\\\c"${TAG};t=tok;flag;b=:YWJj:;d=1.50`), ok)
  await add('the same signature again', request(), ok)
  {
    const lower = `atproto-space-credential ${credential}`
    await add('scheme in lower case, signed as sent', await signed(ref.signatureInput, { authorization: lower }), ok,
      { note: 'SPC-24: the scheme is matched case-insensitively by the receiver, but the base covers the field value as received.' })
    await add('scheme case changed after signing', request({ authorization: lower }), no, { note: 'SPC-24: the base covers the field value as received.' })
  }
  await add('fields with surrounding whitespace', request({ authorization: ` ${AUTH}  `, audience: `\t${AUDIENCE} ` }), ok,
    { note: 'RFC 9421 §2.1: leading and trailing whitespace is stripped from field values in the base.' })

  await add('changed authorization', request({ authorization: AUTH + '-changed' }), no)
  await add('another credential', request({ authorization: 'Atproto-Space-Credential another.credential.jwt' }), no)
  await add('changed audience', request({ audience: 'did:plc:someoneelse' }), no)
  await add('another signing key', request({ keyId: other.did() }), no)
  await add('keyid differing from the credential key', await signed(`("authorization" "atproto-space-audience");keyid="${other.did()}"${TAG}`), no)
  for (const parameter of [';keyid=1', ';keyid', ';keyid=tok'])
    await add(`keyid ${parameter.slice(1)} that is not a string`, await signed(`("authorization" "atproto-space-audience")${TAG}${parameter}`), no)
  await add('a signature parameter added after signing', request({ signatureInput: `${refInput};alg="ecdsa-p256-sha256"` }), no)
  await add('without a tag', await signed('("authorization" "atproto-space-audience")'), no)
  await add('another tag', await signed('("authorization" "atproto-space-audience");tag="atproto-space"'), no)
  await add('tag as a token', await signed('("authorization" "atproto-space-audience");tag=atproto-space-credential'), no)
  await add('tag in upper case', await signed('("authorization" "atproto-space-audience");tag="ATPROTO-SPACE-CREDENTIAL"'), no)
  {
    const input = `("authorization" "atproto-space-audience")${TAG}`
    await add('the alpha label atproto-space', request({ signatureInput: `atproto-space=${input}`, signature: label(await signBase(key, input), 'atproto-space') }), no)
    await add('label only in the signature input', request({ signature: label(b64(ref.signature), 'other') }), no)
  }
  await add('audience not covered by the signature', await signed(`("authorization")${TAG}`), no)
  for (const components of ['("atproto-space-audience")', '("atproto-space-audience" "authorization")', '("authorization" "atproto-space-audience" "content-type")',
    '("authorization" "authorization" "atproto-space-audience")', '("authorization";sf "atproto-space-audience")', '(authorization "atproto-space-audience")',
    '("Authorization" "atproto-space-audience")', '("@method" "atproto-space-audience")', '()'])
    await add(`components ${components}`, request({ signatureInput: `${LABEL}=${components}${TAG}` }), no)
  for (const input of ['not-a-list', '', LABEL, `${LABEL}=1`, `${LABEL}="authorization"`, `other=("authorization" "atproto-space-audience")${TAG}`])
    await add(`signature input ${JSON.stringify(input)}`, request({ signatureInput: input }), no)
  for (const signature of ['', 'not-a-byte-sequence', `${LABEL}=not-a-byte-sequence`, `${LABEL}=:YWJj:`, `${LABEL}=:!!!:`, `${LABEL}=1`,
    'other=:YWJj:', `${LABEL}=(:YWJj:)`])
    await add(`signature ${JSON.stringify(signature)}`, request({ signature }), no)
  await add('signature with a parameter', request({ signature: `${refSig};x=1` }), no)
  await add('DER-encoded signature', request({ signature: label(b64(der)) }), no)
  await add('signature of zeros', request({ signature: label(b64(new Uint8Array(64))) }), no)
  await add('signature of ones', request({ signature: label(b64(new Uint8Array(64).fill(0xff))) }), no)
  for (const alg of ['ecdsa-p384-sha384', 'ed25519', 'rsa-pss-sha512'])
    await add(`algorithm ${alg}`, await signed(`("authorization" "atproto-space-audience");alg="${alg}"${TAG}`), no)
  await add('algorithm as a token', await signed(`("authorization" "atproto-space-audience");alg=ecdsa-p256-sha256${TAG}`), no)
  await add('credential key that is not P-256', request({ keyId: k256.did() }), no)
  await add('credential key that is not a did:key', request({ keyId: AUDIENCE }), no)
  await add('empty authorization', request({ authorization: '' }), no)
  await add('without authorization', request({ authorization: null }), no)
  await add('empty audience', request({ audience: '' }), no)
  await add('without audience', request({ audience: null }), no)
  await add('without Signature-Input', request({ signatureInput: null }), no)
  await add('without Signature', request({ signature: null }), no)
  const repeated = { beyondReference: true, note: 'SPC-22 refuses a repeated label; the reference (structured-headers 1.0.1) takes the last member.' }
  await add('label repeated in the signature input', request({ signatureInput: `${refInput}, ${refInput}` }), no, repeated)
  await add('label repeated in the signature', request({ signature: `${refSig}, ${refSig}` }), no, repeated)

  const headers = await createSpaceSigHeaders(key, { authorization: AUTH, audience: AUDIENCE })
  assert(headers['signature-input'] === refInput && headers.signature === refSig, 'reference signature headers')
  const k256Refused = await attempt(() => createSpaceSig(k256, { authorization: AUTH, audience: AUDIENCE }))
  assert(!k256Refused.ok, 'K-256 signing')
  const signing = [
    { name: 'request', curve: 'P-256', authorization: AUTH, audience: AUDIENCE, signatureInput: refInput, base: base(AUTH, AUDIENCE, ref.signatureInput) },
    { name: 'request with a K-256 key', curve: 'K-256', authorization: AUTH, audience: AUDIENCE, signatureInput: null, referenceError: k256Refused.error },
  ]
  write('http-signatures.json', {
    comment: 'RFC 9421 space credential signatures (label and tag atproto-space-credential, covering ("authorization" "atproto-space-audience")) and the verdicts owed, checked against the reference verifyCredentialSig(headers, cnf.kid). A null field is an absent header.',
    keys: { 'P-256': { privateKey: hex(sha256('atproto.net space vector key 1')), didKey: KEY }, 'K-256': { privateKey: hex(sha256('atproto.net space vector key 3')), didKey: k256.did() } },
    cases,
    signing,
  })
}

// ── RFC 8941 structured fields ───────────────────────────
{
  const reference = (field, key) => {
    try {
      const member = parseDictionary(field).get(key)
      return member === undefined ? { parsed: true, member: null } : { parsed: true, member: isInnerList(member) ? serializeInnerList(member) : serializeItem(member) }
    } catch { return { parsed: false } }
  }
  const cases = []
  const add = (name, field, key, expect) => {
    const ref = reference(field, key)
    const agrees = ref.parsed === expect.parsed && (!expect.parsed || ref.member === expect.member)
    cases.push({ name, field, key, expect, ...(agrees ? {} : { reference: ref }) })
  }
  for (const [field, canonical] of [
    ['sig=("a" "b")', '("a" "b")'], ['sig=(  "a"   "b"  )', '("a" "b")'], ['sig=()', '()'],
    ['sig=("a");keyid="x"', '("a");keyid="x"'], ['sig=("a");  keyid="x"', '("a");keyid="x"'],
    ['sig=("a");alg="p";keyid="x"', '("a");alg="p";keyid="x"'], ['sig=("a";sf "b")', '("a";sf "b")'],
    ['sig=("a");n=1;d=1.50;t=tok;b=:YWJj:;f;g=?0;h=?1', '("a");n=1;d=1.5;t=tok;b=:YWJj:;f;g=?0;h'],
    ['sig=("a");d=-0.0', '("a");d=0.0'], ['sig=("a");n=-42', '("a");n=-42'], ['sig=("a");s="q\\"\\\\"', '("a");s="q\\"\\\\"'],
    ['sig=("a");b=:YWJjZA:', '("a");b=:YWJjZA==:'], ['sig=("a");k=1;k=2', '("a");k=2'], ['sig=("a");k=1;j=1;k=2', '("a");k=2;j=1'],
  ])
    add(`canonical ${field}`, field, 'sig', { parsed: true, member: canonical })
  const members = 'a=1, b="two" ,\tc=("x"), d, sig=("a")'
  for (const [key, member] of [['a', '1'], ['b', '"two"'], ['c', '("x")'], ['d', '?1'], ['sig', '("a")'], ['missing', null]])
    add(`member ${key} among others`, members, key, { parsed: true, member })
  add('byte sequence', 'sig=:AQID:', 'sig', { parsed: true, member: ':AQID:' })
  add('empty field', '', 'sig', { parsed: true, member: null })
  add('repeated key', 'sig=1, other=2, sig=3', 'sig', { parsed: true, member: '3', duplicated: true })
  add('key next to a repeated one', 'sig=1, other=2, sig=3', 'other', { parsed: true, member: '2', duplicated: false })
  add('largest integer', 'n=999999999999999', 'n', { parsed: true, member: '999999999999999' })
  add('largest decimal', 'n=999999999999.999', 'n', { parsed: true, member: '999999999999.999' })
  for (const field of ['sig=', 'sig=1,', ',sig=1', 'sig=1 sig2=2', 'Sig=1', '1sig=1', 'sig=("a"', 'sig=("a""b")', 'sig=("a";)', 'sig="unterminated',
    'sig="bad\\escape"', 'sig="tab\there"', 'sig="café"', 'sig=?2', 'sig=?', 'sig=:YWJj', 'sig=:YW!j:', 'sig=:Y:', 'sig=1.', 'sig=1.2345',
    'sig=1234567890123456', 'sig=1234567890123.5', 'sig=-', 'sig=@1618884473', 'sig=%"display"', 'sig=("a");k=', 'sig=1;'])
    add(`malformed ${field}`, field, 'sig', { parsed: false })
  write('structured-fields.json', {
    comment: 'RFC 8941 dictionaries: the canonical serialization of one member, or a refusal. `reference` records where structured-headers 1.0.1 differs.',
    cases,
  })
}

// ── Space tokens (SPC-18…21) ─────────────────────────────
{
  const NOW = 1767225600 // 2026-01-01T00:00:00Z
  const k = async (text, Kind) => Kind.import(hex(sha256(text)))
  const user = await k('atproto.net space token user', Secp256k1Keypair)
  const authority = await k('atproto.net space token authority', Secp256k1Keypair)
  const intruder = await k('atproto.net space token intruder', Secp256k1Keypair)
  const holder = await k('atproto.net space token holder', P256Keypair)
  const client = await k('atproto.net space token client', P256Keypair)
  const USER = 'did:plc:z72i7hdynmk6r22z27h6tvur'
  const AUTHORITY = 'did:plc:ewvi7nxzyoun6zhxrhs64oiz'
  const SPACE = `at://${AUTHORITY}/space/com.atmoboards.forum/default`
  const HOST = `${AUTHORITY}#atproto_pds`
  const CLIENT = 'https://app.example.com/client-metadata.json'
  const TYP = { delegation: 'atproto-space-delegation+jwt', credential: 'atproto-space-credential+jwt', clientAttestation: 'atproto-client-attestation+jwt' }
  const json = (v) => Buffer.from(JSON.stringify(v).replace(/"@@(-?\d+)@@"/g, '$1')).toString('base64url')
  let serial = 0

  /** A token in the reference's encoding (createSpaceToken), with a fixed iat and jti and one field changed. */
  const mint = async (type, { header, payload, signer } = {}) => {
    const h = { alg: (signer ?? { delegation: user, credential: authority, clientAttestation: client }[type]).jwtAlg, typ: TYP[type] }
    if (type !== 'clientAttestation') h.kid = '#atproto'
    header?.(h)
    const base = {
      delegation: { iss: USER, sub: SPACE, aud: HOST },
      credential: { iss: AUTHORITY, sub: SPACE, cnf: { kid: holder.did() } },
      clientAttestation: { iss: CLIENT, sub: CLIENT, aud: HOST },
    }[type]
    const p = { ...base, iat: NOW, exp: NOW + (type === 'credential' ? 600 : 60), jti: hex(sha256(`jti ${++serial}`)).slice(0, 32) }
    payload?.(p)
    const input = `${json(h)}.${json(p)}`
    const key = signer ?? { delegation: user, credential: authority, clientAttestation: client }[type]
    return `${input}.${Buffer.from(await key.sign(Buffer.from(input))).toString('base64url')}`
  }

  const cases = []
  const add = async (name, type, token, expect, { key, aud, sub, at = 0, beyondReference, note } = {}) => {
    const signingKey = key ?? { delegation: user, credential: authority, clientAttestation: client }[type].did()
    const audience = aud === undefined ? (type === 'credential' ? undefined : HOST) : aud ?? undefined
    const subject = sub === undefined ? (type === 'clientAttestation' ? undefined : SPACE) : sub ?? undefined
    const realNow = Date.now
    Date.now = () => (NOW + at) * 1000
    let reference
    try {
      await verifySpaceToken(type, token, { getSigningKey: () => signingKey, aud: audience, sub: subject })
      reference = { ok: true }
    } catch (err) { reference = { ok: false, error: err.code } }
    finally { Date.now = realNow }
    if (!beyondReference && (reference.ok !== expect.ok || (!expect.ok && reference.error !== expect.error)))
      throw new Error(`${name}: the reference says ${JSON.stringify(reference)}`)
    cases.push({ name, type, token, signingKey, audience: audience ?? null, subject: subject ?? null, now: NOW + at, expect, ...(beyondReference ? { beyondReference, reference } : {}), ...(note ? { note } : {}) })
  }
  const ok = { ok: true }
  const no = (error) => ({ ok: false, error })

  await add('delegation token', 'delegation', await mint('delegation'), ok)
  await add('delegation token with the wrong key', 'delegation', await mint('delegation'), no('BadJwtSignature'), { key: intruder.did() })
  await add('delegation token for another audience', 'delegation', await mint('delegation'), no('BadJwtAudience'), { aud: `${USER}#atproto_pds` })
  await add('delegation token for the alpha audience #atproto_space_host', 'delegation', await mint('delegation', { payload: (p) => { p.aud = `${AUTHORITY}#atproto_space_host` } }), no('BadJwtAudience'),
    { note: 'SPC-19: the audience is the authority\'s #atproto_pds since the 2026-10-22 alpha.' })
  await add('delegation token for another space', 'delegation', await mint('delegation'), no('BadJwtSub'), { sub: `at://${AUTHORITY}/space/com.atmoboards.forum/other` })
  await add('expired delegation token', 'delegation', await mint('delegation'), no('JwtExpired'), { at: 70 })
  await add('delegation token just past exp, within the skew', 'delegation', await mint('delegation'), ok, { at: 63 })
  {
    const [h, , s] = (await mint('delegation')).split('.')
    const forged = (await mint('delegation', { payload: (p) => { p.sub = `at://${AUTHORITY}/space/com.atmoboards.forum/other` } })).split('.')[1]
    await add('tampered delegation token', 'delegation', `${h}.${forged}.${s}`, no('BadJwtSignature'), { sub: null })
  }
  await add('credential presented as a delegation token', 'delegation', await mint('credential'), no('BadJwtType'), { key: authority.did() })
  await add('delegation token without aud', 'delegation', await mint('delegation', { payload: (p) => { delete p.aud } }), no('BadJwtAudience'), { aud: null })
  await add('delegation token without jti', 'delegation', await mint('delegation', { payload: (p) => { delete p.jti } }), no('BadJwt'))
  await add('delegation token with kid #other_key', 'delegation', await mint('delegation', { header: (h) => { h.kid = '#other_key' } }), no('BadJwt'), { note: 'SPC-19: kid is exactly #atproto.' })
  await add('delegation token without kid', 'delegation', await mint('delegation', { header: (h) => { delete h.kid } }), no('BadJwt'), { note: 'SPC-19: kid is exactly #atproto.' })
  for (const malformed of ['onlyonepart', 'a.b.c.d', '!!!.!!!.!!!'])
    await add(`malformed token ${malformed}`, 'delegation', malformed, no('BadJwt'))
  await add('token whose header has no typ', 'delegation', 'e30.WzFd.AAAA', no('BadJwtType'))
  await add('delegation token expiring in the last representable second', 'delegation', await mint('delegation', { payload: (p) => { p.exp = 253402300799 } }), ok)
  for (const [claim, seconds] of [['exp', '253402300800'], ['exp', '9223372036854775807'], ['exp', '-9223372036854775808'], ['iat', '253402300800'], ['iat', '-62135596801']])
    await add(`delegation token ${claim} ${seconds} outside the representable range`, 'delegation',
      await mint('delegation', { payload: (p) => { p[claim] = `@@${seconds}@@` } }), no('BadJwt'), { beyondReference: true, note: 'SPC-20: time claims outside the representable range are refused.' })
  for (const jti of [' ', '\t\t', 'a\u0000b'])
    await add(`delegation token jti ${JSON.stringify(jti)} that cannot be spent`, 'delegation', await mint('delegation', { payload: (p) => { p.jti = jti } }), no('BadJwt'), { beyondReference: true, note: 'SPC-19: jti is 128-bit hex.' })
  await add('delegation token with an overlong jti', 'delegation', await mint('delegation', { payload: (p) => { p.jti = 'a'.repeat(256) } }), no('BadJwt'), { beyondReference: true, note: 'SPC-19: jti is 128-bit hex.' })

  await add('credential', 'credential', await mint('credential'), ok)
  await add('credential bound by jkt', 'credential', await mint('credential', { payload: (p) => { p.cnf = { jkt: 'thumbprint' } } }), no('BadJwtCnf'))
  await add('credential bound by another member', 'credential', await mint('credential', { payload: (p) => { p.cnf = { other: holder.did() } } }), no('BadJwtCnf'))
  await add('credential without a binding', 'credential', await mint('credential', { payload: (p) => { delete p.cnf } }), no('BadJwtCnf'))
  await add('credential bound to a DID that is not a did:key', 'credential', await mint('credential', { payload: (p) => { p.cnf = { kid: USER } } }), no('BadJwtCnf'),
    { beyondReference: true, note: 'SPC-19: cnf.kid is a P-256 did:key; the reference checks only that it is a DID (the request signature check refuses it later).' })
  await add('credential bound to a K-256 did:key', 'credential', await mint('credential', { payload: (p) => { p.cnf = { kid: user.did() } } }), no('BadJwtCnf'),
    { beyondReference: true, note: 'SPC-19: cnf.kid is a P-256 did:key; the reference checks only that it is a DID (the request signature check refuses it later).' })
  await add('credential with kid #atproto_space', 'credential', await mint('credential', { header: (h) => { h.kid = '#atproto_space' } }), no('BadJwt'),
    { note: 'SPC-19: kid is exactly #atproto; the 2026-10-15 reference honoured a dedicated #atproto_space key.' })
  await add('credential without kid', 'credential', await mint('credential', { header: (h) => { delete h.kid } }), no('BadJwt'), { note: 'SPC-19: kid is exactly #atproto.' })
  await add('credential with an empty jti', 'credential', await mint('credential', { payload: (p) => { p.jti = '' } }), no('BadJwt'))
  await add('credential with a blank jti', 'credential', await mint('credential', { payload: (p) => { p.jti = ' ' } }), no('BadJwt'), { beyondReference: true, note: 'SPC-19: jti is 128-bit hex.' })
  await add('credential without iat', 'credential', await mint('credential', { payload: (p) => { delete p.iat } }), no('BadJwt'))
  for (const lifetime of [0, -1, 3601])
    await add(`credential lasting ${lifetime} s`, 'credential', await mint('credential', { payload: (p) => { p.exp = p.iat + lifetime } }), no('BadJwt'))
  await add('credential lasting exactly an hour', 'credential', await mint('credential', { payload: (p) => { p.exp = p.iat + 3600 } }), ok)
  await add('credential issued 4 s ahead', 'credential', await mint('credential', { payload: (p) => { p.iat = NOW + 4; p.exp = NOW + 604 } }), ok)
  for (const ahead of [6, 600])
    await add(`credential issued ${ahead} s ahead`, 'credential', await mint('credential', { payload: (p) => { p.iat = NOW + ahead; p.exp = NOW + ahead + 600 } }), no('BadJwt'))
  await add('credential with the wrong key', 'credential', await mint('credential'), no('BadJwtSignature'), { key: intruder.did() })
  await add('expired credential', 'credential', await mint('credential'), no('JwtExpired'), { at: 700 })

  await add('client attestation', 'clientAttestation', await mint('clientAttestation'), ok)
  await add('client attestation with another subject', 'clientAttestation', await mint('clientAttestation', { payload: (p) => { p.sub = 'https://other.example.com/client-metadata.json' } }), no('BadJwtIss'))
  await add('client attestation with a kid', 'clientAttestation', await mint('clientAttestation', { header: (h) => { h.kid = 'client-key-1' } }), ok,
    { note: 'The reference mints and accepts a kid on a client attestation (the key of the client\'s JWKS); SPC-19 says an attestation has none.' })
  await add('client attestation for the alpha audience #atproto_space_host', 'clientAttestation', await mint('clientAttestation', { payload: (p) => { p.aud = `${AUTHORITY}#atproto_space_host` } }), no('BadJwtAudience'))

  write('space-tokens.json', {
    comment: 'Space delegation tokens, credentials and client attestations in the reference encoding, with the verdict owed at `now`; checked against the reference verifySpaceToken. A credential for a public space (no delegation token, SPC-62) is the same token.',
    cases,
  })
}

// ── KEM: DHKEM(P-256, HKDF-SHA256) (SPC-54, CRY-51) ──────
const kemSecrets = []
let kemKeyShare
{
  const realCreate = P256Keypair.create
  /** encapsulate() with its ephemeral key fixed. */
  const encapsulateWith = async (ephemeral, authorKey) => {
    P256Keypair.create = async () => ephemeral
    try { return await encapsulate(authorKey) } finally { P256Keypair.create = realCreate }
  }
  const p256 = (scalar) => P256Keypair.import(scalar)
  const uncompressed = (didKey) => hex(parseDidKey(didKey).keyBytes)
  const cases = []
  const add = async (name, authorScalar, ephemeralScalar, note, expected) => {
    const author = await p256(authorScalar)
    const ephemeral = await p256(ephemeralScalar)
    const { keyShare, sharedSecret } = await encapsulateWith(ephemeral, author.did())
    assert(keyShare === ephemeral.did(), `${name}: key share`)
    assert(hex(decapsulate(author, keyShare)) === hex(sharedSecret), `${name}: decapsulation`)
    if (expected) assert(hex(sharedSecret) === expected, `${name}: RFC 9180 shared secret`)
    kemSecrets.push(sharedSecret)
    kemKeyShare ??= keyShare
    cases.push({ name, note, authorPrivateKey: authorScalar, authorKey: author.did(), authorKeyUncompressed: uncompressed(author.did()),
      ephemeralPrivateKey: ephemeralScalar, keyShare, keyShareUncompressed: uncompressed(keyShare), sharedSecret: hex(sharedSecret) })
  }
  const AUTHOR = hex(sha256('atproto.net space vector kem author 1'))
  for (const i of [1, 2, 3])
    await add(`author 1, ephemeral ${i}`, AUTHOR, hex(sha256(`atproto.net space vector kem ephemeral ${i}`)), 'SPC-54/55: encapsulate with this ephemeral key gives keyShare and sharedSecret; decapsulate gives the same secret.')
  await add('author 2, ephemeral 1', hex(sha256('atproto.net space vector kem author 2')), hex(sha256('atproto.net space vector kem ephemeral 1')), 'SPC-54: the same key share under another author key gives another secret.')
  await add('RFC 9180 A.3.1', 'f3ce7fdae57e1a310d87f1ebbde6f328be0a99cdbcadf4d6589cf29de4b8ffd2', '4995788ef4b9d6132b249ce59a77281493eb39af373d236a1fe415cb0c2d7beb',
    'CRY-51: RFC 9180 §A.3.1 DHKEM(P-256, HKDF-SHA256): skRm is the author key, skEm the ephemeral key; shared_secret as published.', 'c0d26aeab536609a572b07695d933b589dcf363ff9d93c93adea537aeabb8cb8')

  const author = await p256(AUTHOR)
  const k256 = await Secp256k1Keypair.import(hex(sha256('atproto.net space vector key 3')))
  const didKey = (bytes) => `did:key:z${multibase(Uint8Array.from(bytes), 'base58btc')}`
  const offCurve = didKey([0x80, 0x24, 0x02, ...new Uint8Array(31), 0x01])
  const uncompressedShare = didKey([0x80, 0x24, ...parseDidKey(cases[0].keyShare).keyBytes])
  const ed25519 = didKey([0xed, 0x01, ...sha256('atproto.net space vector ed25519')])
  const refusals = []
  const refuse = async (name, operation, input, note) => {
    const reference = await attempt(() => operation === 'decapsulate' ? decapsulate(author, input) : encapsulate(input))
    assert(!reference.ok, `${name}: the reference accepts it`)
    refusals.push({ name, note, operation, ...(operation === 'decapsulate' ? { authorPrivateKey: AUTHOR, keyShare: input } : { authorKey: input }), expect: { ok: false }, referenceError: reference.error })
  }
  for (const [name, keyShare] of [['a did:plc', 'did:plc:ewvi7nxzyoun6zhxrhs64oiz'], ['did:key:nope', 'did:key:nope'], ['empty', ''], ['garbage', '!!!'],
    ['a K-256 did:key', k256.did()], ['an Ed25519 did:key', ed25519], ['a P-256 x-coordinate off the curve', offCurve], ['an uncompressed P-256 did:key', uncompressedShare]])
    await refuse(`key share ${name}`, 'decapsulate', keyShare, 'SPC-55/56: key_share is a compressed P-256 did:key; anything else is an invalid key (BadSpaceMac).')
  await refuse('author key that is K-256', 'encapsulate', k256.did(), 'SPC-54: #atproto_kem is a P-256 Multikey.')
  await refuse('author key that is not a did:key', 'encapsulate', 'did:plc:ewvi7nxzyoun6zhxrhs64oiz', 'SPC-54: #atproto_kem is a P-256 Multikey.')

  write('kem.json', {
    comment: 'DHKEM(P-256, HKDF-SHA256) from the reference kem.ts: encapsulation with a fixed ephemeral key (its did:key is the key_share), decapsulation with the author\'s #atproto_kem private key, and refused keys. Private keys are hex scalars; *Uncompressed are SEC1 points (0x04 ‖ x ‖ y).',
    cases,
    refusals,
  })
}

// ── Commit and record MACs and their headers (SPC-55…57, SPC-77) ──
{
  const secret = (n) => sha256(`atproto.net space vector shared secret ${n}`)
  const SPACE = 'at://did:example:space/space/app.bsky.group/test'
  const URI = 'at://did:example:alice/space/app.bsky.group/test/app.bsky.feed.post/1'
  const CID_A = 'bafyreidefdycgbfy3oglcb6ism3eqhyp5llsrpzxjsuac2gsy4mtrtx244'
  const CID_B = 'bafyreidpw4cbv6gr4ukh33z23pvvrpr3wi4gnpmi4doamlsl3sa4rgri2a'
  const commit = { ver: 1, space: SPACE, repo: 'did:example:alice', rev: '3kbcq3p7ad400', data: new Uint8Array(32).fill(1) }

  const commitMacs = []
  const addCommit = async (name, sharedSecret, c, note) => {
    const reference = await attempt(() => mac.computeCommitMac(sharedSecret, c))
    commitMacs.push({ name, note, sharedSecret: hex(sharedSecret), commit: { ...c, data: hex(c.data) }, mac: reference.ok ? hex(reference.value) : null, ...(reference.ok ? {} : { referenceError: reference.error }) })
  }
  await addCommit('reference test commit', secret(1), commit, 'SPC-56: HMAC-SHA256(secret, "atproto-space-repo-v1" ‖ len‖space ‖ len‖repo ‖ len‖rev ‖ data).')
  await addCommit('same commit, another secret', secret(2), commit, 'SPC-56.')
  await addCommit('field boundary: repo did:example:alice, rev 3kbc', secret(1), { ...commit, rev: '3kbc' }, 'SPC-56: the length prefixes keep these two apart.')
  await addCommit('field boundary: repo did:example:alice3, rev kbc', secret(1), { ...commit, repo: 'did:example:alice3', rev: 'kbc' }, 'SPC-56: the length prefixes keep these two apart.')
  await addCommit('data changed', secret(1), { ...commit, data: new Uint8Array(32).fill(2) }, 'SPC-56: data is covered.')
  await addCommit('empty repo', secret(1), { ...commit, data: new LtHash().digest() }, 'SPC-77: data of an empty repo is SHA-256 of the zero state.')
  await addCommit('secret from kem.json "author 1, ephemeral 1"', kemSecrets[0], { ...commit, space: 'at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default', repo: 'did:plc:z72i7hdynmk6r22z27h6tvur' }, 'SPC-54…56 end to end.')
  await addCommit('ver 2 has the MAC of ver 1', secret(1), { ...commit, ver: 2 }, 'SPC-56: the reference context is "atproto-space-repo-v1" whatever the commit\'s ver; ver itself is not MACed (verifyCommit refuses ver ≠ 1 before the MAC).')
  await addCommit('multi-byte UTF-8 fields', secret(1), { ...commit, space: 'at://did:example:späce/space/app.bsky.group/tëst', repo: 'did:example:ålice', rev: '3kbc✓' }, 'SPC-56: lengths count UTF-8 bytes, not characters or UTF-16 units (the fields are not valid syntax; the MAC does not validate).')
  await addCommit('a 300-byte space', secret(1), { ...commit, space: `${SPACE}${'x'.repeat(300 - SPACE.length)}` }, 'SPC-56: a length prefix above 255 uses both bytes.')
  await addCommit('a 65535-byte rev', secret(1), { ...commit, rev: 'r'.repeat(65535) }, 'SPC-56: the longest field a uint16 prefix encodes.')
  await addCommit('a 65536-byte rev', secret(1), { ...commit, rev: 'r'.repeat(65536) }, 'SPC-56: a field longer than a uint16 prefix encodes is refused.')

  const recordMacs = []
  const addRecord = (name, sharedSecret, uri, cid, note) => {
    const value = mac.computeRecordMac(sharedSecret, uri, parseCid(cid))
    recordMacs.push({ name, note, sharedSecret: hex(sharedSecret), uri, cid, cidBytes: hex(parseCid(cid).bytes), mac: hex(value) })
  }
  addRecord('reference test record', secret(1), URI, CID_A, 'SPC-56: HMAC-SHA256(secret, "atproto-space-record-v1" ‖ len‖uri ‖ binary CID).')
  addRecord('another secret', secret(2), URI, CID_A, 'SPC-56.')
  addRecord('another uri', secret(1), `${URI}2`, CID_A, 'SPC-56: the uri is covered.')
  addRecord('another cid', secret(1), URI, CID_B, 'SPC-56: the CID is covered.')
  addRecord('a raw-codec CID', secret(1), URI, 'bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku', 'SPC-56: the MAC covers the CID bytes as given.')
  addRecord('multi-byte UTF-8 uri', secret(1), 'at://did:example:ålice/space/app.bsky.group/tëst/app.bsky.feed.post/✓', CID_A, 'SPC-56: lengths count UTF-8 bytes.')
  addRecord('a 300-byte uri', secret(1), `${URI}${'x'.repeat(300 - URI.length)}`, CID_A, 'SPC-56: a length prefix above 255 uses both bytes.')

  const KEY_SHARE = kemKeyShare
  const MAC32 = Uint8Array.from({ length: 32 }, (_, i) => i)
  const formatted = []
  for (const type of [mac.COMMIT_MAC_TYPE, mac.RECORD_MAC_TYPE]) {
    formatted.push({ name: `want ${type}`, header: 'want', type, keyShare: KEY_SHARE, value: mac.formatWantMacHeader(type, KEY_SHARE) })
    formatted.push({ name: `mac ${type}`, header: 'mac', type, mac: hex(MAC32), value: mac.formatMacHeader(type, MAC32) })
  }
  formatted.push({ name: 'mac of a non-multiple-of-3 length', header: 'mac', type: mac.COMMIT_MAC_TYPE, mac: hex(MAC32.subarray(0, 31)), value: mac.formatMacHeader(mac.COMMIT_MAC_TYPE, MAC32.subarray(0, 31)) })

  const parsing = []
  const parse = async (name, header, value, type, note, { beyondReference } = {}) => {
    const reference = await attempt(() => header === 'want' ? mac.parseWantMacHeader(value ?? undefined, type) : mac.parseMacHeader(value ?? undefined, type))
    const verdict = reference.ok ? { ok: true, ...(header === 'want' ? { keyShare: reference.value } : { mac: hex(reference.value) }) } : { ok: false }
    if (beyondReference) assert(reference.ok, `${name}: the reference refuses it already`)
    parsing.push({ name, note, header, value, type, expect: beyondReference ? { ok: false } : verdict, ...(reference.ok ? {} : { referenceError: reference.error }),
      ...(beyondReference ? { beyondReference, reference: verdict } : {}) })
  }
  const repo = mac.COMMIT_MAC_TYPE, record = mac.RECORD_MAC_TYPE
  const want = (fields) => fields.replace('KS', KEY_SHARE)
  const B64 = Buffer.from(MAC32).toString('base64')
  const N = 'SPC-55/56'
  await parse('want: repo', 'want', mac.formatWantMacHeader(repo, KEY_SHARE), repo, N)
  await parse('want: record', 'want', mac.formatWantMacHeader(record, KEY_SHARE), record, N)
  await parse('want: members reordered, no spaces', 'want', want('key_share="KS",ver=1,type="atproto-space-repo"'), repo, N)
  await parse('want: unknown members ignored', 'want', want('type="atproto-space-repo", ver=1, key_share="KS", future=?1;x=1'), repo, `${N}: members other than type, ver and key_share are ignored.`)
  await parse('want: parameters on members ignored', 'want', want('type="atproto-space-repo";a=1, ver=1;b, key_share="KS";c="d"'), repo, `${N}: parameters are dropped.`)
  await parse('want: empty key_share', 'want', 'type="atproto-space-repo", ver=1, key_share=""', repo, `${N}: parsing accepts any string; decapsulation then refuses it (kem.json).`)
  await parse('want: repeated type, last wins', 'want', want('type="atproto-space-record", ver=1, key_share="KS", type="atproto-space-repo"'), repo, `${N}: RFC 8941 keeps the last of a repeated key.`)
  await parse('want: record header for a repo request', 'want', mac.formatWantMacHeader(record, KEY_SHARE), repo, `${N}: the type must be the one the endpoint expects.`)
  await parse('want: repo header for a record request', 'want', mac.formatWantMacHeader(repo, KEY_SHARE), record, N)
  await parse('want: unknown type', 'want', want('type="atproto-space-blob", ver=1, key_share="KS"'), repo, N)
  await parse('want: type as a token', 'want', want('type=atproto-space-repo, ver=1, key_share="KS"'), repo, `${N}: type is a string.`)
  await parse('want: without type', 'want', want('ver=1, key_share="KS"'), repo, N)
  await parse('want: ver 2', 'want', want('type="atproto-space-repo", ver=2, key_share="KS"'), repo, `${N}: unsupported version.`)
  await parse('want: ver 0', 'want', want('type="atproto-space-record", ver=0, key_share="KS"'), record, N)
  await parse('want: ver as a string', 'want', want('type="atproto-space-repo", ver="1", key_share="KS"'), repo, `${N}: ver is an integer.`)
  await parse('want: ver as a decimal', 'want', want('type="atproto-space-repo", ver=1.0, key_share="KS"'), repo,
    `${N}: ver is an integer; the reference (JavaScript numbers) cannot tell the decimal 1.0 from the integer 1.`, { beyondReference: true })
  await parse('want: ver as a boolean', 'want', want('type="atproto-space-repo", ver=?1, key_share="KS"'), repo, `${N}: ver is an integer.`)
  await parse('want: without ver', 'want', want('type="atproto-space-repo", key_share="KS"'), repo, N)
  await parse('want: without key_share', 'want', 'type="atproto-space-repo", ver=1', repo, N)
  await parse('want: key_share as a token', 'want', 'type="atproto-space-repo", ver=1, key_share=zDnae', repo, `${N}: key_share is a string.`)
  await parse('want: key_share as a byte sequence', 'want', 'type="atproto-space-repo", ver=1, key_share=:YWJj:', repo, `${N}: key_share is a string.`)
  await parse('want: key_share as an inner list', 'want', want('type="atproto-space-repo", ver=1, key_share=("KS")'), repo, `${N}: no member may be an inner list.`)
  await parse('want: an unknown inner-list member', 'want', want('type="atproto-space-repo", ver=1, key_share="KS", extra=(1 2)'), repo, `${N}: the reference refuses an inner list in any member.`)
  await parse('want: missing header', 'want', null, repo, `${N}: a missing header is BadSpaceMac (SPC-57).`)
  await parse('want: empty header', 'want', '', repo, N)
  for (const malformed of ['???', 'type=', 'type="atproto-space-repo",', 'type="atproto-space-repo" ver=1', 'Type="atproto-space-repo", ver=1, key_share="x"'])
    await parse(`want: malformed ${JSON.stringify(malformed)}`, 'want', malformed, repo, `${N}: not an RFC 8941 dictionary.`)

  await parse('mac: repo', 'mac', mac.formatMacHeader(repo, MAC32), repo, N)
  await parse('mac: record', 'mac', mac.formatMacHeader(record, MAC32), record, N)
  await parse('mac: members reordered', 'mac', `mac=:${B64}:, ver=1, type="atproto-space-repo"`, repo, N)
  await parse('mac: unpadded base64', 'mac', `type="atproto-space-repo", ver=1, mac=:${B64.replace(/=+$/, '')}:`, repo, `${N}: structured-headers 1.0.1 accepts an unpadded byte sequence.`)
  await parse('mac: 16 bytes', 'mac', mac.formatMacHeader(repo, MAC32.subarray(0, 16)), repo, `${N}: parsing accepts any length; verification then fails (constant-time comparison of unequal lengths).`)
  await parse('mac: empty', 'mac', 'type="atproto-space-repo", ver=1, mac=::', repo, `${N}: parsing accepts any length; verification then fails.`)
  await parse('mac: parameters on the mac ignored', 'mac', `type="atproto-space-repo", ver=1, mac=:${B64}:;x=1`, repo, N)
  await parse('mac: record header for a repo response', 'mac', mac.formatMacHeader(record, MAC32), repo, `${N}: the response type matches the request.`)
  await parse('mac: ver 2', 'mac', `type="atproto-space-repo", ver=2, mac=:${B64}:`, repo, N)
  await parse('mac: without mac', 'mac', 'type="atproto-space-repo", ver=1', repo, N)
  await parse('mac: mac as a string', 'mac', 'type="atproto-space-repo", ver=1, mac="abc"', repo, `${N}: mac is a byte sequence.`)
  await parse('mac: mac as an integer', 'mac', 'type="atproto-space-repo", ver=1, mac=1', repo, N)
  await parse('mac: mac as an inner list', 'mac', `type="atproto-space-repo", ver=1, mac=(:${B64}:)`, repo, N)
  await parse('mac: invalid base64', 'mac', 'type="atproto-space-repo", ver=1, mac=:!!!:', repo, N)
  await parse('mac: missing header', 'mac', null, repo, N)
  await parse('mac: malformed', 'mac', 'type="atproto-space-repo", ver=1, mac=:abc', repo, N)

  write('space-mac.json', {
    comment: 'Commit and record MACs (reference mac.ts computeCommitMac / computeRecordMac) with fixed shared secrets, and the Atproto-Space-Want-Mac / Atproto-Space-Mac headers (formatWantMacHeader, formatMacHeader, parseWantMacHeader, parseMacHeader). Commit data and MACs are hex; a null mac is a refusal. A null header value is an absent header.',
    headerNames: { want: mac.WANT_MAC_HEADER, mac: mac.MAC_HEADER },
    commitMacs,
    recordMacs,
    formatted,
    parsing,
  })
}

// ── STAR space repo exports (SPC-59, SPC-60) ─────────────
{
  const AUTHOR = 'did:plc:z72i7hdynmk6r22z27h6tvur'
  const SPACE = 'at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default'
  const REV = '3kbcq3p7ad400'
  const SECRET = sha256('atproto.net space vector star secret')
  const MAX_RECORD = 1024 * 1024
  const utf8 = (s) => new TextEncoder().encode(s)
  const concat = (parts) => Buffer.concat(parts.map((p) => Buffer.from(p)))
  const frame = (bytes) => concat([encodeVarInt(bytes.length), bytes])
  const compare = (a, b) => Buffer.compare(Buffer.from(a), Buffer.from(b))
  const collect = async (stream) => { const chunks = []; for await (const chunk of stream) chunks.push(chunk); return concat(chunks) }
  /** Hex, with runs of 64 or more equal bytes as {repeat, count} segments (Vectors.Hex). */
  const bytesField = (bytes) => {
    const segments = []
    let start = 0, i = 0
    while (i < bytes.length) {
      let j = i
      while (j < bytes.length && bytes[j] === bytes[i]) j++
      if (j - i >= 64) { if (i > start) segments.push(hex(bytes.subarray(start, i))); segments.push({ repeat: hex(bytes.subarray(i, i + 1)), count: j - i }); start = j }
      i = j
    }
    if (segments.length === 0) return hex(bytes)
    if (start < bytes.length) segments.push(hex(bytes.subarray(start)))
    return segments
  }
  /** A map in the given key order, text keys and CBOR-encoded values: for non-canonical encodings. */
  const rawMap = (entries) => concat([Uint8Array.of(0xa0 | entries.length), ...entries.flatMap(([k, v]) => [encode(k), v])])
  const metaOf = (commit) => ({ $type: 'atproto-space-repo', ...commit })
  /** Raw archive: magic, metadata frame, key/value frames. */
  const archive = (metaBytes, pairs) => concat([STAR_MAGIC, frame(metaBytes), ...pairs.flatMap(([k, v]) => [frame(k), frame(v)])])
  /** Records [collection, rkey, value], serialized by the reference, with their CIDs. */
  const serialize = async (records) => Promise.all(records.map(([collection, rkey, value]) => serializeRecord(collection, rkey, value)))
  const commitOf = (serialized, change = {}) => ({ ...RepoCommit.fromRecords(serialized).toCommit({ space: SPACE, repo: AUTHOR, rev: REV }), ...change })
  const pairsOf = (serialized) => serialized.map((r) => [utf8(`${r.collection}/${r.rkey}`), r.bytes])

  const cases = []
  const add = async (name, note, bytes, { request = { repo: AUTHOR, space: SPACE }, sharedSecret = SECRET, macFor, commit, expect, beyondReference } = {}) => {
    const macValue = mac.computeCommitMac(sharedSecret, macFor ?? commit)
    const reference = await attempt(() => verifyRepoStarFull([bytes], { ...request, sharedSecret: SECRET, mac: macValue }))
    const star = await attempt(async () => { const reader = await readStar([bytes]); let n = 0; for await (const _ of reader.pairs) n++; return n })
    const verdict = reference.ok
      ? { ok: true, commit: (({ $type, ...c }) => ({ ...c, data: hex(c.data) }))(reference.value.commit), records: reference.value.records.map((r) => ({ path: `${r.collection}/${r.rkey}`, cid: r.cid.toString() })) }
      : { ok: false }
    if (!beyondReference && expect !== undefined && expect !== verdict.ok) throw new Error(`${name}: the reference says ${JSON.stringify(reference)}`)
    cases.push({
      name, note, request, sharedSecret: hex(SECRET), mac: hex(macValue), archive: bytesField(bytes),
      expect: beyondReference ? { ok: false } : verdict,
      star: star.ok ? { ok: true, pairs: star.value } : { ok: false, error: star.error },
      ...(reference.ok ? {} : { referenceError: reference.error }),
      ...(beyondReference ? { beyondReference: true, reference: verdict } : {}),
    })
  }
  /** A well-formed export of these records (in the given order), written by the reference serializeRepo. */
  const exportOf = async (records, change = {}) => {
    const serialized = await serialize(records)
    const commit = commitOf(serialized, change)
    return { serialized, commit, bytes: await collect(serializeRepo(commit, serialized)) }
  }

  const note = (text) => ({ $type: 'com.example.note', text })
  const link = parseCid('bafyreidefdycgbfy3oglcb6ism3eqhyp5llsrpzxjsuac2gsy4mtrtx244')
  const three = [['com.example.note', '3kbcq3p7ad400', note('one')], ['com.example.note', '3kbcq3p7ad401', note('two')], ['com.example.note', '3kbcq3p7ad402', note('three')]]

  // Valid exports
  {
    const { commit, bytes } = await exportOf([])
    await add('empty repo', 'SPC-59/60: metadata only; data is the digest of the empty LtHash.', bytes, { commit, expect: true })
  }
  {
    const { commit, bytes } = await exportOf(three)
    await add('three records', 'SPC-59/60.', bytes, { commit, expect: true })
  }
  {
    // Byte order of "{collection}/{rkey}": "." (0x2e) sorts before "/" (0x2f), so a collection that
    // extends another sorts first; rkey punctuation and case sort by byte.
    const records = [
      ['com.example.a.b', 'x', note('collection extending com.example.a')],
      ['com.example.a', 'A', note('upper case')], ['com.example.a', 'a', note('lower')], ['com.example.a', 'a-b', note('-')], ['com.example.a', 'a.b', note('.')],
      ['com.example.a', 'a:b', note(':')], ['com.example.a', 'a_b', note('_')], ['com.example.a', 'a~b', note('~')], ['com.example.ab', 'a', note('ab')],
    ]
    const serialized = await serialize(records)
    serialized.sort((x, y) => compare(utf8(`${x.collection}/${x.rkey}`), utf8(`${y.collection}/${y.rkey}`)))
    const order = serialized.map((r) => `${r.collection}/${r.rkey}`)
    assert(order[0] === 'com.example.a.b/x' && order.at(-1) === 'com.example.ab/a', `ordering ${order}`)
    const commit = commitOf(serialized)
    await add('records needing byte ordering', 'SPC-59: keys in strictly increasing byte order: com.example.a.b/x precedes com.example.a/A ("." < "/"), and A < a < a-b < a.b < a:b < a_b < a~b.', await collect(serializeRepo(commit, serialized)), { commit, expect: true })
  }
  {
    const { commit, bytes } = await exportOf([['com.example.rich', 'self', { $type: 'com.example.rich', text: 'héllo ✓ 𝄞', n: -42, big: 9007199254740991, yes: true, nothing: null, list: [1, 'two', [3]], nested: { b: { a: 'z' } }, bytes: Uint8Array.of(0, 1, 2, 255), ref: link }]])
    await add('a record of every data type', 'SPC-17/59: text, integers, booleans, null, arrays, nested maps, bytes and a CID link, canonically encoded.', bytes, { commit, expect: true })
  }
  const sized = (length) => {
    const fixed = encode({ $type: 'com.example.big', text: 'a'.repeat(70000) }).length - 70000
    const record = { $type: 'com.example.big', text: 'a'.repeat(length - fixed) }
    assert(encode(record).length === length, 'record size')
    return record
  }
  {
    const { commit, bytes } = await exportOf([['com.example.big', 'max', sized(MAX_RECORD)]])
    await add('a record of exactly 1 MiB', 'SPC-59: a record value is at most 1 MiB (1,048,576 bytes).', bytes, { commit, expect: true })
  }
  {
    const { commit, bytes } = await exportOf(three, { rev: 'not-a-tid' })
    await add('rev that is not a TID', 'SPC-77 says rev is a TID; the reference metadata schema takes any string, and SPC-60 does not list rev among the checks.', bytes, { commit, expect: true })
  }
  {
    const serialized = await serialize(three)
    const commit = commitOf(serialized)
    await add('metadata with an extra field', 'SPC-59 lists the metadata fields; the reference ignores unknown ones (its schema strips them).', archive(encode({ ...metaOf(commit), extra: 'ignored' }), pairsOf(serialized)), { commit, expect: true })
  }

  // Commit and metadata refusals
  const base = await exportOf(three)
  const goodPairs = pairsOf(base.serialized)
  await add('MAC under another secret', 'SPC-60: a MAC mismatch rejects the archive.', base.bytes, { commit: base.commit, sharedSecret: sha256('another secret'), expect: false })
  await add('MAC over another rev', 'SPC-60.', base.bytes, { commit: base.commit, macFor: { ...base.commit, rev: '3kbcq3p7ad499' }, expect: false })
  await add('another repo requested', 'SPC-60/77: repo must equal the request.', base.bytes, { commit: base.commit, request: { repo: 'did:plc:ewvi7nxzyoun6zhxrhs64oiz', space: SPACE }, expect: false })
  await add('another space requested', 'SPC-60/77: space must equal the request.', base.bytes, { commit: base.commit, request: { repo: AUTHOR, space: `${SPACE}2` }, expect: false })
  {
    const commit = { ...base.commit, ver: 2 }
    await add('commit ver 2', 'SPC-60/77: ver is 1 (the MAC does not cover ver, so it matches).', archive(encode(metaOf(commit)), goodPairs), { commit, expect: false })
  }
  for (const [name, meta] of [
    ['metadata $type of another profile', { ...metaOf(base.commit), $type: 'atproto-space-records' }],
    ['metadata without $type', (({ $type, ...rest }) => rest)(metaOf(base.commit))],
    ['metadata without rev', (({ rev, ...rest }) => rest)(metaOf(base.commit))],
    ['metadata without data', (({ data, ...rest }) => rest)(metaOf(base.commit))],
    ['metadata data as a string', { ...metaOf(base.commit), data: hex(base.commit.data) }],
    ['metadata ver as a string', { ...metaOf(base.commit), ver: '1' }],
    ['metadata repo that is not a DID', { ...metaOf(base.commit), repo: 'alice.example.com' }],
    ['metadata space that is not a space reference', { ...metaOf(base.commit), space: 'at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/com.atmoboards.forum/default' }],
  ])
    await add(name, 'SPC-59/60: the metadata is $type "atproto-space-repo" plus the commit fields.', archive(encode(meta), goodPairs), { commit: base.commit, expect: false })
  {
    const meta = metaOf(base.commit)
    const unsorted = rawMap(Object.keys(meta).sort().map((k) => [k, encode(meta[k])]))
    await add('metadata with keys out of canonical order', 'SPC-59: the metadata is canonical DRISL.', archive(unsorted, goodPairs), { commit: base.commit, expect: false })
    await add('metadata with trailing bytes', 'SPC-59: the metadata frame holds exactly one item.', archive(concat([encode(meta), Uint8Array.of(0)]), goodPairs), { commit: base.commit, expect: false })
    await add('metadata that is an array', 'SPC-59.', archive(encode([meta]), goodPairs), { commit: base.commit, expect: false })
    await add('metadata that is not CBOR', 'SPC-59.', archive(Uint8Array.of(0xff), goodPairs), { commit: base.commit, expect: false })
  }

  // Framing refusals (STAR-lite v1)
  const metaBytes = encode(metaOf(base.commit))
  await add('empty input', 'SPC-59: STAR magic 2a 6c 01 00.', new Uint8Array(0), { commit: base.commit, expect: false })
  await add('wrong magic', 'SPC-59.', concat([Uint8Array.of(0x3a), base.bytes.subarray(1)]), { commit: base.commit, expect: false })
  await add('another STAR version', 'SPC-59: version byte 01.', concat([Uint8Array.of(0x2a, 0x6c, 0x02, 0x00), base.bytes.subarray(4)]), { commit: base.commit, expect: false })
  await add('magic only', 'SPC-59: the metadata is required.', STAR_MAGIC, { commit: base.commit, expect: false })
  await add('metadata length padded with a zero byte', 'SPC-59: varints are in shortest form.', concat([STAR_MAGIC, Uint8Array.of(0x80 | (metaBytes.length & 0x7f), metaBytes.length >> 7 | 0x80, 0x00), metaBytes]), { commit: base.commit, expect: false })
  await add('varint longer than 6 bytes', 'SPC-59: the reference reads at most 6 varint bytes.', concat([STAR_MAGIC, Uint8Array.of(0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01)]), { commit: base.commit, expect: false })
  await add('metadata length over 64 MiB', 'SPC-59/60: the length is refused before reading (the frame is not present).', concat([STAR_MAGIC, encodeVarInt(64 * 1024 * 1024 + 1)]), { commit: base.commit, expect: false })
  await add('metadata truncated', 'SPC-60.', concat([STAR_MAGIC, encodeVarInt(metaBytes.length), metaBytes.subarray(0, 10)]), { commit: base.commit, expect: false })
  {
    const head = concat([STAR_MAGIC, frame(metaBytes)])
    const [key, value] = goodPairs[0]
    await add('EOF inside a key length', 'SPC-60: EOF is allowed only at a pair boundary.', concat([head, Uint8Array.of(0x80)]), { commit: base.commit, expect: false })
    await add('EOF inside a key', 'SPC-60.', concat([head, encodeVarInt(key.length), key.subarray(0, 5)]), { commit: base.commit, expect: false })
    await add('key without a value', 'SPC-60.', concat([head, frame(key)]), { commit: base.commit, expect: false })
    await add('EOF inside a value', 'SPC-60.', concat([head, frame(key), encodeVarInt(value.length), value.subarray(0, 3)]), { commit: base.commit, expect: false })
    await add('key length padded with a zero byte', 'SPC-59: varints are in shortest form.', concat([head, Uint8Array.of(key.length | 0x80, 0x00), key, frame(value)]), { commit: base.commit, expect: false })
  }

  // Record refusals
  {
    const pairs = [goodPairs[1], goodPairs[0], goodPairs[2]]
    await add('records out of order', 'SPC-59/60: keys strictly increase.', archive(metaBytes, pairs), { commit: base.commit, expect: false })
    const dup = await serialize([three[0], three[0], three[1], three[2]])
    const commit = commitOf(dup)
    await add('duplicate key', 'SPC-59/60: no duplicate keys (the commit counts the record twice, so only the key order fails).', archive(encode(metaOf(commit)), pairsOf(dup)), { commit, expect: false })
  }
  await add('a record missing', 'SPC-60: the digest of the records does not match data.', archive(metaBytes, goodPairs.slice(0, 2)), { commit: base.commit, expect: false })
  {
    const extra = await serialize([['com.example.note', '3kbcq3p7ad403', note('four')]])
    await add('an extra record', 'SPC-60: digest mismatch.', archive(metaBytes, [...goodPairs, ...pairsOf(extra)]), { commit: base.commit, expect: false })
    await add('a record changed', 'SPC-60: digest mismatch.', archive(metaBytes, [goodPairs[0], goodPairs[1], [goodPairs[2][0], encode(note('THREE'))]]), { commit: base.commit, expect: false })
    await add('a record under another key', 'SPC-60: the key is part of the LtHash element.', archive(metaBytes, [goodPairs[0], goodPairs[1], [utf8('com.example.note/3kbcq3p7ad409'), goodPairs[2][1]]]), { commit: base.commit, expect: false })
  }
  /** One record with this key and value bytes, under a commit that matches it (so only the record is at fault). */
  const single = async (name, noteText, key, value, extra = {}) => {
    let commit = base.commit
    const path = new TextDecoder().decode(key)
    const slash = path.indexOf('/')
    if (slash > 0) commit = { ...base.commit, data: new RepoCommit().add(path.slice(0, slash), path.slice(slash + 1), await cidForCbor(value)).setHash.digest() }
    await add(name, noteText, archive(encode(metaOf(commit)), [[key, value]]), { commit, ...extra })
  }
  const okValue = encode(note('one'))
  for (const [name, path] of [['empty key', ''], ['key without a slash', 'com.example.note'], ['key with two slashes', 'com.example.note/a/b'],
    ['key whose collection is not an NSID', 'note/a'], ['key whose rkey is not a record key', 'com.example.note/..'], ['key that is not UTF-8', null]])
    await single(name, 'SPC-59/60: each key is a record path {collection}/{rkey}.', path === null ? Uint8Array.of(0x63, 0x2f, 0xff) : utf8(path), okValue, { expect: false })
  await single('record that is an array', 'SPC-17/60: a record is a map.', utf8('com.example.note/a'), encode(['one']), { expect: false })
  await single('record that is text', 'SPC-17/60.', utf8('com.example.note/a'), encode('one'), { expect: false })
  await single('record that is not CBOR', 'SPC-17/60.', utf8('com.example.note/a'), Uint8Array.of(0xff), { expect: false })
  await single('record with trailing bytes', 'SPC-17/60.', utf8('com.example.note/a'), concat([okValue, Uint8Array.of(0)]), { expect: false })
  await single('record with a repeated key', 'SPC-17/60.', utf8('com.example.note/a'), rawMap([['text', encode('a')], ['text', encode('b')]]), { expect: false })
  await single('record with a non-minimal integer', 'SPC-17/60.', utf8('com.example.note/a'), rawMap([['n', Uint8Array.of(0x18, 0x01)]]), { expect: false })
  await single('record with a tag other than 42', 'SPC-17/60.', utf8('com.example.note/a'), rawMap([['t', Uint8Array.of(0xc1, 0x01)]]), { expect: false })
  await single('record with an integer key', 'SPC-17/60: text keys only.', utf8('com.example.note/a'), Uint8Array.of(0xa1, 0x01, 0x01), { expect: false })
  const strict = { beyondReference: true }
  // DRISL orders keys shorter first, so "a" belongs before "text".
  await single('record with keys out of canonical order', 'SPC-17/60 require canonical encoding; the reference decodes it, hashes the bytes as sent and accepts it.', utf8('com.example.note/a'),
    rawMap([['text', encode('one')], ['a', encode('b')]]), strict)
  await single('record with a float', 'SPC-17/60 forbid floats; the reference decoder accepts them.', utf8('com.example.note/a'), rawMap([['n', Uint8Array.of(0xfb, 0x3f, 0xf8, 0, 0, 0, 0, 0, 0)]]), strict)
  await single('record with undefined', 'SPC-17/60: undefined is not DRISL; the reference decoder reads it as null.', utf8('com.example.note/a'), rawMap([['u', Uint8Array.of(0xf7)]]), strict)
  {
    const serialized = await serialize([['com.example.big', 'over', sized(MAX_RECORD + 1)]])
    const commit = commitOf(serialized)
    await add('a record of 1 MiB + 1 byte', 'SPC-59/60: a record value is at most 1 MiB; the length is refused before the value is read.', archive(encode(metaOf(commit)), pairsOf(serialized)), { commit, expect: false })
  }

  write('star.json', {
    comment: 'Space repo exports (STAR-lite v1, profile atproto-space-repo): archives the reference serializeRepo writes, and invalid ones, with the verdict of the reference verifyRepoStarFull(archive, {repo, space, sharedSecret, mac}). `archive` is hex, or segments for Vectors.Hex. `star` is the bare STAR layer verdict (reference readStar, pairs drained, no profile checks).',
    profile: { magic: hex(STAR_MAGIC), metaType: 'atproto-space-repo', maxRecordBytes: MAX_RECORD },
    cases,
  })
}
