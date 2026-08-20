// Spaces (permissioned data) sample — the alpha access-controlled data protocol.
// See docs/spaces.md for full documentation.
//
//   ATPROTO_PDS_URL=http://localhost:2583 \
//   ATPROTO_TEST_HANDLE=alice.test ATPROTO_TEST_PASSWORD=... dotnet run
//
// This needs a PDS running the permissioned-data implementation (bluesky-social/atproto#5187);
// an ordinary PDS answers 404 on every com.atproto.space.* method.
//
// It walks three things in order:
//   1. Personal data — a space anchored on the user's own DID, reachable with OAuth alone.
//   2. Sync — the set-hash comparison that tells a syncer whether its copy is current.
//   3. Shared reads — the credential exchange that reaches another member's repo.

using System.Text.Json;
using ATProtoNet;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

var pdsUrl = Environment.GetEnvironmentVariable("ATPROTO_PDS_URL") ?? "http://localhost:2583";
var handle = Environment.GetEnvironmentVariable("ATPROTO_TEST_HANDLE");
var password = Environment.GetEnvironmentVariable("ATPROTO_TEST_PASSWORD");

Console.WriteLine("ATProto.NET Spaces Sample");
Console.WriteLine("=========================");
Console.WriteLine($"PDS: {pdsUrl}\n");

if (string.IsNullOrEmpty(handle) || string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("Set ATPROTO_TEST_HANDLE and ATPROTO_TEST_PASSWORD.");
    return 1;
}

using var client = new AtProtoClientBuilder().WithInstanceUrl(pdsUrl).Build();
await client.LoginAsync(handle, password);

var did = client.Did!;
Console.WriteLine($"Signed in as {did}\n");

// ── 1. Personal data ──────────────────────────────────────────────────────
//
// A space anchored on the user's own DID needs no credential machinery: their PDS is both the
// space host and the repo host, so OAuth (or, here, an app password session) is enough.

Console.WriteLine("── Creating a personal space ──");

var created = await client.SimpleSpace.CreateSpaceAsync(
    "com.example.bookmarks",
    skey: "self",
    policy: new MemberListPolicy(),
    appAccess: new OpenAppAccess());

var space = created.ToSpaceUri();
Console.WriteLine($"space:     {space}");
Console.WriteLine($"authority: {space.Authority}");
Console.WriteLine($"type:      {space.SpaceType}");
Console.WriteLine($"skey:      {space.Skey}\n");

var write = await client.Space.CreateRecordAsync(
    space, did, "com.example.bookmark",
    new
    {
        // Records in a space are ordinary Lexicon-typed records; only the perimeter differs.
        type = "com.example.bookmark",
        url = "https://atproto.com/blog/atproto-spaces-alpha",
        title = "AT Protocol Spaces",
        createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
    });

Console.WriteLine($"wrote {write.ToRecordUri().Path}  ({write.Cid})\n");

Console.WriteLine("── Reading it back ──");
await foreach (var record in client.Space.EnumerateRecordsAsync(space, did))
    Console.WriteLine($"  {record.Path}");

// listSpaces is "spaces I have written to", not "spaces I am a member of" — a PDS only tracks
// the former, since membership is the authority's business.
var spaces = await client.Space.ListSpacesAsync(type: "com.example.bookmarks");
Console.WriteLine($"\n{spaces.Spaces.Count} space(s) of this type hold data for this account.\n");

// ── 2. Sync ───────────────────────────────────────────────────────────────
//
// A syncer keeps a running set hash over its own copy. When it matches the repo's signed
// commit, the copy is exactly current — and that comparison, not having received every
// individual operation, is what makes the protocol self-healing.

Console.WriteLine("── Syncing ──");

var store = new ConsoleStore();
var syncer = new SpaceSyncer(space, store, SpaceSyncer.ResolveSigningKeyAsync(new ATProtoNet.Identity.DidResolver()));
var cursor = new SpaceRepoCursor(did);

var result = await syncer.SyncRepoAsync(client.Space, cursor);

Console.WriteLine($"outcome: {result.Outcome}");
Console.WriteLine($"rev:     {cursor.Rev}");
Console.WriteLine($"digest:  {Convert.ToHexStringLower(cursor.Commit.Digest())[..16]}…");

if (result.Commit is not null)
{
    // The signature covers only (space, author, rev, ikm) — never the digest, which is bound
    // by a symmetric MAC instead. A leaked commit therefore proves nothing about its contents.
    Console.WriteLine($"commit:  ver={result.Commit.Ver} rev={result.Commit.Rev} " +
                      $"hash={Convert.ToHexStringLower(result.Commit.Hash)[..16]}…");
    Console.WriteLine($"matches: {cursor.Commit.Matches(result.Commit)}");
}

// Persist these two together; a restart resumes from exactly here without re-reading the repo.
var (savedRev, savedState) = (cursor.Rev, cursor.GetState());
Console.WriteLine($"persisted {savedState.Length}-byte set-hash state at rev {savedRev}\n");

// ── 3. Shared reads ───────────────────────────────────────────────────────
//
// Reading another member's repo needs a space credential: the application asks the user's PDS
// for a delegation token, presents it to the space authority with a DPoP proof, and gets back a
// credential bound to the key that signed that proof.

Console.WriteLine("── Reading the space as a syncer would ──");

await using var provider = new SpaceCredentialProvider(
    client,
    // A local PDS is not resolvable through the PLC directory, so point the provider at it
    // directly. Against the real network this is resolved from the authority's DID document.
    new SpaceCredentialOptions { HostResolver = (_, _) => Task.FromResult(pdsUrl) });

try
{
    var credential = await provider.GetCredentialAsync(space);
    Console.WriteLine($"credential expires {credential.ExpiresAt:u}, bound to key {credential.Token.ConfirmationThumbprint}");

    // The writer set: accounts that have written at least one record into the space. It is the
    // sync boundary, not an access-control list — readers are never enumerated.
    await foreach (var writer in client.Space.EnumerateReposAsync(space))
    {
        Console.WriteLine($"\nwriter {writer.Did} @ rev {writer.Rev}");

        using var reader = await provider.CreateReaderForRepoAsync(space, writer.Did);
        await foreach (var record in reader.Space.EnumerateRecordsAsync(space, writer.Did))
            Console.WriteLine($"  {record.Path}");
    }
}
catch (SpaceCredentialException ex) when (ex.Error == SpaceErrors.SpaceDeleted)
{
    // The durable signal that a space is gone: a syncer that missed notifySpaceDeleted learns
    // it here, on its next renewal, and should drop every copy it holds.
    Console.WriteLine("The space was deleted — dropping every local copy.");
}
catch (SpaceCredentialException ex)
{
    // A refusal for any other reason says nothing about the space; keep what you hold.
    Console.WriteLine($"No credential ({ex.Error ?? "unknown"}): {ex.Message}");
}

Console.WriteLine("\n── Scopes an OAuth client would request ──");
Console.WriteLine("  " + ATProtoNet.Auth.OAuth.AtProtoScopes.Space("com.example.bookmarks"));
Console.WriteLine("  " + ATProtoNet.Auth.OAuth.AtProtoScopes.Space(
    "com.atmoboards.forum", authority: "*", actions: ATProtoNet.Auth.OAuth.SpaceAction.Read));

return 0;

/// <summary>
/// A syncer's copy of a space. The real thing writes to a database; this one narrates.
/// </summary>
internal sealed class ConsoleStore : ISpaceRepoStore
{
    public Task ApplyAsync(SpaceUri space, string repo, SpaceRepoOpEntry op, CancellationToken cancellationToken)
    {
        var kind = (op.Prev, op.Cid) switch
        {
            (null, not null) => "create",
            (not null, not null) => "update",
            _ => "delete",
        };

        Console.WriteLine($"  [{kind}] {op.Collection}/{op.Rkey} @ {op.Rev}");
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(SpaceUri space, string repo, VerifiedSpaceRepo contents, CancellationToken cancellationToken)
    {
        // Reached when the oplog could not carry the copy forward — a dropped write, a compacted
        // oplog, or local corruption all land here, and all are repaired the same way.
        Console.WriteLine($"  [recover] {contents.Records.Count} record(s) at rev {contents.Commit.Rev}");
        return Task.CompletedTask;
    }

    public Task DropAsync(SpaceUri space, string repo, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [drop] {repo}");
        return Task.CompletedTask;
    }
}
