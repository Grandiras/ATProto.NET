namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public int MstKeyHeight(string key) => throw Later(2);

    /// <inheritdoc />
    public int MstCommonPrefixLength(string left, string right) => throw Later(2);

    /// <inheritdoc />
    public bool IsValidMstKey(string key) => throw Later(2);

    /// <inheritdoc />
    public MstTree MstEdit(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<MstOperation> operations) => throw Later(2);

    /// <inheritdoc />
    public MstTree MstCoveringProof(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<string> keys) => throw Later(2);

    /// <inheritdoc />
    public string? MstInvert(MstTree proof, IReadOnlyList<MstOperation> operations) => throw Later(2);

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, string>>? MstLoad(string root, IReadOnlyDictionary<string, byte[]> blocks) => throw Later(2);
}
