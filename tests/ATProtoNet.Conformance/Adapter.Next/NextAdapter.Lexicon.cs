namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public LexiconResolution ResolveLexicon(string nsid, string didDocumentJson, byte[] getRecordCar) => throw Later(3);

    /// <inheritdoc />
    public string LexiconDnsName(string nsid) => throw Later(3);

    /// <inheritdoc />
    public bool ValidateLexicon(string lexiconJson) => throw Later(3);

    /// <inheritdoc />
    public bool ValidateRecord(IReadOnlyList<string> lexiconsJson, string rkey, string recordJson) => throw Later(3);
}
