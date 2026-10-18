namespace ATProtoNet.Conformance.Legacy;

/// <summary>
/// <see cref="IProtocolAdapter"/> over the 0.8 packages. Each conformance area implements its
/// members in its own file (<c>LegacyAdapter.{Area}.cs</c>).
/// </summary>
public sealed partial class LegacyAdapter : IProtocolAdapter
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> KnownFailures => LegacyKnownFailures.All;
}
