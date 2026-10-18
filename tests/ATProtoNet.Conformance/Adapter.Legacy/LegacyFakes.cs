using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

namespace ATProtoNet.Conformance.Legacy;

/// <summary>A clock fixed at the vector's <c>now</c>.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// DID documents served from memory: <see cref="IDidResolver.ResolveAsync"/> answers with the
/// cached document, <see cref="IDidResolver.RefreshAsync"/> with the fresh one where the vector
/// gives one. An unknown DID does not resolve.
/// </summary>
internal sealed class FakeDidResolver : IDidResolver
{
    private readonly Dictionary<string, DidDocument> _cached = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DidDocument> _fresh = new(StringComparer.Ordinal);

    public FakeDidResolver Publish(string documentJson, string? freshDocumentJson = null)
    {
        var document = Parse(documentJson);
        _cached[document.Id.Value] = document;
        if (freshDocumentJson is not null)
            _fresh[document.Id.Value] = Parse(freshDocumentJson);
        return this;
    }

    /// <summary>Publishes a document whose only key is <paramref name="didKey"/> as <c>#atproto</c> (none when null).</summary>
    public FakeDidResolver PublishSigningKey(string did, string? didKey) => Publish(SigningKeyDocument(did, didKey));

    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
        _cached.TryGetValue(did.Value, out var document)
            ? Task.FromResult(document)
            : Task.FromException<DidDocument>(new DidResolutionException($"{did} is not in the vector.", DidResolutionErrorKind.NotFound));

    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default) =>
        _fresh.TryGetValue(did.Value, out var document) ? Task.FromResult(_cached[did.Value] = document) : ResolveAsync(did, cancellationToken);

    public static string SigningKeyDocument(string did, string? didKey) => JsonSerializer.Serialize(new
    {
        id = did,
        verificationMethod = didKey is null
            ? []
            : new[] { new { id = $"{did}#atproto", type = "Multikey", controller = did, publicKeyMultibase = didKey["did:key:".Length..] } },
    });

    private static DidDocument Parse(string json) => JsonSerializer.Deserialize<DidDocument>(json, AtProtoJsonDefaults.Options)!;
}
