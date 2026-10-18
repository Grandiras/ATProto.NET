// Writes oauth-metadata.json: the protected-resource and authorization-server metadata a PDS and
// its authorization server publish, each case changing one member, with the verdict the AT Protocol
// OAuth profile owes it — 0.8's AuthorizationServerMetadataValidationTests as data.
// No dependencies: `node gen-oauth-metadata.mjs > oauth-metadata.json`.
const PDS = 'https://pds.example.com'
const ISSUER = 'https://auth.example.com'

const resource = (res = PDS, servers = [ISSUER]) => ({ resource: res, authorization_servers: servers })
const server = (issuer = ISSUER, change = {}) => {
  const metadata = {
    issuer,
    authorization_endpoint: `${issuer}/oauth/authorize`,
    token_endpoint: `${issuer}/oauth/token`,
    pushed_authorization_request_endpoint: `${issuer}/oauth/par`,
    revocation_endpoint: `${issuer}/oauth/revoke`,
    require_pushed_authorization_requests: true,
    authorization_response_iss_parameter_supported: true,
    client_id_metadata_document_supported: true,
    scopes_supported: ['atproto', 'transition:generic'],
    dpop_signing_alg_values_supported: ['ES256'],
    token_endpoint_auth_methods_supported: ['none', 'private_key_jwt'],
    token_endpoint_auth_signing_alg_values_supported: ['ES256'],
  }
  for (const [member, value] of Object.entries(change))
    if (value === undefined) delete metadata[member]
    else metadata[member] = value
  return metadata
}

const cases = []
const add = (name, spec, expect, { pds = PDS, resourceMetadata = resource(), serverMetadata = server(), allowPrivateNetworks = false } = {}) =>
  cases.push({ name, spec, pds, allowPrivateNetworks, resourceMetadata, serverMetadata, expect })
const accepted = (issuer = ISSUER) => ({ ok: true, issuer })
const refused = (error) => ({ ok: false, error })

add('valid metadata', 'AUT-35', accepted())
for (const issuer of ['https://auth.example.com:8443', 'https://auth.example.com/tenant', 'https://auth.example.com/', 'https://AUTH.example.com', 'http://auth.example.com', 'https://evil.example.com'])
  add(`issuer ${issuer}`, 'AUT-35', refused('issuer_mismatch'), { serverMetadata: server(ISSUER, { issuer }) })
{
  const issuer = 'https://auth.example.com:8443/tenant'
  add('issuer with a port and path the resource names', 'AUT-35', accepted(issuer),
    { resourceMetadata: resource(PDS, [issuer]), serverMetadata: server(issuer) })
}
for (const [member, value] of [
  ['token_endpoint', 'http://auth.example.com/oauth/token'],
  ['token_endpoint', 'https://auth.example.com/oauth/token?tenant=1'],
  ['token_endpoint', 'https://auth.example.com/oauth/token#frag'],
  ['token_endpoint', '/oauth/token'],
  ['token_endpoint', 'https://user:pass@auth.example.com/oauth/token'],
  ['authorization_endpoint', 'javascript:alert(1)'],
  ['pushed_authorization_request_endpoint', 'ftp://auth.example.com/oauth/par'],
  ['revocation_endpoint', 'https://auth.example.com/oauth/revoke?x=1'],
])
  add(`${member} ${value}`, 'AUT-35', refused('invalid_metadata'), { serverMetadata: server(ISSUER, { [member]: value }) })
for (const member of ['authorization_endpoint', 'token_endpoint', 'pushed_authorization_request_endpoint'])
  add(`missing ${member}`, 'AUT-35', refused('invalid_metadata'), { serverMetadata: server(ISSUER, { [member]: undefined }) })
add('no revocation endpoint', 'AUT-35', accepted(), { serverMetadata: server(ISSUER, { revocation_endpoint: undefined }) })
{
  const local = 'http://localhost:2583'
  add('plain HTTP under the development opt-out', 'AUT-35', accepted(local),
    { pds: local, resourceMetadata: resource(local, [local]), serverMetadata: server(local), allowPrivateNetworks: true })
}
for (const member of ['require_pushed_authorization_requests', 'authorization_response_iss_parameter_supported', 'client_id_metadata_document_supported']) {
  add(`missing ${member}`, 'AUT-35', refused('invalid_metadata'), { serverMetadata: server(ISSUER, { [member]: undefined }) })
  add(`${member} false`, 'AUT-35', refused('invalid_metadata'), { serverMetadata: server(ISSUER, { [member]: false }) })
}
for (const res of ['https://other-pds.example.com', 'https://pds.example.com:8443', 'https://pds.example.com/?x=1'])
  add(`resource ${res}`, 'AUT-34', refused('invalid_resource_metadata'), { resourceMetadata: resource(res) })
add('resource metadata without a resource', 'AUT-34', refused('invalid_resource_metadata'), { resourceMetadata: { authorization_servers: [ISSUER] } })
add('resource with a trailing slash', 'AUT-34', accepted(), { resourceMetadata: resource(`${PDS}/`) })
for (const as of ['https://auth.example.com/', 'auth.example.com', 'https://auth.example.com?x=1'])
  add(`authorization server ${as}`, 'AUT-34', refused('invalid_resource_metadata'), { resourceMetadata: resource(PDS, [as]) })
add('two authorization servers', 'AUT-34', refused('invalid_resource_metadata'), { resourceMetadata: resource(PDS, [ISSUER, 'https://other-auth.example.com']) })
add('scopes without atproto', 'AUT-35', refused('unsupported_scope'), { serverMetadata: server(ISSUER, { scopes_supported: ['transition:generic'] }) })
add('DPoP algorithms without ES256', 'AUT-35', refused('unsupported_dpop_alg'), { serverMetadata: server(ISSUER, { dpop_signing_alg_values_supported: ['RS256', 'ES384'] }) })
add('protected resources that leave out the PDS', 'AUT-35', refused('invalid_resource_metadata'),
  { serverMetadata: server(ISSUER, { protected_resources: ['https://other-pds.example.com'] }) })
add('protected resources that list the PDS', 'AUT-35', accepted(),
  { serverMetadata: server(ISSUER, { protected_resources: ['https://other-pds.example.com', PDS] }) })

console.log(JSON.stringify({ cases }, null, 2))
