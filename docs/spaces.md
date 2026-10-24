# Spaces

Spaces are atproto's permissioned data: records shared with the members of a space rather than
published, stored in per-space repositories on each author's host. `ATProtoNet.Spaces` has the
protocol's foundation, the parts that do no I/O: the repository hash, commits, the MACs that
authenticate them, repository exports and the three token types. The space client, the syncer
and the server roles build on it in later phases.

The SDK implements the Spaces alpha of 2026-10-22: proposal 0016 at
[`b89d0b04`](https://github.com/bluesky-social/proposals/blob/b89d0b04495a62e0e22f1ab04c665a9c4e1c655d/0016-permissioned-data/README.md),
checked against the reference implementation at `3a0b8c66`. The assembly records both as
`AssemblyMetadata` (`ATProtoNet.Spaces.Proposal`, `ATProtoNet.Spaces.Reference`). The alpha still
ships breaking changes, and this page follows each one.

## Repositories and commits

A space repository commits to its records with an `LtHash`, a set hash that depends only on which
records are present. A `SpaceCommit` names the author, the space and the revision, and carries the
hash's digest as `data`. It is not signed.

```csharp
using ATProtoNet;
using ATProtoNet.Spaces;

var space = SpaceUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.example.forum/default");
var author = Did.Parse("did:plc:44ybard66vv44zksje25o7dz");
var record = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");

var hash = LtHash.Empty();
hash.Add(Nsid.Parse("com.example.post"), RecordKey.Parse("3jzfcijpj2z2a"), record);
var commit = new SpaceCommit(author, space, Tid.Next(), hash.Digest());
```

`LtHash` is a mutable accumulator: `GetState` and `FromState` persist it, `Clone` copies it, and
it has no value equality.

## MACs

A host authenticates a commit or a record with a MAC keyed by a secret it shares with one
requester, and sends it in the `Atproto-Space-Mac` response header. The requester starts the
exchange: it encapsulates a fresh secret to the author's `#atproto_kem` key and sends the
ephemeral key in `Atproto-Space-Want-Mac`. It makes a new request for every call, retries
included.

```csharp continued
using ATProtoNet.Crypto;

using var authorKem = KemPrivateKey.Generate(KemAlgorithm.Default);    // the host's
var request = SpaceMacRequest.Create(authorKem.PublicKey, SpaceMacType.Repo, out var secret);
string header = request.ToHeaderValue();

// The host:
var received = SpaceMacRequest.Parse(header, SpaceMacType.Repo);
using var hostSecret = received.Accept(authorKem);
string macHeader = SpaceMac.FormatHeader(SpaceMacType.Repo, SpaceMac.ComputeCommit(hostSecret, commit));

// The requester:
bool authentic = SpaceMac.VerifyCommit(secret, commit, SpaceMac.ParseHeader(macHeader, SpaceMacType.Repo));
secret.Dispose();
```

A MAC convinces the requester and no one else, since either side could have computed it. Space
data is deniable and cannot be rebroadcast with proof, public spaces included.

## Exports

`com.atproto.space.getRepo` returns a STAR archive whose metadata is the commit and whose entries
are the records in path order. `SpaceRepoExport.VerifyAsync` checks that the commit is the
requested repository's, that its MAC verifies, and that the records hash to its `data`. Only that
last check shows the whole repository arrived, so nothing is returned before it.
`SpaceRepoExport.WriteAsync` is the host's side. Every failure is a `SpaceVerificationException`
whose `Error` names the check.

## Tokens

`SpaceToken.Parse` reads a delegation token, a space credential or a client attestation of the
expected type, checks its claims and, given a `TimeProvider`, its times. `VerifySignature` checks
it against the issuer's key. `SpaceToken.CreateCredential` is the space authority's side and
`CreateClientAttestation` the OAuth client's. A refused token is a `SpaceTokenException` whose
`Error` is the protocol's error name (`BadJwtType`, `JwtExpired`, …).

A credential is bound to a P-256 key (`ConfirmationKey`). Every request it authorizes is signed
with that key under the HTTP message signature label and `tag` `atproto-space-credential`
([Cryptography](crypto.md#http-message-signatures)).
