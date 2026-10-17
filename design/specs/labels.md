# Labels

| | |
|---|---|
| **Status** | Draft |
| **Packages** | `ATProtoNet` (namespaces `ATProtoNet.Labels` and `ATProtoNet.Streaming.Labels`), `ATProtoNet.AspNetCore` (namespace `ATProtoNet.AspNetCore.Labels`) |
| **Phase** | 6 (signing, verification, codec); 7 (stream consumer); 9 (labeler endpoints) |
| **Depends on** | [data-model](data-model.md) (DRISL), [crypto](crypto.md) (`SigningKey`, `SignedObject`), [identity](identity.md) (`IDidResolver`, verify-with-refresh), [streaming](streaming.md), [server](server.md) |
| **Normative references** | [Labels](https://atproto.com/specs/label) · [Event stream](https://atproto.com/specs/event-stream) · Lexicons `com.atproto.label.defs`, `com.atproto.label.queryLabels`, `com.atproto.label.subscribeLabels` · reference vectors from indigo and `@atproto/ozone` |

## Scope

**In:** the bytes a label signature covers; signing; verification against the issuer's
`#atproto_label` key; the `subscribeLabels` frame codec, used by both the stream consumer and the
server; the label-stream consumer's label-specific rules; the `queryLabels` and
`subscribeLabels` server endpoints.

**Out:** the label *model* (generated from `com.atproto.label.defs#label`,
[lexicon](lexicon.md)); the `atproto-accept-labelers` request header and labeler routing
([xrpc-client](xrpc-client.md)); labeler declarations (`app.bsky.labeler.service`) and label value
definitions (generated with the `app.bsky` Lexicons, `ATProtoNet.Lexicons.App.Bsky.Labeler`); self-labels in records (plain Lexicon data);
the generic stream runtime ([streaming](streaming.md)) and XRPC subscription hosting
([server](server.md)).

### Ownership

| Concern | Where | Why there |
|---|---|---|
| `LabelSigning` (signed bytes), `LabelSigner`, `LabelVerifier`, `LabelFrameCodec` | `ATProtoNet`, namespace `ATProtoNet.Labels` | Needs the generated `Label` type and the resolver; both the consumer and the server use it. One codec, not an encoder in one place and a decoder in another (0.8 split them between `Labeling/` and `Streaming/`). |
| `LabelStream` consumer | `ATProtoNet`, namespace `ATProtoNet.Streaming.Labels` | Runs on the shared stream runtime. |
| `queryLabels`, `subscribeLabels` endpoints, `ILabelSource` | `ATProtoNet.AspNetCore`, namespace `ATProtoNet.AspNetCore.Labels` | Server hosting needs ASP.NET Core. |

## Public API sketch

```csharp
namespace ATProtoNet.Labels;

public static class LabelSigning
{
    public const int Version = 1;
    public const int MaxValueBytes = 128;
    public static byte[] GetSigningBytes(Label label);                         // LBL-1..LBL-4
    public static LabelVerificationStatus VerifyWithKey(Label label, VerificationKey key);
}

public sealed class LabelSigner(Did labeler, SigningKey key, TimeProvider? time = null)
{
    public Did Labeler { get; }
    public DidKey PublicKey { get; }                                            // publish as #atproto_label
    public Label Sign(LabelRequest request);                                    // stamps cts, sets ver
    public Label Sign(Label label);                                             // re-sign an existing label
}
public sealed record LabelRequest
{
    public required string Subject { get; init; }                               // at:// URI or DID
    public required string Value { get; init; }
    public Cid? Cid { get; init; }
    public bool Negate { get; init; }
    public AtDatetime? ExpiresAt { get; init; }
}

public sealed class LabelVerifier(IDidResolver resolver)
{
    public Task<LabelVerificationResult> VerifyAsync(Label label, CancellationToken ct = default);
    public Task<IReadOnlyList<LabelVerificationResult>> VerifyAllAsync(IEnumerable<Label> labels, CancellationToken ct = default);
}
public enum LabelVerificationStatus { Valid, Unsigned, UnsupportedVersion, Malformed, NoLabelKey, IssuerUnresolved, InvalidSignature }
public sealed record LabelVerificationResult(Label Label, LabelVerificationStatus Status, DidKey? KeyTried, Exception? Error)
{
    public bool IsValid => Status == LabelVerificationStatus.Valid;
}

public closed record LabelStreamMessage;                                        // closed (AG-6a)
public sealed record LabelsMessage(long Seq, IReadOnlyList<Label> Labels) : LabelStreamMessage;
public sealed record LabelInfoMessage(string Name, string? Message) : LabelStreamMessage;
public static class LabelFrameCodec
{
    public static byte[] Encode(LabelStreamMessage message);
    public static byte[] EncodeError(string error, string? message = null);
    public static LabelFrameReadResult Decode(ReadOnlySpan<byte> frame);        // message, error frame, or drop reason
}

namespace ATProtoNet.Streaming.Labels;

public sealed class LabelStream(LabelStreamOptions options)                     // runtime from streaming spec
{
    public IAsyncEnumerable<LabelStreamEvent> ReadAsync(long? cursor = null, CancellationToken ct = default);
}
// LabelStreamEvent = LabelsReceived (labels + optional verification results) | stream notices (D-23)

namespace ATProtoNet.AspNetCore.Labels;

public interface ILabelSource
{
    ValueTask<LabelPage> QueryAsync(LabelQuery query, CancellationToken ct);
    IAsyncEnumerable<SequencedLabels> ReadSequencedAsync(long? after, CancellationToken ct); // for subscribeLabels
    ValueTask<long?> GetLatestSeqAsync(CancellationToken ct);
    ValueTask<long?> GetOldestSeqAsync(CancellationToken ct);                   // retention window start
}
// builder.AddLabeler<TSource>(o => o.Signer = ...); app.MapAtProtoXrpc() serves both methods
```

## Requirements

### Signed bytes

- **LBL-1** (MUST) The signed bytes are the canonical DRISL encoding of the label's schema fields
  only (`ver`, `src`, `uri`, `cid`, `val`, `neg`, `cts`, `exp`), without `sig`, without `$type`,
  and without fields the schema does not declare (even when the label carries them as unknown
  fields).
  *Source:* [Label spec, signatures](https://atproto.com/specs/label) · 0.8 `LabelSigning.cs:10-17` ·
  test `LabelSigningInteropTests.GetSigningBytes_IgnoresTheSignatureAndUndeclaredFields`
- **LBL-2** (MUST) An absent optional field is omitted; a present one is encoded even when it holds
  its default, so a label carrying `"neg": false` is verified over bytes that include it.
  *Source:* reference implementations · test `GetSigningBytes_ExplicitNegFalse_IsEncodedAsCarried`
- **LBL-3** (MUST) `cid` is encoded as a text string, not a tag-42 link (the label names a version of
  its subject; it does not link to it).
  *Source:* `com.atproto.label.defs#label` (`format: cid` string) · 0.8 `LabelSigning.cs:134`
- **LBL-4** (MUST) The encoding matches the reference vectors byte for byte (all keys are three
  bytes, so DRISL order is alphabetical).
  *Source:* tests `GetSigningBytes_IndigoVector_MatchesTheReferenceEncoding`,
  `GetSigningBytes_NegatedLabel_MatchesTheReferenceEncoding`, `GetSigningBytes_EveryOptionalField_MatchesTheReferenceEncoding`
- **LBL-5** (MUST) The signature is ECDSA over SHA-256 of the signed bytes, in 64-byte low-S `r‖s`
  form, produced and checked through `SignedObject` ([crypto](crypto.md)). A high-S form of a valid
  signature verifies as invalid (labels are content-addressed data, like commits).
  *Source:* Label spec · tests `Verify_HighSFormOfAValidSignature_IsInvalid`,
  `Sign_ReferenceKey_SignsTheReferenceBytesWithALowSSignature`
- **LBL-6** (MUST) A label without `src`, `uri`, `val` or `cts`, or with text that is not valid
  Unicode (a lone surrogate), has no signed bytes: signing throws `ArgumentException`, verification
  reports `Malformed`.
  *Source:* 0.8 `LabelSigning.cs:32-35,177`

### Signing

- **LBL-7** (MUST) `LabelSigner` sets `ver = 1`, stamps `cts` from `TimeProvider` for new labels,
  omits `neg` unless it is `true` (as the reference signer does), replaces any existing signature
  and drops undeclared fields.
  *Source:* tests `LabelSignerTests.Sign_Label_SetsVersionAndAVerifyingSignature`,
  `Sign_Subject_StampsTheCurrentTimeAndSetsTheFields`, `Sign_NotNegated_OmitsNeg`,
  `Sign_ReplacesAnExistingSignatureAndDropsUndeclaredFields`, `LabelSigningInteropTests.Sign_NegFalse_DropsItAsTheReferenceSignerDoes`
- **LBL-8** (MUST) Signing refuses: a label whose `src` is not the signer's labeler DID; a `ver`
  other than 1; an empty value or one over 128 UTF-8 bytes; an empty subject; `cts` or `exp` that are
  not valid datetimes.
  *Source:* Label spec (`val` 128 bytes max) · tests `Sign_AnotherLabelersLabel_Throws`, `Sign_UnknownVersion_Throws`,
  `Sign_EmptyValue_Throws`, `Sign_ValueLength_IsBoundedAt128Utf8Bytes`, `Sign_InvalidTimestamps_Throw`, `Sign_EmptySubject_Throws`
- **LBL-9** (MUST) `LabelSigner` is safe for concurrent use without locking by the caller.
  *Source:* test `Sign_ConcurrentCalls_AllVerify`; **Decision**: 0.8 locked around a non-thread-safe
  `AtProtoKey` (`LabelSigner.cs:296`); `SigningKey` is thread-safe ([crypto](crypto.md)), and a
  public-only key cannot be passed at all (0.8 `Sign_PublicKeyOnly_Throws` becomes a compile-time
  guarantee)
- **LBL-10** (MUST) A signed label survives a JSON round trip (`sig` as `$bytes`, unpadded) and still
  verifies.
  *Source:* test `Sign_SignedLabel_SurvivesAJsonRoundTrip`

### Verification

- **LBL-11** (MUST) Checks that need no key run first and resolve nothing: no or empty `sig` →
  `Unsigned`; `ver` missing or not 1 → `UnsupportedVersion`; LBL-6 → `Malformed`.
  *Source:* 0.8 `LabelVerifier.cs:8-36` · tests `Verify_NoOrEmptySignature_ReportsUnsigned`,
  `Verify_VersionOtherThanOne_ReportsUnsupportedVersion`, `VerifyAsync_UnsignedOrUnversioned_ResolvesNothing`
- **LBL-12** (MUST) The key is the issuer's (`src`) `#atproto_label` verification method, read with the
  strict lookup (`TryGetVerificationKey`). There is **no** fallback to `#atproto`; a label signed with
  the account key is invalid.
  *Source:* Label spec (labeler service identity) · tests `VerifyAsync_NoLabelKeyPublished_DoesNotFallBackToTheAccountKey`,
  `VerifyAsync_SignedWithTheAccountKey_IsNotValid`, `Verify_BlueskyModerationLabel_AgainstTheAccountKey_IsInvalid`
- **LBL-13** (MUST) Verification uses verify-with-refresh ([identity](identity.md) IDN-44): a label
  that fails against a cached key, or whose issuer published no key in the cached document, triggers
  one rate-limited refetch, and is retried only if the key changed.
  *Source:* Label spec · tests `VerifyAsync_KeyRotatedSinceCached_RefetchesOnceAndVerifies`,
  `VerifyAsync_LabelKeyPublishedSinceCached_RefetchesAndVerifies`, `VerifyAsync_SignedBeforeARotation_IsInvalidAfterOneRefetch`
- **LBL-14** (MUST) Outcomes after the key step: verifies → `Valid`; no usable `#atproto_label` key
  even after the refetch (absent or malformed) → `NoLabelKey`; the issuer does not resolve →
  `IssuerUnresolved` with the resolution error attached; otherwise `InvalidSignature`. A label signed
  under a rotated-away key is `InvalidSignature` until the labeler re-signs it; the spec accepts this.
  *Source:* tests `VerifyAsync_MalformedLabelKey_ReportsNoLabelKey`, `VerifyAsync_IssuerDoesNotResolve_ReportsTheResolutionError`,
  `VerifyAsync_TamperedLabel_IsInvalid`
- **LBL-15** (MUST) Verification returns a result for every label and never throws for a bad label;
  `VerifyAllAsync` keeps input order. Caller cancellation throws `OperationCanceledException`.
  *Source:* tests `VerifyAllAsync_KeepsTheOrderAndEveryLabel`, `VerifyAsync_Cancelled_Throws`
- **LBL-16** (MUST) The reference vectors verify: the indigo vector, live Bluesky moderation labels
  against their published documents, and Ozone `signLabel` output with and without `neg`.
  *Source:* tests `Verify_IndigoVector_IsValid`, `Verify_IndigoVectorWithAChangedValue_IsInvalid`,
  `Verify_BlueskyModerationLabel_IsValid`, `Verify_OzoneSignedLabel_IsValid`, `Verify_OzoneSignedLabelWithoutNeg_IsValid`

### Frame codec

- **LBL-17** (MUST) A `#labels` frame is the DRISL header `{op: 1, t: "#labels"}` followed by the body
  `{seq, labels}`; an `#info` frame is `{op: 1, t: "#info"}` followed by `{name, message?}`; an error
  frame is `{op: -1}` followed by `{error, message?}`. Encoded frames match the reference frames byte
  for byte.
  *Source:* [Event stream spec](https://atproto.com/specs/event-stream) · tests
  `LabelStreamFramesTests.Encode_Labels_MatchesTheReferenceFrame`, `Encode_Info_MatchesTheReferenceFrame`,
  `EncodeError_MatchesTheReferenceFrame`
- **LBL-18** (MUST) The encoder refuses a `#labels` message containing a label that is unsigned or has
  no `ver` (a service hands only signed labels to another), and an `#info` message without a name.
  *Source:* Label spec ("signatures should be validated when labels are transferred between
  services") · tests `Encode_UnsignedLabel_Throws`, `Encode_InfoWithoutAName_Throws`
- **LBL-19** (MUST) The decoder binds frames directly from DRISL (no JSON transcoding,
  [D-10](../06-decisions.md#d-10-serialization-systemtextjson-with-source-generation-generated-union-converters)),
  applies the event-stream frame rules of [streaming](streaming.md) (unreadable header, unknown `t`,
  body that does not bind → a drop reason, not an exception; error frame → an error result), and
  reads frames the encoder wrote.
  *Source:* test `Encode_Frames_AreReadBackByTheConsumerAndVerify`

### Label stream consumption

- **LBL-20** (MUST) The label stream follows the shared stream runtime's delivery, cursor and
  reconnect rules; the cursor is `seq`; an explicit cursor wins over a stored one; events with
  `seq` ≤ the current position are skipped (inclusive replay).
  *Source:* 0.8 `LabelStreamConsumer.cs:132` · tests `EventStreamLoopTests.LabelStreamConsumer_ResumesAndPersistsTheSequenceNumber`,
  `LabelStreamConsumer_ParsesLabelsAndInfo`
- **LBL-21** (MUST) With a verifier configured, every delivered `LabelsReceived` event carries one
  verification result per label. Labels that fail verification are **delivered with their result,
  not dropped**; without a verifier, results are absent (not "unverified").
  *Source:* 0.8 `LabelStreamConsumer.cs:13-23` · test `LabelStreamFramesTests.ConsumeAsync_WithoutAVerifier_LeavesVerificationUnset`.
  **Pinned difference from the firehose**, which drops commits that fail Sync verification: a
  firehose consumer in verified mode is promised authenticated repository data, while labels are
  advisory annotations whose consumer decides what to trust (the spec expects end clients to rely
  on labelers they chose). Filter with `IsValid`.
- **LBL-22** (MUST) `#info` messages (for example `OutdatedCursor`) are delivered as the stream's
  `StreamInfo` notice, in-band and unsequenced ([D-23](../06-decisions.md#d-23-stream-side-information-is-delivered-in-band)).
  *Source:* D-23

### Labeler endpoints

- **LBL-23** (MUST) `queryLabels` requires `uriPatterns`; each pattern is non-empty and may contain `*`
  only as its last character (`*` alone matches everything); otherwise 400 `InvalidRequest`.
  `sources` filters by labeler DID.
  *Source:* reference labeler · 0.8 `QueryLabelsEndpoint.cs:114-124` · tests
  `QueryLabelsEndpointTests.Query_InvalidRequest_Answers400`, `Query_PrefixAndExactPatterns_MatchAsTheLexiconDescribes`,
  `Query_Wildcard_ReturnsEveryLabelSignedByItsLabeler`, `Query_Sources_FiltersByLabeler`
- **LBL-24** (MUST) `limit` defaults to 50; values above 250 are served as 250; values below 1 are 400
  `InvalidRequest`. Paging uses the source's cursor.
  *Source:* 0.8 `QueryLabelsEndpoint.cs:59-62,133` · tests `Query_Limit_DefaultsTo50AndIsCappedAt250`, `Query_LimitAndCursor_Page`
- **LBL-25** (MUST) When a signer is registered, labels whose `src` is the signer's labeler and that
  carry no signature are signed on the way out; already-signed labels and labels of other sources are
  served as stored. Without a signer, labels are served as stored.
  *Source:* 0.8 `QueryLabelsEndpoint.cs:99-109` · tests `Query_AlreadySignedLabel_IsServedAsStored`,
  `Query_WithoutASigner_ServesLabelsAsStored`
- **LBL-26** (MUST) `subscribeLabels` is served through the server's XRPC subscription support
  ([server](server.md)): with no cursor it streams new labels; with a cursor it replays every sequenced
  label after it from the source, then continues live without a gap or duplicate; a cursor beyond the
  latest `seq` gets the error frame `FutureCursor` and the connection closes; a cursor older than the
  source's retention gets an `#info` `OutdatedCursor` message and replay starts at the oldest
  available `seq`. Every label sent is signed (LBL-18), signing on the way out as in LBL-25.
  *Source:* [Event stream spec, sequence numbers](https://atproto.com/specs/event-stream) · **Decision**:
  new in 0.9; 0.8 could not serve it although `docs/xrpc-handlers.md` claimed it did
- **LBL-27** (SHOULD) The labeler's DID document publishes the signer's key as `#atproto_label` and the
  service as `#atproto_labeler` / `AtprotoLabeler`; the hosting docs show how, and a startup check MAY
  warn when the configured labeler DID's document does not publish the signer's key.
  *Source:* Label spec (labeler service identity)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `LabelSigning.MaxValueBytes` | 128 UTF-8 bytes | LBL-8 |
| `LabelSigning.Version` | 1 | LBL-7, LBL-11 |
| `QueryLabels.DefaultLimit` / `MaxLimit` | 50 / 250 | LBL-24 |
| Frame size, nesting depth | per [streaming](streaming.md) and [data-model](data-model.md) | LBL-19 |

## Errors

| Failure | Result |
|---|---|
| Signing an invalid or foreign label | `ArgumentException` |
| Verifying a bad label | a `LabelVerificationResult` status, never an exception |
| Issuer resolution failure during verification | `IssuerUnresolved` with the `IdentityResolutionException` attached |
| Encoding an unsigned label or nameless info | `ArgumentException` |
| Malformed frame on decode | drop reason (consumer reports `EventDropped` notice if requested) |
| Error frame | `StreamException` from the stream runtime, retryable per its rules |
| Bad `queryLabels` input | XRPC 400 `InvalidRequest` |

## Conformance and tests

**Vectors:** the indigo label vector, live Bluesky moderation labels with their DID documents, Ozone
`signLabel` outputs (currently pasted into `LabelSigningInteropTests`; extract to
`vectors/labels/`), and the reference `#labels`/`#info`/error frames from `LabelStreamFramesTests`.
Adapter operation: `VerifyLabel`, plus `LabelSigningBytes` and `EncodeLabelFrame`.

**0.8 scenarios to port:** `LabelSignerTests`, `LabelSigningInteropTests`, `LabelVerifierTests`,
`LabelStreamFramesTests`, the label cases of `EventStreamLoopTests`, `QueryLabelsEndpointTests`. New:
`subscribeLabels` server tests (cursor replay, `FutureCursor`, `OutdatedCursor`, backfill-to-live
without gap).

## Changes from 0.8

| Change | Reason |
|---|---|
| Encoder and decoder in one `LabelFrameCodec` in `ATProtoNet` | 0.8 split encoding (`Labeling/`) from decoding (`Streaming/LabelStreamConsumer`) |
| Frames decoded directly from DRISL | D-10; no CBOR → JSON transcoding |
| `SigningKey`/`VerificationKey` types instead of `AtProtoKey` and `did:key` strings | thread safety without locks, public-only keys rejected at compile time, AG-9a |
| `subscribeLabels` can be served | the server gains subscription support; 0.8's docs claimed this and the code could not |
| Verification results carry `DidKey? KeyTried` and the resolution exception | typed keys; error detail without a `DidResolutionException`-typed property |

## Open points

1. **Effective label set helper.** Provide a `LabelSet` that applies negation (`neg` cancels an
   earlier label with the same `src`, `uri`, `val`) and expiry (`exp`, against `TimeProvider`) for
   consumers? Proposed: yes, small and pure, in `ATProtoNet.Labels` in phase 6.
2. **Label value syntax.** Validate `val` against the spec's preferred syntax (lowercase ASCII,
   hyphens, `!` prefix for system labels) when signing? Proposed: warn-level validation only; the
   spec recommends rather than requires it.
3. **Startup key check** (LBL-27): on by default or opt-in? Proposed: opt-in, since it needs network
   access at startup.
