namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>The MST layer of a key: leading zero 2-bit pairs of SHA-256 over its bytes.</summary>
    int MstKeyHeight(string key);

    /// <summary>The length of the longest common prefix of two keys, as MST nodes compress them.</summary>
    int MstCommonPrefixLength(string left, string right);

    /// <summary>Whether <paramref name="key"/> may be stored in an MST.</summary>
    bool IsValidMstKey(string key);

    /// <summary>
    /// Builds the tree of <paramref name="entries"/> (key to value CID), applies
    /// <paramref name="operations"/> to it in order, and returns the result with every node.
    /// </summary>
    MstTree MstEdit(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<MstOperation> operations);

    /// <summary>
    /// The covering proof of <paramref name="keys"/> in the tree of <paramref name="entries"/>:
    /// the root and the nodes that prove each key present or absent.
    /// </summary>
    MstTree MstCoveringProof(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<string> keys);

    /// <summary>
    /// Loads the partial tree <paramref name="proof"/> holds, undoes <paramref name="operations"/>
    /// (in an order of the implementation's choosing; the result does not depend on it) and returns
    /// the root before them, or <see langword="null"/> when the proof does not cover them.
    /// </summary>
    string? MstInvert(MstTree proof, IReadOnlyList<MstOperation> operations);

    /// <summary>
    /// Loads a complete tree from untrusted blocks and returns its entries in key order, or
    /// <see langword="null"/> when the blocks are not exactly a well-formed, canonical MST.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>>? MstLoad(string root, IReadOnlyDictionary<string, byte[]> blocks);
}

/// <summary>An MST: its root CID and its node blocks by CID.</summary>
public sealed record MstTree(string Root, IReadOnlyDictionary<string, byte[]> Blocks);

/// <summary>
/// A change to one key: an insert when <paramref name="Before"/> is null, a delete when
/// <paramref name="After"/> is null, otherwise an update.
/// </summary>
public sealed record MstOperation(string Key, string? Before, string? After);
