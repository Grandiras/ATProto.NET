namespace ATProtoNet.Conformance;

/// <summary>
/// The protocol operations the conformance suite checks, implemented once over each SDK
/// (<c>Adapter.Legacy</c> over 0.8, <c>Adapter.Next</c> over the rewrite). Each conformance area
/// adds its members in its own file (<c>IProtocolAdapter.{Area}.cs</c>).
/// </summary>
/// <remarks>
/// Members take and return plain data (strings, bytes, verdict records), never either SDK's
/// types, so the suite stays independent of both APIs.
/// </remarks>
public partial interface IProtocolAdapter
{
    /// <summary>
    /// Vectors this implementation is known to fail, by vector ID (or ID prefix ending in
    /// <c>*</c>), with the reason: an issue reference for a filed bug, or the feature the
    /// implementation lacks. The suite skips them rather than failing.
    /// </summary>
    IReadOnlyDictionary<string, string> KnownFailures { get; }
}
