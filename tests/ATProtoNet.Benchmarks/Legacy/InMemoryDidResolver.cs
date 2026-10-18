using ATProtoNet.Identity;

namespace ATProtoNet.Benchmarks;

/// <summary>Serves fixed DID documents without a network, counting what it serves.</summary>
internal sealed class InMemoryDidResolver(IReadOnlyDictionary<Did, DidDocument> documents) : IDidResolver
{
    public int Resolves { get; private set; }

    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        Resolves++;
        return Task.FromResult(documents[did]);
    }
}
