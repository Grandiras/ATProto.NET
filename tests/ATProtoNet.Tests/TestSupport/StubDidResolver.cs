using System.Collections.Concurrent;
using System.Security.Cryptography;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// The DID resolver tests share: serves the documents published to it (an unknown DID is
/// <see cref="DidResolutionErrorKind.NotFound"/>), or whatever its script answers, and counts
/// every resolve, refresh and invalidation.
/// </summary>
/// <remarks>
/// <see cref="Rotate(string, DidDocument)"/> models a cache that predates a key rotation:
/// resolution keeps returning the previous document until a refresh fetches the published one.
/// </remarks>
public sealed class StubDidResolver : IDidResolver
{
    private readonly Func<Did, CancellationToken, Task<DidDocument>>? _script;
    private readonly ConcurrentDictionary<string, DidDocument> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DidDocument> _stale = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _pds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Did, int> _calls = new();
    private int _resolves;
    private int _refreshes;
    private int _invalidations;

    public StubDidResolver()
    {
    }

    /// <summary>A resolver whose every resolution (refreshes included) is answered by <paramref name="script"/>.</summary>
    public StubDidResolver(Func<Did, CancellationToken, Task<DidDocument>> script) => _script = script;

    /// <summary>How many times a document was resolved, refreshes included.</summary>
    public int Resolves => Volatile.Read(ref _resolves);

    /// <summary>How many times a document was refreshed past the cache.</summary>
    public int Refreshes => Volatile.Read(ref _refreshes);

    /// <summary>How many times a DID was invalidated.</summary>
    public int Invalidations => Volatile.Read(ref _invalidations);

    /// <summary>How many times one DID was resolved.</summary>
    public int Calls(Did did) => _calls.GetValueOrDefault(did);

    /// <summary>Waits (a few seconds at most) until one DID has been resolved at least <paramref name="count"/> times.</summary>
    public async Task WaitForCallsAsync(Did did, int count)
    {
        for (var i = 0; i < 500 && Calls(did) < count; i++)
            await Task.Delay(10);

        Assert.True(Calls(did) >= count, $"Expected {count} calls for {did}, saw {Calls(did)}.");
    }

    public StubDidResolver Publish(string did, DidDocument document)
    {
        _documents[did] = document;
        return this;
    }

    /// <summary>Publishes a minimal atproto document: a signing key and, unless null, a PDS.</summary>
    public StubDidResolver Publish(Did did, string signingKey, string? pds = "https://pds.example.com")
    {
        _pds[did.Value] = pds;
        return Publish(did.Value, DidDocs.Parse(did.Value, pds: pds, signingKey: signingKey));
    }

    /// <summary>
    /// Publishes a new document for a DID while resolution keeps serving the current one, as a
    /// cache would until it is refreshed.
    /// </summary>
    public StubDidResolver Rotate(string did, DidDocument document)
    {
        _stale[did] = _documents[did];
        return Publish(did, document);
    }

    /// <summary><see cref="Rotate(string, DidDocument)"/> to a new signing key, keeping the PDS.</summary>
    public StubDidResolver Rotate(Did did, string signingKey) =>
        Rotate(did.Value, DidDocs.Parse(did.Value, pds: _pds.GetValueOrDefault(did.Value), signingKey: signingKey));

    /// <summary>Publishes an ordinary account: one <c>#atproto</c> Multikey and a PDS endpoint.</summary>
    public StubDidResolver PublishAccount(string did, AtProtoKey key, string? pds = null) =>
        Publish(did, AccountDocument(did, key, pds));

    /// <summary>An ordinary account's document: one <c>#atproto</c> Multikey and a PDS endpoint.</summary>
    public static DidDocument AccountDocument(string did, AtProtoKey key, string? pds = null) => new()
    {
        Id = Did.Parse(did),
        VerificationMethod =
        [
            new VerificationMethod
            {
                Id = $"{did}#atproto",
                Type = "Multikey",
                Controller = did,
                PublicKeyMultibase = key.ToMultikey(),
            },
        ],
        Service = pds is null
            ? []
            :
            [
                new DidDocumentService
                {
                    Id = "#atproto_pds",
                    Type = "AtprotoPersonalDataServer",
                    Endpoint = pds,
                },
            ],
    };

    /// <summary>
    /// Publishes an account whose key sits under a legacy <c>Ecdsa...VerificationKey2019</c>
    /// entry — a bare uncompressed point rather than a multicodec-tagged compressed one, which
    /// is what older PLC releases and hand-written <c>did:web</c> documents serve.
    /// </summary>
    public StubDidResolver PublishLegacyAccount(string did, string fragment, ECDsa key)
    {
        var q = key.ExportParameters(false).Q;

        return Publish(did, new DidDocument
        {
            Id = Did.Parse(did),
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = $"{did}{fragment}",
                    Type = "EcdsaSecp256r1VerificationKey2019",
                    Controller = did,
                    PublicKeyMultibase = "z" + AtProtoCrypto.Base58Encode([0x04, .. q.X!, .. q.Y!]),
                },
            ],
        });
    }

    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _resolves);
        _calls.AddOrUpdate(did, 1, (_, count) => count + 1);

        if (_script is not null)
            return _script(did, cancellationToken);

        if (_stale.TryGetValue(did.Value, out var stale))
            return Task.FromResult(stale);

        return _documents.TryGetValue(did.Value, out var document)
            ? Task.FromResult(document)
            : Task.FromException<DidDocument>(
                new DidResolutionException($"No fixture for '{did}'.", DidResolutionErrorKind.NotFound, did));
    }

    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refreshes);
        _stale.TryRemove(did.Value, out _);
        return ResolveAsync(did, cancellationToken);
    }

    public Task InvalidateAsync(Did did, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invalidations);
        return Task.CompletedTask;
    }
}
