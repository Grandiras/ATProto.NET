namespace ATProtoNet.Conformance.Next;

/// <summary>
/// <see cref="IProtocolAdapter"/> over the rewrite. Each conformance area implements its members in
/// its own file (<c>NextAdapter.{Area}.cs</c>); an area whose phase has not landed throws
/// <see cref="NotSupportedException"/>, and its suite is not derived in <c>Suites.cs</c>.
/// </summary>
public sealed partial class NextAdapter : IProtocolAdapter
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> KnownFailures { get; } =
        new[] { SyntaxKnownFailures, DataModelKnownFailures, CidCarKnownFailures, CryptoKnownFailures, SpacesKnownFailures, LexiconKnownFailures }
            .SelectMany(d => d).ToDictionary();

    private static NotSupportedException Later(int phase) => new($"Built in phase {phase} of design/05-rewrite-plan.md.");
}
