namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Resolves the Lexicon <paramref name="nsid"/> whose authority is the DID of
    /// <paramref name="didDocumentJson"/>, its PDS answering <c>com.atproto.sync.getRecord</c> with
    /// <paramref name="getRecordCar"/>, and verifies the record.
    /// </summary>
    LexiconResolution ResolveLexicon(string nsid, string didDocumentJson, byte[] getRecordCar);

    /// <summary>The DNS name whose TXT record names the authority of <paramref name="nsid"/>.</summary>
    string LexiconDnsName(string nsid);

    /// <summary>Whether <paramref name="lexiconJson"/> is a valid Lexicon schema document.</summary>
    bool ValidateLexicon(string lexiconJson);

    /// <summary>
    /// Whether <paramref name="recordJson"/>, stored under <paramref name="rkey"/>, is valid against
    /// the record Lexicon its <c>$type</c> names, with <paramref name="lexiconsJson"/> the schemas in scope.
    /// </summary>
    bool ValidateRecord(IReadOnlyList<string> lexiconsJson, string rkey, string recordJson);
}

/// <summary>The outcome of resolving a Lexicon: where its verified schema record is, or a failure.</summary>
public sealed record LexiconResolution(bool Resolved, string? Uri = null, string? Cid = null, string? LexiconId = null);
