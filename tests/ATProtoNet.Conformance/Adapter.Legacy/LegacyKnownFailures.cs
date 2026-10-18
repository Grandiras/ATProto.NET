namespace ATProtoNet.Conformance.Legacy;

/// <summary>
/// Vectors 0.8 fails, each with the issue filed for it (<c>KnownLegacyFailure</c> in
/// design/05-rewrite-plan.md, phase 0 gate).
/// </summary>
internal static class LegacyKnownFailures
{
    private const string NoStrictReader = "0.8 has no strict DRISL reader for general values (DagCborDecoder is lax; strict reading exists only inside CommitBlock and MstNodeData).";

    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        ["syntax/language_*"] = "0.8 has no type for the Lexicon language format.",
        ["syntax/uri_*"] = "0.8 has no type for the Lexicon uri format.",

        // Layer 0: data model, CID and CAR, crypto, MST, commits and proofs, hostile input.
        ["data-model/drisl-strict.json#*"] = NoStrictReader,
        ["hostile/drisl-strict.json#*"] = NoStrictReader,
        ["data-model/data-model-invalid.json#top-level not an object"] = "0.8 has no data-model validation beyond encodability: DagCborEncoder encodes any JSON value, not only a map.",
        ["data-model/data-model-invalid.json#record with $type null"] = "0.8 has no data-model validation of $type: a null $type is encoded as is.",
        ["data-model/data-model-invalid.json#record with $type wrong type"] = "0.8 has no data-model validation of $type: an integer $type is encoded as is.",
        ["data-model/data-model-invalid.json#record with empty $type string"] = "0.8 has no data-model validation of $type: an empty $type is encoded as is.",
        ["data-model/data-model-invalid.json#blob with string size"] = "0.8 has no data-model validation of blobs: a string size is encoded as is.",
        ["data-model/data-model-invalid.json#blob with missing key"] = "0.8 has no data-model validation of blobs: a blob without ref is encoded as is.",
        ["data-model/data-model-invalid.json#bytes with wrong field type"] = "#231: {\"$bytes\": [1,2,3]} is encoded as an ordinary map; the interop suite calls it invalid (DAT-47).",
        ["data-model/data-model-invalid.json#bytes with extra fields"] = "#231: a $bytes object with another field is encoded as an ordinary map; the interop suite calls it invalid (DAT-47).",
        ["data-model/data-model-invalid.json#link with wrong field type"] = "#231: {\"$link\": 1234} is encoded as an ordinary map; the interop suite calls it invalid (DAT-47).",
        ["data-model/data-model-invalid.json#link with extra fields"] = "#231: a $link object with another field is encoded as an ordinary map; the interop suite calls it invalid (DAT-47).",
        ["data-model/data-model-valid.json#float, but integer-like"] = "#231: DagCborEncoder rejects the JSON number 123.0; the interop suite calls it a valid integer (DAT-2).",
        ["data-model/drisl-encode.json#link not a blessed CID"] = "#226: DagCborEncoder writes a $link whose CID is not a blessed atproto CID (dag-pb codec 0x70) instead of rejecting it.",
        ["data-model/drisl-decode.json#link not a blessed CID"] = "#226: DagCborDecoder reads a tag-42 link to a dag-pb (0x70) CID as {\"$link\": \"bafybei…\"} instead of rejecting it.",
        ["data-model/drisl-decode.json#duplicate key"] = "#227: DagCborDecoder accepts duplicate map keys and returns JSON with the key twice ({\"a\":1,\"a\":2}).",
        ["data-model/drisl-decode.json#indefinite text"] = "#228: DagCborDecoder (lax) rejects indefinite-length text strings though it accepts indefinite arrays and maps.",
        ["data-model/drisl-decode.json#indefinite bytes"] = "#228: DagCborDecoder (lax) rejects indefinite-length byte strings though it accepts indefinite arrays and maps.",
        ["cid-car/car-read.json#zero block length"] = "#229: CarReader stops at a zero-length block section and silently ignores the rest of the file instead of rejecting it.",
        ["cid-car/car-read.json#header keys out of canonical order"] = "#230: CarReader reads the header with a lax CborReader, so a non-canonical header (keys out of order) is accepted.",
        ["repo/commit-blocks.json#did not a DID"] = "0.8 has no did syntax check when reading a commit block (CommitBlock keeps did as a string; REP-29): reported as a bad signature, not malformed.",
        ["repo/commit-blocks.json#rev not a TID"] = "0.8 has no rev syntax check when reading a commit block (only RecordProof checks it; REP-29): reported as a bad signature, not malformed.",
        ["repo/commit-blocks.json#keys out of canonical order"] = "#230: CommitBlock.Read uses CborConformanceMode.Strict, which does not enforce canonical key order, so a reordered commit is a bad signature rather than malformed.",

        // Firehose, Jetstream, labels, tokens, Lexicon and Spaces.
        ["streaming/jetstream-events.json#v2 commit with a non-numeric seq"] =
            "#232: a Jetstream v2 event whose seq is not a number is delivered without a cursor instead of dropped (STR-71).",
        ["lexicon/interop/lexicon-*"] = "0.8 has no Lexicon schema validation.",
        ["lexicon/interop/record-data-*"] = "0.8 has no record validation against Lexicon schemas.",
    };
}
