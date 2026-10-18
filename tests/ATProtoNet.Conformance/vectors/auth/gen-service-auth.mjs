// Writes service-auth.json: inter-service auth tokens minted as @atproto/xrpc-server's
// createServiceJwt does (compact JWS, @atproto/crypto signatures), each rewritten to break one rule,
// with the verdict a receiver owes it: 0.8's ServiceAuthVerifierTests as data, at a fixed clock.
// Run with @atproto/crypto 0.5.5 installed: `node gen-service-auth.mjs > service-auth.json`.
// Deterministic: fixed keys, claims and clock; RFC 6979 signatures.
import { createHash } from 'node:crypto'
import { P256Keypair, Secp256k1Keypair } from '@atproto/crypto'

const seed = (text) => createHash('sha256').update(text).digest('hex')
const caller = await Secp256k1Keypair.import(seed('ATProto.NET service auth caller'))
const p256Caller = await P256Keypair.import(seed('ATProto.NET service auth P-256 caller'))
const labelKey = await P256Keypair.import(seed('ATProto.NET service auth label key'))
const attacker = await Secp256k1Keypair.import(seed('ATProto.NET service auth attacker'))
const rotated = await Secp256k1Keypair.import(seed('ATProto.NET service auth rotated key'))

const CALLER = 'did:plc:callercallercallercaller'
const P256_CALLER = 'did:plc:p256p256p256p256p256p256'
const AUDIENCE = 'did:web:feed.example.com#bsky_fg'
const METHOD = 'app.bsky.feed.getFeedSkeleton'
const NOW = 1767225600 // 2026-01-01T00:00:00Z

const method = (did, fragment, key) => ({ id: `${did}${fragment}`, type: 'Multikey', controller: did, publicKeyMultibase: key.did().slice(8) })
const doc = (did, ...methods) => ({ id: did, verificationMethod: methods })
const documents = {
  [CALLER]: doc(CALLER, method(CALLER, '#atproto', caller)),
  [P256_CALLER]: doc(P256_CALLER, method(P256_CALLER, '#atproto', p256Caller)),
}

const b64url = (bytes) => Buffer.from(bytes).toString('base64url')
// "@@<digits>@@" writes an integer JSON number that a double cannot hold.
const json = (value) => b64url(Buffer.from(JSON.stringify(value).replace(/"@@(-?\d+)@@"/g, '$1')))
let serial = 0

/** A token as createServiceJwt mints it, after `payload` and `header` rewrite any field. */
const token = async ({ payload, header, signer = caller } = {}) => {
  const claims = { iat: NOW, iss: CALLER, aud: AUDIENCE, exp: NOW + 60, lxm: METHOD, jti: `jti${String(++serial).padStart(4, '0')}` }
  payload?.(claims)
  const fields = { typ: 'JWT', alg: signer.jwtAlg }
  header?.(fields)
  const input = `${json(fields)}.${json(claims)}`
  return `${input}.${b64url(await signer.sign(Buffer.from(input)))}`
}

const K256_ORDER = 0xfffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141n
const withS = (jwt, s) => {
  const [h, p, sig] = jwt.split('.')
  const bytes = Buffer.from(sig, 'base64url')
  return `${h}.${p}.${b64url(Buffer.concat([bytes.subarray(0, 32), Buffer.from(s.toString(16).padStart(64, '0'), 'hex')]))}`
}
const sOf = (jwt) => BigInt('0x' + Buffer.from(jwt.split('.')[2], 'base64url').subarray(32).toString('hex'))

const ok = (extra = {}) => ({ ok: true, issuer: CALLER, audience: AUDIENCE, method: METHOD, keyId: '#atproto', ...extra })
const refused = (error) => ({ ok: false, error })
const cases = []
const add = (name, spec, steps, options) => cases.push({ name, spec, ...(options ? { options } : {}), steps })
const one = async (name, spec, make, expect, options) => add(name, spec, [{ token: await make(), expect }], options)

// ── Accepted ─────────────────────────────────────────────
await one('valid token', 'AUT-66', () => token({ payload: (p) => { p.jti = '0123456789abcdef0123456789abcdef' } }),
  ok({ tokenId: '0123456789abcdef0123456789abcdef', expiresAt: NOW + 60 }))
await one('P-256 caller', 'AUT-66', () => token({ signer: p256Caller, payload: (p) => { p.iss = P256_CALLER } }), ok({ issuer: P256_CALLER }))
await one('bare DID audience when accepted', 'AUT-66', () => token({ payload: (p) => { p.aud = 'did:web:feed.example.com' } }),
  ok({ audience: 'did:web:feed.example.com' }), { audiences: [AUDIENCE, 'did:web:feed.example.com'] })
{
  const genuine = await token()
  add('high-S signature', 'AUT-67', [{ token: withS(genuine, K256_ORDER - sOf(genuine)), expect: ok() }])
}
await one('no method to bind', 'AUT-66', () => token(), ok(), { method: null })
await one('within the clock skew', 'AUT-66', () => token({ payload: (p) => { p.iat = NOW + 20; p.exp = NOW - 20 } }), ok())
for (const typ of ['JWT', 'jwt', null])
  await one(`typ ${typ ?? 'absent'}`, 'AUT-66', () => token({ header: (h) => { if (typ === null) delete h.typ; else h.typ = typ } }), ok())
await one('issued as long ago as the ceiling allows', 'AUT-66', () => token({ payload: (p) => { p.iat = NOW - 330 } }), ok())
await one('not before within the skew', 'AUT-66', () => token({ payload: (p) => { p.nbf = NOW + 20 } }), ok())
await one('long-lived token with a raised ceiling', 'AUT-66', () => token({ payload: (p) => { p.exp = NOW + 59 * 60 } }), ok(),
  { maxLifetimeSeconds: 3600 })

// ── Structure and header ─────────────────────────────────
for (const malformed of ['', 'a.b', 'not-a-jwt', 'e30.e30.!'])
  add(`malformed "${malformed}"`, 'AUT-73', [{ token: malformed, expect: refused('BadJwt') }])
for (const alg of ['none', 'HS256', 'RS256'])
  await one(`alg ${alg}`, 'AUT-66', () => token({ header: (h) => { h.alg = alg } }), refused('BadJwt'))
await one('alg for the other curve', 'AUT-66', () => token({ header: (h) => { h.alg = 'ES256' } }), refused('BadJwtSignature'))
for (const typ of ['at+jwt', 'refresh+jwt', 'dpop+jwt'])
  await one(`typ ${typ}`, 'AUT-66', () => token({ header: (h) => { h.typ = typ } }), refused('BadJwtType'))
for (const typ of ['atproto-space-delegation+jwt', 'atproto-client-attestation+jwt', 'AT+JWT', 'application/example+jwt'])
  await one(`typ ${typ} with no method to bind`, 'AUT-66', () => token({ header: (h) => { h.typ = typ } }), refused('BadJwtType'), { method: null })
await one('typ that is not a string', 'AUT-66', () => token({ header: (h) => { h.typ = 1 } }), refused('BadJwtType'))

// ── kid ──────────────────────────────────────────────────
for (const kid of ['#atproto', `${CALLER}#atproto`])
  await one(`kid ${kid}`, 'AUT-66', () => token({ header: (h) => { h.kid = kid } }), ok())
for (const kid of ['#atproto_label', 'atproto', 'did:plc:someoneelsesomeoneelsesom#atproto'])
  await one(`kid ${kid} not allowed`, 'AUT-66', () => token({ header: (h) => { h.kid = kid } }), refused('BadJwt'))
await one('kid that is not a string', 'AUT-66', () => token({ header: (h) => { h.kid = 1 } }), refused('BadJwt'))
{
  const options = { allowedKeyIds: ['#atproto', '#atproto_label'], documents: { [CALLER]: doc(CALLER, method(CALLER, '#atproto', caller), method(CALLER, '#atproto_label', labelKey)) } }
  await one('allowed kid for another key', 'AUT-66', () => token({ signer: labelKey, header: (h) => { h.kid = '#atproto_label' } }),
    ok({ keyId: '#atproto_label' }), options)
  await one('allowed kid signed by the account key', 'AUT-67', () => token({ header: (h) => { h.kid = '#atproto_label' } }),
    refused('BadJwtSignature'), options)
}
await one('no kid when #atproto is not allowed', 'AUT-66', () => token(), refused('BadJwt'), { allowedKeyIds: ['#atproto_label'] })

// ── iss ──────────────────────────────────────────────────
await one('issuer with a service fragment', 'AUT-66', () => token({ payload: (p) => { p.iss = `${CALLER}#atproto_labeler` } }), refused('BadJwtIss'))
for (const iss of ['host.example.com', 'https://host.example.com'])
  await one(`issuer ${iss}`, 'AUT-66', () => token({ payload: (p) => { p.iss = iss } }), refused('BadJwtIss'))
await one('missing issuer', 'AUT-66', () => token({ payload: (p) => { delete p.iss } }), refused('BadJwtIss'))
await one('issuer that does not resolve', 'AUT-65', () => token({ payload: (p) => { p.iss = 'did:plc:unknownunknownunknownunk' } }), refused('BadJwtIss'))

// ── aud ──────────────────────────────────────────────────
for (const aud of ['did:web:feed.example.com', 'did:web:feed.example.com#bsky_labeler', 'did:web:other.example.com#bsky_fg'])
  await one(`audience ${aud} not accepted`, 'AUT-66', () => token({ payload: (p) => { p.aud = aud } }), refused('BadJwtAudience'))

// ── lxm ──────────────────────────────────────────────────
for (const [name, change] of [
  ['another method', (p) => { p.lxm = 'com.atproto.repo.deleteRecord' }],
  ['a method differing only in case', (p) => { p.lxm = 'app.bsky.feed.getfeedskeleton' }],
  ['no lxm', (p) => { delete p.lxm }],
  ['an lxm that is not an NSID', (p) => { p.lxm = 'not an nsid' }],
  ['an lxm that is a number', (p) => { p.lxm = 42 }],
])
  await one(`token for ${name}`, 'AUT-66', () => token({ payload: change }), refused('BadJwtLexiconMethod'))

// ── exp, iat, nbf ────────────────────────────────────────
await one('expired', 'AUT-66', () => token({ payload: (p) => { p.exp = NOW - 120 } }), refused('JwtExpired'))
await one('valid for longer than the ceiling', 'AUT-66', () => token({ payload: (p) => { p.exp = NOW + 3600 } }), refused('BadJwt'))
await one('dated in the future', 'AUT-66', () => token({ payload: (p) => { p.iat = NOW + 120 } }), refused('BadJwt'))
await one('issued longer ago than the ceiling', 'AUT-66', () => token({ payload: (p) => { p.iat = NOW - 600 } }), refused('BadJwt'))
await one('not valid yet', 'AUT-66', () => token({ payload: (p) => { p.nbf = NOW + 60 } }), refused('BadJwt'))
for (const claim of ['exp', 'iat']) {
  await one(`missing ${claim}`, 'AUT-66', () => token({ payload: (p) => { delete p[claim] } }), refused('BadJwt'))
  await one(`${claim} that is not a whole number`, 'AUT-66', () => token({ payload: (p) => { p[claim] = 1.5 } }), refused('BadJwt'))
}
// Outside what a time type holds: refused, never thrown.
for (const claim of ['exp', 'iat'])
  for (const seconds of ['253402300800', '9223372036854775807', '-62135596801', '-9223372036854775808'])
    await one(`${claim} ${seconds} outside the representable range`, 'AUT-66',
      () => token({ payload: (p) => { p[claim] = `@@${seconds}@@` } }), refused('BadJwt'))
for (const nbf of [253402300800, 1.5])
  await one(`nbf ${nbf} that is not a valid time`, 'AUT-66', () => token({ payload: (p) => { p.nbf = nbf } }), refused('BadJwt'))
await one('the largest representable expiry', 'AUT-66', () => token({ payload: (p) => { p.exp = 253402300799 } }), refused('BadJwt'))

// ── Signature ────────────────────────────────────────────
await one('signed by another key', 'AUT-67', () => token({ signer: attacker }), refused('BadJwtSignature'))
await one('key rotated since the cached document', 'AUT-67', () => token({ signer: rotated }), ok(),
  { freshDocuments: { [CALLER]: doc(CALLER, method(CALLER, '#atproto', rotated)) } })
await one('issuer publishing no #atproto key', 'AUT-67', () => token(), refused('BadJwtSignature'), { documents: { [CALLER]: doc(CALLER) } })
{
  const [h, , s] = (await token()).split('.')
  const forged = (await token()).split('.')[1] // another jti
  add('tampered payload', 'AUT-67', [{ token: `${h}.${forged}.${s}`, expect: refused('BadJwtSignature') }])
}
for (const s of [(1n << 256n) - 1n, K256_ORDER, 0n])
  add(`signature scalar ${s === 0n ? 'zero' : s === K256_ORDER ? 'n' : '2^256 - 1'}`, 'AUT-67',
    [{ token: withS(await token(), s), expect: refused('BadJwtSignature') }])

// ── jti ──────────────────────────────────────────────────
{
  const twice = await token()
  add('same token twice', 'AUT-68', [{ token: twice, expect: ok() }, { token: twice, expect: refused('BadJwt') }])
}
await one('missing jti', 'AUT-66', () => token({ payload: (p) => { delete p.jti } }), refused('BadJwt'))
await one('overlong jti', 'AUT-66', () => token({ payload: (p) => { p.jti = 'a'.repeat(256) } }), refused('BadJwt'))
for (const jti of [' ', '\t', 'abc\u0000def', 'abc\ndef', 'abc\u0085'])
  add(`jti ${JSON.stringify(jti)} that cannot be spent`, 'AUT-68', [
    { token: await token({ payload: (p) => { p.jti = jti } }), expect: refused('BadJwt') },
  ])
add('forged token does not spend the genuine jti', 'AUT-68', [
  { token: await token({ signer: attacker, payload: (p) => { p.jti = 'shared' } }), expect: refused('BadJwtSignature') },
  { token: await token({ payload: (p) => { p.jti = 'shared' } }), expect: ok({ tokenId: 'shared' }) },
])
add('audience refusal does not spend the jti', 'AUT-68', [
  { token: await token({ payload: (p) => { p.aud = 'did:web:other.example.com#bsky_fg'; p.jti = 'kept' } }), expect: refused('BadJwtAudience') },
  { token: await token({ payload: (p) => { p.jti = 'kept' } }), expect: ok({ tokenId: 'kept' }) },
])
{
  // Accepted until exp + skew, so the jti is kept that long.
  const replayed = await token()
  add('replayed after expiry but within the skew', 'AUT-68', [
    { token: replayed, expect: ok() },
    { token: replayed, at: 120, expect: refused('BadJwt') },
  ], { clockSkewSeconds: 300 })
}

console.log(JSON.stringify({
  generator: { '@atproto/crypto': '0.5.5' },
  now: NOW,
  defaults: { audiences: [AUDIENCE], method: METHOD, allowedKeyIds: ['#atproto'], maxLifetimeSeconds: 300, clockSkewSeconds: 30 },
  documents,
  cases,
}, null, 2))
