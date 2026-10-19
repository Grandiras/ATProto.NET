namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public byte[] LabelSigningBytes(string labelJson) => throw Later(6);

    /// <inheritdoc />
    public LabelStatus VerifyLabel(string labelJson, string didKey) => throw Later(6);

    /// <inheritdoc />
    public LabelStatus VerifyLabelAgainstIssuer(string labelJson, string? documentJson, string? freshDocumentJson) => throw Later(6);

    /// <inheritdoc />
    public string SignLabel(string labelJson, string curve, string privateKeyHex) => throw Later(6);

    /// <inheritdoc />
    public byte[]? EncodeLabelFrame(string messageJson) => throw Later(6);
}
