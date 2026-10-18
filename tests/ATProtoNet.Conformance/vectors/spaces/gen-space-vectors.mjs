// Writes the Spaces vectors (lthash.json, http-signatures.json, structured-fields.json,
// space-tokens.json, blake3-long-output.json) from the reference implementation pinned in
// MANIFEST.md: bluesky-social/atproto `packages/space/src` (lthash.ts, http-signature.ts,
// credential.ts, error.ts), run as TypeScript, with @atproto/crypto 0.5.5, @atproto/lex-data 0.1.8,
// @atproto/syntax, @noble/hashes 1.8.0 and structured-headers 1.0.1 installed:
//   node --experimental-transform-types gen-space-vectors.mjs <packages/space/src> <out dir>
// The reference sources are copied next to the working directory's node_modules with their
// relative `.js` imports pointed at the `.ts` files. Every case carries the reference verdict
// (`reference`); a case whose expectation goes beyond the reference says so (`beyondReference`).
// Deterministic: fixed keys, claims and clock; RFC 6979 signatures.
import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { P256Keypair, Secp256k1Keypair } from '@atproto/crypto'
import { blake3 } from '@noble/hashes/blake3'
import { parseDictionary, serializeInnerList, serializeItem, isInnerList } from 'structured-headers'

const [source, out] = process.argv.slice(2)
const copy = resolve('.space-ref')
mkdirSync(copy, { recursive: true })
for (const file of readdirSync(source).filter((f) => f.endsWith('.ts') && !f.endsWith('.test.ts')))
  writeFileSync(join(copy, file), readFileSync(join(source, file), 'utf8').replace(/(from '\.\/[\w-]+)\.js'/g, "$1.ts'"))
const { LtHash } = await import(join(copy, 'lthash.ts'))
const { createSpaceSig, verifySpaceSignature } = await import(join(copy, 'http-signature.ts'))
const { verifySpaceToken } = await import(join(copy, 'credential.ts'))

const sha256 = (data) => createHash('sha256').update(data).digest()
const hex = (bytes) => Buffer.from(bytes).toString('hex')
const write = (name, value) => writeFileSync(join(out, name), JSON.stringify(value, null, 2) + '\n')

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

// ── HTTP message signatures ──────────────────────────────
{
  const key = await P256Keypair.import(hex(sha256('atproto.net space vector key 1')))
  const other = await P256Keypair.import(hex(sha256('atproto.net space vector key 2')))
  const k256 = await Secp256k1Keypair.import(hex(sha256('atproto.net space vector key 3')))
  const KEY = key.did()
  const credential = 'eyJhbGciOiJFUzI1NiIsInR5cCI6ImF0cHJvdG8tc3BhY2UtY3JlZGVudGlhbCtqd3QifQ.eyJzdWIiOiJhdDovL2RpZDpwbGM6YXV0aG9yaXR5L3NwYWNlL2NvbS5leGFtcGxlLmZvcnVtL2RlZmF1bHQifQ.c2ln'
  const delegation = 'eyJhbGciOiJFUzI1NiIsInR5cCI6ImF0cHJvdG8tc3BhY2UtZGVsZWdhdGlvbitqd3QifQ.eyJhdWQiOiJkaWQ6cGxjOmF1dGhvcml0eSNhdHByb3RvX3NwYWNlX2hvc3QifQ.c2ln'
  const USE = `Atproto-Space ${credential}`
  const EXCHANGE = `Bearer ${delegation}`
  const AUDIENCE = 'did:plc:repoowner'
  const N = 0xffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551n

  const b64 = (bytes) => Buffer.from(bytes).toString('base64')
  const base = (authorization, input, audience) =>
    [`"authorization": ${authorization}`, ...(audience ? [`"atproto-space-audience": ${audience}`] : []), `"@signature-params": ${input}`].join('\n')
  const signBase = async (signer, authorization, input, audience) => b64(await signer.sign(Buffer.from(base(authorization, serializeInnerList(parseDictionary(`x=${input}`).get('x')), audience))))
  const label = (sig) => `atproto-space=:${sig}:`

  const use = await createSpaceSig(key, { authorization: USE, audience: AUDIENCE })
  const exchange = await createSpaceSig(key, { authorization: EXCHANGE })
  const useInput = `atproto-space=${use.signatureInput}`
  const useSig = label(b64(use.signature))
  const exchangeInput = `atproto-space=${exchange.signatureInput}`
  const exchangeSig = label(b64(exchange.signature))
  // 0.8 pinned these against the reference at 679724ad; the pinned reference still signs them.
  if (useSig !== 'atproto-space=:GVrrfzolwFMAI0SLxkSfOq71Ep4SkUNaLTAZ9SRp1pML+wBKwoVmjxRkNYF9idRAr51SihpapFxUHlgmRFWzkw==:') throw new Error('use signature')
  if (exchangeSig !== 'atproto-space=:CLjYMdudKnilQaMTbySLMwviYwKs2S93frm4q29MIEckBviUGogVBZ4SogYQBBbHl9ycIkR/WxaiEyn34TW9Xg==:') throw new Error('exchange signature')

  const raw = Buffer.from(use.signature)
  const highS = Buffer.concat([raw.subarray(0, 32), Buffer.from((N - BigInt('0x' + hex(raw.subarray(32)))).toString(16).padStart(64, '0'), 'hex')])
  const der = (() => {
    const int = (v) => { let t = v; while (t.length > 1 && t[0] === 0) t = t.subarray(1); if (t[0] & 0x80) t = Buffer.concat([Buffer.from([0]), t]); return Buffer.concat([Buffer.from([2, t.length]), t]) }
    const body = Buffer.concat([int(raw.subarray(0, 32)), int(raw.subarray(32))])
    return Buffer.concat([Buffer.from([0x30, body.length]), body])
  })()

  const cases = []
  const add = async (name, mode, c, expect, extra = {}) => {
    const headers = { authorization: c.authorization, 'signature-input': c.signatureInput, signature: c.signature, ...(mode === 'request' ? { 'atproto-space-audience': c.audience } : {}) }
    let reference
    try { reference = { ok: true, key: await verifySpaceSignature(headers, mode === 'request' ? c.keyId : undefined) } } catch { reference = { ok: false } }
    if (!extra.beyondReference && reference.ok !== expect.ok) throw new Error(`${name}: the reference says ${JSON.stringify(reference)}`)
    cases.push({ name, mode, ...c, expect, reference: reference.ok, ...extra })
  }
  const request = (change = {}) => ({ authorization: USE, audience: AUDIENCE, signatureInput: useInput, signature: useSig, keyId: KEY, ...change })
  const exch = (change = {}) => ({ authorization: EXCHANGE, signatureInput: exchangeInput, signature: exchangeSig, ...change })
  const ok = { ok: true, key: KEY }
  const no = { ok: false }

  await add('reference request signature', 'request', request(), ok)
  await add('reference exchange signature', 'exchange', exch(), ok)
  await add('high-S request signature', 'request', request({ signature: label(b64(highS)) }), ok)
  {
    const input = `atproto-space=("authorization" "atproto-space-audience");alg="ecdsa-p256-sha256";keyid="${KEY}"`
    await add('optional parameters and other labels', 'request', request({
      signatureInput: `other=("authorization");keyid="other", ${input}`,
      signature: `other=:YWJj:, ${label(await signBase(key, USE, input.slice('atproto-space='.length), AUDIENCE))}`,
    }), ok)
  }
  await add('input spelled loosely, signed canonically', 'request', request({
    signatureInput: `atproto-space=(  "authorization"   "atproto-space-audience"  );  keyid="${KEY}";alg="ecdsa-p256-sha256"`,
    signature: label(await signBase(key, USE, `("authorization" "atproto-space-audience");keyid="${KEY}";alg="ecdsa-p256-sha256"`, AUDIENCE)),
  }), ok)
  {
    const input = `("authorization");keyid="${KEY}";created=1618884473;nonce="a\\"b\\\\c";tag=tok;flag;b=:YWJj:;d=1.50`
    await add('every parameter type, signed in canonical form', 'exchange', exch({ signatureInput: `atproto-space=${input}`, signature: label(await signBase(key, EXCHANGE, input)) }), ok)
  }
  await add('the same signature again', 'request', request(), ok)

  await add('changed authorization', 'request', request({ authorization: USE + '-changed' }), no)
  await add('another credential', 'request', request({ authorization: 'Atproto-Space another.credential.jwt' }), no)
  await add('changed audience', 'request', request({ audience: 'did:plc:someoneelse' }), no)
  await add('another signing key', 'request', request({ keyId: other.did() }), no)
  await add('keyid differing from the credential key', 'request', request({ signatureInput: `${useInput};keyid="${other.did()}"` }), no)
  for (const parameter of [';keyid=1', ';keyid', ';keyid=tok'])
    await add(`keyid ${parameter.slice(1)} that is not a string`, 'request', request({ signatureInput: useInput + parameter }), no)
  await add('a signature parameter added after signing', 'request', request({ signatureInput: `${useInput};alg="ecdsa-p256-sha256"` }), no)
  await add('audience not covered by the signature', 'request', request({
    signatureInput: `atproto-space=("authorization");keyid="${KEY}"`, signature: label(await signBase(key, USE, `("authorization");keyid="${KEY}"`)),
  }), no)
  for (const components of ['("atproto-space-audience")', '("atproto-space-audience" "authorization")', '("authorization" "atproto-space-audience" "content-type")',
    '("authorization" "authorization" "atproto-space-audience")', '("authorization";sf "atproto-space-audience")', '(authorization "atproto-space-audience")',
    '("Authorization" "atproto-space-audience")', '("@method" "atproto-space-audience")', '()'])
    await add(`components ${components}`, 'request', request({ signatureInput: `atproto-space=${components}` }), no)
  for (const input of ['not-a-list', '', 'atproto-space', 'atproto-space=1', 'atproto-space="authorization"', 'other=("authorization" "atproto-space-audience")'])
    await add(`signature input ${JSON.stringify(input)}`, 'request', request({ signatureInput: input }), no)
  for (const signature of ['', 'not-a-byte-sequence', 'atproto-space=not-a-byte-sequence', 'atproto-space=:YWJj:', 'atproto-space=:!!!:', 'atproto-space=1',
    'other=:YWJj:', 'atproto-space=:YWJj:;x=1', 'atproto-space=(:YWJj:)'])
    await add(`signature ${JSON.stringify(signature)}`, 'request', request({ signature }), no)
  await add('DER-encoded signature', 'request', request({ signature: label(b64(der)) }), no)
  await add('signature of zeros', 'request', request({ signature: label(b64(new Uint8Array(64))) }), no)
  await add('signature of ones', 'request', request({ signature: label(b64(new Uint8Array(64).fill(0xff))) }), no)
  for (const alg of ['ecdsa-p384-sha384', 'ed25519', 'rsa-pss-sha512'])
    await add(`algorithm ${alg}`, 'request', request({ signatureInput: `${useInput};alg="${alg}"` }), no)
  await add('algorithm as a token', 'request', request({ signatureInput: `${useInput};alg=ecdsa-p256-sha256` }), no)
  await add('request key that is not P-256', 'request', request({ keyId: k256.did() }), no)
  await add('empty request authorization', 'request', request({ authorization: '' }), no)
  await add('label repeated in the signature input', 'request', request({ signatureInput: `${useInput}, ${useInput}` }), no, { beyondReference: true })
  await add('label repeated in the signature', 'request', request({ signature: `${useSig}, ${useSig}` }), no, { beyondReference: true })

  await add('credential signature on the exchange', 'exchange', exch({ authorization: USE, signatureInput: useInput, signature: useSig }), no)
  await add('exchange algorithm ecdsa-p384-sha384', 'exchange', exch({ signatureInput: `${exchangeInput};alg="ecdsa-p384-sha384"` }), no)
  await add('exchange key that is not P-256', 'exchange', exch({ signatureInput: exchangeInput.replace(KEY, k256.did()) }), no)
  for (const parameters of ['', ';keyid', ';keyid=123', ';keyid="not-a-key"', ';keyid="did:key:z"', ';keyid="did:web:example.com"'])
    await add(`exchange keyid ${JSON.stringify(parameters)}`, 'exchange', exch({ signatureInput: `atproto-space=("authorization")${parameters}` }), no)
  await add('exchange signed by a key other than its keyid', 'exchange', exch({ signatureInput: exchangeInput.replace(KEY, other.did()) }), no)
  await add('changed delegation token', 'exchange', exch({ authorization: EXCHANGE + 'x' }), no)
  await add('empty exchange authorization', 'exchange', exch({ authorization: '' }), no)

  const signing = [
    { name: 'request', curve: 'P-256', authorization: USE, audience: AUDIENCE, signatureInput: useInput, base: base(USE, use.signatureInput, AUDIENCE) },
    { name: 'exchange', curve: 'P-256', authorization: EXCHANGE, signatureInput: exchangeInput, base: base(EXCHANGE, exchange.signatureInput) },
    { name: 'request with a K-256 key', curve: 'K-256', authorization: USE, audience: AUDIENCE, signatureInput: null },
    { name: 'exchange with a K-256 key', curve: 'K-256', authorization: EXCHANGE, signatureInput: null },
  ]
  write('http-signatures.json', {
    comment: 'RFC 9421 space request signatures and the verdicts owed, checked against the reference verifySpaceSignature.',
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

// ── Space tokens ─────────────────────────────────────────
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
  const HOST = `${AUTHORITY}#atproto_space_host`
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
  const add = async (name, type, token, expect, { key, aud, sub, at = 0, beyondReference } = {}) => {
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
    cases.push({ name, type, token, signingKey, audience: audience ?? null, subject: subject ?? null, now: NOW + at, expect, ...(beyondReference ? { beyondReference, reference } : {}) })
  }
  const ok = { ok: true }
  const no = (error) => ({ ok: false, error })

  await add('delegation token', 'delegation', await mint('delegation'), ok)
  await add('delegation token with the wrong key', 'delegation', await mint('delegation'), no('BadJwtSignature'), { key: intruder.did() })
  await add('delegation token for another audience', 'delegation', await mint('delegation'), no('BadJwtAudience'), { aud: `${USER}#atproto_space_host` })
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
  for (const malformed of ['onlyonepart', 'a.b.c.d', '!!!.!!!.!!!'])
    await add(`malformed token ${malformed}`, 'delegation', malformed, no('BadJwt'))
  await add('token whose header has no typ', 'delegation', 'e30.WzFd.AAAA', no('BadJwtType'))
  await add('delegation token expiring in the last representable second', 'delegation', await mint('delegation', { payload: (p) => { p.exp = 253402300799 } }), ok)
  for (const [claim, seconds] of [['exp', '253402300800'], ['exp', '9223372036854775807'], ['exp', '-9223372036854775808'], ['iat', '253402300800'], ['iat', '-62135596801']])
    await add(`delegation token ${claim} ${seconds} outside the representable range`, 'delegation',
      await mint('delegation', { payload: (p) => { p[claim] = `@@${seconds}@@` } }), no('BadJwt'), { beyondReference: true })
  for (const jti of [' ', '\t\t', 'a\u0000b'])
    await add(`delegation token jti ${JSON.stringify(jti)} that cannot be spent`, 'delegation', await mint('delegation', { payload: (p) => { p.jti = jti } }), no('BadJwt'), { beyondReference: true })
  await add('delegation token with an overlong jti', 'delegation', await mint('delegation', { payload: (p) => { p.jti = 'a'.repeat(256) } }), no('BadJwt'), { beyondReference: true })

  await add('credential', 'credential', await mint('credential'), ok)
  await add('credential bound by jkt', 'credential', await mint('credential', { payload: (p) => { p.cnf = { jkt: 'thumbprint' } } }), no('BadJwtCnf'))
  await add('credential bound by another member', 'credential', await mint('credential', { payload: (p) => { p.cnf = { other: holder.did() } } }), no('BadJwtCnf'))
  await add('credential without a binding', 'credential', await mint('credential', { payload: (p) => { delete p.cnf } }), no('BadJwtCnf'))
  await add('credential with an empty jti', 'credential', await mint('credential', { payload: (p) => { p.jti = '' } }), no('BadJwt'))
  await add('credential with a blank jti', 'credential', await mint('credential', { payload: (p) => { p.jti = ' ' } }), no('BadJwt'), { beyondReference: true })
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

  write('space-tokens.json', {
    comment: 'Space delegation tokens, credentials and client attestations in the reference encoding, with the verdict owed at `now`; checked against the reference verifySpaceToken.',
    cases,
  })
}
