namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Converts a value in the JSON form of the data model (<c>$link</c>, <c>$bytes</c>) to
    /// canonical DRISL-CBOR, or returns <see langword="null"/> when it is not a data-model value.
    /// </summary>
    byte[]? EncodeDataModel(string json);

    /// <summary>
    /// Reads one DRISL-CBOR value into the JSON form of the data model, or returns
    /// <see langword="null"/> when <paramref name="mode"/> rejects the bytes. Any other failure
    /// (an exception, a crash) is a conformance failure.
    /// </summary>
    string? DecodeDataModel(ReadOnlySpan<byte> cbor, DrislMode mode);
}

/// <summary>How strictly DRISL-CBOR is read.</summary>
public enum DrislMode
{
    /// <summary>Every DRISL rule: canonical encoding only.</summary>
    Strict,
    /// <summary>Non-canonical but well-formed encodings accepted, as for event-stream envelopes.</summary>
    Lenient,
}
