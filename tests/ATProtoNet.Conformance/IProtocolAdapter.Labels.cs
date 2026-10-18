namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>The bytes a label's signature covers, for a label in its JSON form.</summary>
    byte[] LabelSigningBytes(string labelJson);

    /// <summary>Verifies a label's signature against <paramref name="didKey"/>.</summary>
    LabelStatus VerifyLabel(string labelJson, string didKey);

    /// <summary>
    /// Verifies a label against its issuer's DID document, resolved as <paramref name="documentJson"/>
    /// (<see langword="null"/>: the issuer does not resolve), and as <paramref name="freshDocumentJson"/>
    /// when the document is fetched again (<see langword="null"/>: unchanged).
    /// </summary>
    LabelStatus VerifyLabelAgainstIssuer(string labelJson, string? documentJson, string? freshDocumentJson);

    /// <summary>
    /// Signs a label (its JSON form, without <c>sig</c>) as its <c>src</c> with the given key
    /// (<paramref name="curve"/> <c>K-256</c> or <c>P-256</c>, the private scalar in hex), and
    /// returns the signed label's JSON.
    /// </summary>
    string SignLabel(string labelJson, string curve, string privateKeyHex);

    /// <summary>
    /// Encodes a <c>subscribeLabels</c> frame: <c>{t: "#labels", seq, labels}</c>,
    /// <c>{t: "#info", name, message}</c>, or an error frame <c>{error, message}</c>. Returns
    /// <see langword="null"/> when the message cannot be sent (an unsigned label, an info without a name).
    /// </summary>
    byte[]? EncodeLabelFrame(string messageJson);
}

/// <summary>The outcomes of verifying a label.</summary>
public enum LabelStatus
{
    /// <summary>The signature verifies.</summary>
    Valid,
    /// <summary>The signature does not verify.</summary>
    InvalidSignature,
    /// <summary>The label carries no signature.</summary>
    Unsigned,
    /// <summary>The label's <c>ver</c> is not 1.</summary>
    UnsupportedVersion,
    /// <summary>The issuer publishes no usable <c>#atproto_label</c> key.</summary>
    NoLabelKey,
    /// <summary>The issuer's DID does not resolve.</summary>
    IssuerUnresolved,
    /// <summary>The label is malformed.</summary>
    Malformed,
}
