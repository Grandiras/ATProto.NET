// Writes dpop-pkce.json: DPoP (RFC 9449) and PKCE (RFC 7636) cases — the RFC examples, the htu
// normalisation table and proof requests of 0.8's DPoPTests and DPoPProofGeneratorTests — with a
// fixed P-256 key. No dependencies: `node gen-dpop-pkce.mjs > dpop-pkce.json`.
import { createECDH, createHash } from 'node:crypto'

const b64url = (bytes) => Buffer.from(bytes).toString('base64url')
const seed = (text) => createHash('sha256').update(text).digest()

const p256 = seed('ATProto.NET DPoP vector key')
const point = createECDH('prime256v1').setPrivateKey(p256).getPublicKey()
const jwk = { kty: 'EC', crv: 'P-256', x: b64url(point.subarray(1, 33)), y: b64url(point.subarray(33)) }
const thumbprint = (key) => b64url(createHash('sha256').update(JSON.stringify({ crv: key.crv, kty: key.kty, x: key.x, y: key.y })).digest())
const ath = (token) => b64url(createHash('sha256').update(token, 'ascii').digest())

// RFC 9449 section 4.1 (Figure 2) embeds this key; section 6.1 gives its thumbprint.
const rfcKey = { kty: 'EC', crv: 'P-256', x: 'l8tFrhx-34tV3hRICRDY9zCkDlpBhF42UQUfWVAWBFs', y: '9VE4jf_Ok_o64zbTTlcuNJajHmt6v9TDVrU0CdvGRDA' }
if (thumbprint(rfcKey) !== '0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I') throw new Error('RFC 9449 thumbprint')
// RFC 9449 section 7.1.
const rfcToken = 'Kz~8mXK1EalYznwH-LC-1fBAo.4Ljp~zsPE_NeO.gxU'
if (ath(rfcToken) !== 'fUHyO2r2Z3DZ53EsNrWBb0xWXoaNy59IiKCAqksmQEo') throw new Error('RFC 9449 ath')

const { y, ...noY } = rfcKey
const thumbprints = [
  { name: 'RFC 9449 key', jwk: rfcKey, expect: '0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I' },
  { name: 'members RFC 7638 does not require', jwk: { ...rfcKey, kid: 'key-1', use: 'sig', alg: 'ES256' }, expect: '0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I' },
  { name: 'the vector key', jwk, expect: thumbprint(jwk) },
  { name: 'not an EC key', jwk: { ...rfcKey, kty: 'RSA' }, expect: null },
  { name: 'EC key without y', jwk: noY, expect: null },
]

const accessTokenHashes = [
  { name: 'RFC 9449 token', token: rfcToken, expect: ath(rfcToken) },
  { name: 'token-one', token: 'token-one', expect: ath('token-one') },
  { name: 'token-two', token: 'token-two', expect: ath('token-two') },
]

const htu = [
  ['https://pds.example.com/xrpc/a', 'https://pds.example.com/xrpc/a'],
  ['https://pds.example.com/xrpc/a?b=c#d', 'https://pds.example.com/xrpc/a'],
  ['https://user:pw@pds.example.com/xrpc/a', 'https://pds.example.com/xrpc/a'],
  ['https://user@pds.example.com:8443/xrpc/a', 'https://pds.example.com:8443/xrpc/a'],
  ['https://user@pds.example.com:8443/xrpc/a?b=c', 'https://pds.example.com:8443/xrpc/a'],
  ['HTTPS://PDS.EXAMPLE.COM/xrpc/a', 'https://pds.example.com/xrpc/a'],
  ['HTTPS://PDS.Example.COM:443/xrpc/a', 'https://pds.example.com/xrpc/a'],
  ['https://pds.example.com:443/xrpc/a', 'https://pds.example.com/xrpc/a'],
  ['http://pds.example.com:80/xrpc/a', 'http://pds.example.com/xrpc/a'],
  ['http://[::1]:2583/xrpc/a', 'http://[::1]:2583/xrpc/a'],
  ['https://pds.example.com', 'https://pds.example.com/'],
  ['/xrpc/a', null],
  ['pds.example.com/xrpc/a', null],
  ['not a url at all', null],
  ['file:///xrpc/a', null],
].map(([url, expect]) => ({ name: url, url, expect }))

const proof = (name, method, url, expect, extra = {}) => ({ name, key: 'p256', method, url, ...extra, expect })
const proofs = [
  proof('without a nonce', 'POST', 'https://example.com/token', { htm: 'POST', htu: 'https://example.com/token', nonce: null, ath: null }),
  proof('with a nonce', 'POST', 'https://example.com/token', { htm: 'POST', nonce: 'test-nonce-value' }, { nonce: 'test-nonce-value' }),
  proof('lower-case method', 'get', 'https://example.com/token', { htm: 'GET' }),
  proof('userinfo, query and fragment dropped', 'GET', 'https://user@pds.example.com:8443/xrpc/a?b=c#d', { htu: 'https://pds.example.com:8443/xrpc/a' }),
  proof('scheme and host lower-cased', 'GET', 'HTTPS://PDS.Example.COM:443/xrpc/a', { htu: 'https://pds.example.com/xrpc/a' }),
  proof('access token hash', 'GET', 'https://resource.example.org/protectedresource',
    { htm: 'GET', htu: 'https://resource.example.org/protectedresource', ath: ath(rfcToken) }, { accessToken: rfcToken }),
  proof('relative URL', 'GET', '/xrpc/a', null),
  proof('URL without a scheme', 'GET', 'pds.example.com/xrpc/a', null),
  proof('file URL', 'GET', 'file:///xrpc/a', null),
  { name: 'K-256 key', key: 'k256', method: 'POST', url: 'https://example.com/token', expect: null },
]

console.log(JSON.stringify({
  keys: {
    p256: { curve: 'P-256', privateKey: p256.toString('hex'), jwk },
    k256: { curve: 'K-256', privateKey: seed('ATProto.NET DPoP K-256 key').toString('hex') },
  },
  thumbprints,
  accessTokenHashes,
  htu,
  proofs,
  pkce: [{ name: 'RFC 7636 appendix B', verifier: 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk', expect: 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM' }],
}, null, 2))
