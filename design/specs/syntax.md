# Identifier syntax — spec

**Status:** Accepted (maintainer, 2026-10-17) · **Package:** `ATProtoNet` (namespace `ATProtoNet`, foundation layer) · **Phase:** 1 (space addresses included)

**Normative references:** [DID](https://atproto.com/specs/did) ·
[Handle](https://atproto.com/specs/handle) · [NSID](https://atproto.com/specs/nsid) ·
[Record Key](https://atproto.com/specs/record-key) · [TID](https://atproto.com/specs/tid) ·
[AT URI](https://atproto.com/specs/at-uri-scheme) ·
[Atproto Spaces (proposal 0016)](spaces.md) (space addresses) ·
[Lexicon string formats](https://atproto.com/specs/lexicon#string-formats) (incl. `datetime`,
`uri`, `language`) · [Data Model: CID formats](https://atproto.com/specs/data-model#link-and-cid-formats) ·
[RFC 3339](https://www.rfc-editor.org/rfc/rfc3339) · [RFC 5646 (BCP 47)](https://www.rfc-editor.org/rfc/rfc5646) ·
[RFC 3986](https://www.rfc-editor.org/rfc/rfc3986)

## Scope

**In:** the vocabulary types `Did`, `Handle`, `AtIdentifier`, `Nsid`, `RecordKey`, `Tid`,
`TidGenerator`, `AtUri`, `SpaceUri` and `SpaceRecordUri` (addresses of spaces and of records in
spaces), the `AtAddress` union over all three, `AtDatetime`,
`Language`; `DecimalString` (a number carried in a Lexicon `string`, a community convention since
the data model has no floats); the string syntax of `Cid`; the generic
Lexicon `uri` format check; the System.Text.Json converters for all of them; the public
`IAtIdentifier<TSelf>` constraint.

**Out:** the binary form, computation and verification of CIDs ([data-model.md](data-model.md));
DID documents, DID-method rules beyond generic syntax (`did:plc` and `did:web` specifics),
handle resolution and reserved-TLD refusal ([identity.md](identity.md)); `did:key` and multibase
key encodings ([crypto.md](crypto.md)); how generated Lexicon code maps string formats to these
types ([lexicon.md](lexicon.md)).

## Public API sketch

```csharp
namespace ATProtoNet;

// The constraint generic code writes against (0.8 keeps its equivalent internal).
public interface IAtIdentifier<TSelf> :
    ISpanParsable<TSelf>, IUtf8SpanParsable<TSelf>, IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : IAtIdentifier<TSelf>
{
    string Value { get; }                       // canonical text
    static abstract int MaxLength { get; }
    static abstract TSelf Parse(string s);      // FormatException on invalid input
    static abstract TSelf Parse(ReadOnlySpan<char> s);
    static abstract TSelf Parse(ReadOnlySpan<byte> utf8Text);
    static abstract bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out TSelf result);
    static abstract bool TryParse(ReadOnlySpan<char> s, [MaybeNullWhen(false)] out TSelf result);
    static abstract bool TryParse(ReadOnlySpan<byte> utf8Text, [MaybeNullWhen(false)] out TSelf result);
}

public sealed class Did : IAtIdentifier<Did>
{
    public string Value { get; }
    public string Method { get; }                       // "plc", "web", …
    public ReadOnlySpan<char> MethodSpecificId { get; }
    public static implicit operator string(Did did);
    public static implicit operator AtIdentifier(Did did);
}

public sealed class Handle : IAtIdentifier<Handle>
{
    public static Handle Invalid { get; }               // "handle.invalid"
    public bool IsInvalid { get; }
    public static implicit operator AtIdentifier(Handle handle);
}

public sealed class AtIdentifier : IAtIdentifier<AtIdentifier>
{
    public Did? Did { get; }
    public Handle? Handle { get; }
    [MemberNotNullWhen(true, nameof(Did))] public bool IsDid { get; }
    [MemberNotNullWhen(true, nameof(Handle))] public bool IsHandle { get; }
}

public sealed class Nsid : IAtIdentifier<Nsid>
{
    public string Authority { get; }                    // "app.bsky.feed" as written (domain reversed)
    public string Name { get; }                         // "post"
}

public sealed class RecordKey : IAtIdentifier<RecordKey>
{
    public static RecordKey Self { get; }               // "self", the key of singleton records
    public static RecordKey From(Tid tid);
}

public readonly struct Tid : IAtIdentifier<Tid>      // default == TID 0 ("2222222222222"), valid
{
    public const int Length = 13;
    public long TimestampMicroseconds { get; }
    public int ClockId { get; }
    public long ToInt64();                              // the 64 bits as a signed long
    public static Tid FromInt64(long value);            // any long: the bits are the TID's
    public static Tid Next();                           // TidGenerator.Default.Next()
}

public sealed class TidGenerator
{
    public const int MaxClockId = 1023;
    public static TidGenerator Default { get; }         // immutable configuration, random clock id per process
    public TidGenerator(int? clockId = null, TimeProvider? timeProvider = null);
    public int ClockId { get; }
    public Tid Next();
}

public sealed class AtUri : IAtIdentifier<AtUri>
{
    public AtIdentifier Authority { get; }
    public Nsid? Collection { get; }
    public RecordKey? RecordKey { get; }
    public static AtUri Create(AtIdentifier authority, Nsid? collection = null, RecordKey? recordKey = null);
}

// Space addresses (SYN-74…SYN-81). Parsed by the same scanner as AtUri.
public sealed class SpaceUri : IAtIdentifier<SpaceUri>              // a space
{
    public Did Authority { get; }                       // the space authority (never a handle)
    public Nsid SpaceType { get; }
    public RecordKey Skey { get; }
    public static SpaceUri Create(Did authority, Nsid spaceType, RecordKey skey);
    public SpaceRecordUri Record(Did author, Nsid collection, RecordKey rkey);
}
public sealed class SpaceRecordUri : IAtIdentifier<SpaceRecordUri>  // a record in a space
{
    public SpaceUri Space { get; }
    public Did Author { get; }                          // never a handle
    public Nsid Collection { get; }
    public RecordKey Rkey { get; }
    public string Path { get; }                         // "{collection}/{rkey}"
}

// Any atproto address: one entry point for both data protocols (SYN-75, SYN-81).
public union AtAddress(AtUri, SpaceUri, SpaceRecordUri)
{
    public static AtAddress Parse(string value);
    public static bool TryParse([NotNullWhen(true)] string? value, out AtAddress address);
}

public sealed class AtDatetime : IAtIdentifier<AtDatetime>, IComparable<AtDatetime>
{
    public string Value { get; }                        // the text exactly as written or read
    public bool IsValid { get; }                        // false only for values read leniently off the wire
    public bool TryGetInstant(out DateTimeOffset instant);
    public static AtDatetime Now(TimeProvider? timeProvider = null);
    public static AtDatetime From(DateTimeOffset value);
    public static AtDatetime From(DateTime value);
}

public sealed class Language : IAtIdentifier<Language> { }   // BCP 47 language tag

// ATProtoNet.Data: a number written as text in a Lexicon string field (SYN-82…SYN-87).
public sealed class DecimalString : IAtIdentifier<DecimalString>
{
    public string Value { get; }                        // the text exactly as written or read
    public bool IsValid { get; }                        // false only for values read leniently off the wire
    public bool TryGetDouble(out double value);         // invariant culture, finite results only
    public bool TryGetDecimal(out decimal value);
    public static DecimalString From(double value);     // shortest round-trip text, invariant
    public static DecimalString From(decimal value);    // keeps the scale: 52.520m → "52.520"
}

// Public JSON converters, so source-generated contexts use them without reflection: IdentifierJsonConverter<T> (every
// IAtIdentifier type but AtDatetime and DecimalString), AtDatetimeJsonConverter, AtAddressJsonConverter, and
// DecimalStringJsonConverter in ATProtoNet.Data. All support dictionary keys.

public static class UriFormat
{
    public const int MaxLength = 8192;
    public static bool IsValid(ReadOnlySpan<char> value);    // Lexicon "uri" format
}
```

`Cid` is specified in [data-model.md](data-model.md#cid); its string syntax is here
(SYN-60…SYN-63).

## Requirements

### Common to all identifier types

- **SYN-1** (MUST) Identifier syntax is ASCII only. Any non-ASCII character makes the value
  invalid.
  *Source:* atproto syntax specs · 0.8 `IdentifierSyntax.cs:17-26` · `SyntaxInteropTests`
- **SYN-2** (MUST) A value containing a line break (CR or LF) anywhere, including at the end, is
  invalid for every type. Validators MUST NOT use end-anchored regular expressions that admit a
  trailing newline.
  *Source:* 0.8 regression (a `$`-anchored regex accepted `did:plc:abc\n`) · test
  `IdentifierLineBreakTests.TryParse_ValueWithALineBreak_IsRefused`
- **SYN-3** (MUST) `Parse` throws `FormatException` for invalid input and `ArgumentNullException`
  for `null`, and nothing else. `TryParse` never throws.
  *Source:* **Decision** [AG-8](../03-api-guidelines.md#types) · 0.8 threw `ArgumentException`
  from `T.Parse` but `FormatException` from `IParsable.Parse` (`IIdentifier.cs:33-66`) ·
  test `IdentifierContractTests.Parse_NullInput_ThrowsArgumentNullException`
- **SYN-4** (MUST) Every type offers `Parse`/`TryParse` for `string`, `ReadOnlySpan<char>` and
  UTF-8 `ReadOnlySpan<byte>`. All three accept and reject exactly the same inputs.
  *Source:* **Decision** AG-8 · 0.8 `IdentifierSyntax` is already generic over `char` and `byte`
- **SYN-5** (MUST) Identifier types are sealed classes, not `record`s, except `Tid`, which is a
  `readonly struct` because its `default` is a valid TID. None exposes a public constructor
  that skips validation.
  *Source:* **Decision** AG-8 · 0.8 mixed `sealed record` with a `record struct` `AtDatetime`
  whose `default` was invalid (`AtDatetime.cs:24`)
- **SYN-6** (MUST NOT) No implicit or explicit conversion from `string` exists. An implicit
  conversion to `string` exists for every type. `Did` and `Handle` convert implicitly to
  `AtIdentifier` (it cannot fail).
  *Source:* **Decision** [D-11](../06-decisions.md#d-11-no-implicit-conversions-from-string) ·
  0.8 test `IdentifierContractTests.ExplicitConversionsFromString_ParseOrThrow` (behaviour
  removed)
- **SYN-7** (MUST) Equality and hash codes compare the canonical `Value` ordinally. `CompareTo`
  is ordinal on `Value` unless a type below says otherwise (`Tid`, `AtDatetime`).
  *Source:* 0.8 test `IdentifierContractTests.Sort_UsesOrdinalOrder`
- **SYN-8** (MUST) Validators are hand-written scanners (no regex at run time). For each grammar,
  a test holds the scanner to the reference regular expression from the atproto spec over the
  interop fixtures plus at least 2,000 seeded mutations per grammar.
  *Source:* 0.8 test `IdentifierSyntaxTests.Validator_FixturesAndGeneratedInput_MatchesTheReferencePattern`
- **SYN-9** (MUST) Validators check the maximum length before scanning the value.
  *Source:* principle 2 ([00 — Vision](../00-vision.md#design-principles))
- **SYN-10** (MUST) `IAtIdentifier<TSelf>` is public, so callers can write generic code (for
  example, a JSON converter or a route-value binder) over every identifier type.
  *Source:* **Decision** AG-8 · 0.8 kept `IIdentifier<T>` internal (primitives audit, problem 3)

### DID

- **SYN-11** (MUST) A DID matches `^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$` and is between
  7 and 2,048 characters long.
  *Source:* [specs/did](https://atproto.com/specs/did) · 0.8 `IdentifierSyntax.cs:44` ·
  vectors `did_syntax_valid.txt`, `did_syntax_invalid.txt`
- **SYN-12** (MUST) A DID ending in `%` is invalid. Percent-escape sequences elsewhere are not
  further validated at the syntax level.
  *Source:* specs/did · 0.8 test `DidTests.Parse_EndingInPercent_Throws`
- **SYN-13** (MUST NOT) Syntax validation does not check method-specific rules (such as
  `did:plc` identifiers being 24 base32 characters). A syntactically valid DID with an
  unsupported method parses; resolution reports the problem.
  *Source:* [specs/lexicon](https://atproto.com/specs/lexicon#string-formats) ("the most
  expansive identifier syntax should be permitted") · method rules: [identity.md](identity.md)
- **SYN-14** (MUST) DIDs are case-sensitive and never normalised. `Method` and
  `MethodSpecificId` expose the two parts.
  *Source:* specs/did · 0.8 test `DidTests.Parse_ExtractsMethodAndSpecificId`

### Handle

- **SYN-15** (MUST) A handle has at least two labels separated by `.`. Each label is 1–63
  characters of `[a-zA-Z0-9-]`, not starting or ending with `-`. The last label (TLD) does not
  start with a digit. The whole handle is at most 253 characters.
  *Source:* [specs/handle](https://atproto.com/specs/handle) · 0.8 `IdentifierSyntax.cs:64` ·
  vectors `handle_syntax_*.txt`
- **SYN-16** (MUST) Parsing strips exactly one leading `@` and lower-cases the handle with the
  invariant culture. `Value` is always the normalised form. The same rules apply to user input
  and to values read from JSON: there is one parse path.
  *Source:* specs/handle (handles are case-insensitive, normalised to lower case) · 0.8
  `Handle.cs:50,61` · test `HandleTests.Parse_NormalizesCaseAndALeadingAt`,
  `SyntaxInteropTests.HandleTryParse_ValidFixture_AcceptsAndLowerCases`
- **SYN-17** (MUST) `Handle.Invalid` is the value `handle.invalid`, and `IsInvalid` is true for
  it. Resolution uses it for handles that fail bidirectional verification.
  *Source:* specs/handle · 0.8 `Handle.Invalid`
- **SYN-18** (MUST) Reserved and special-use TLDs (`.local`, `.onion`, `.test`, …) are
  syntactically valid. Refusing to resolve them is identity's job.
  *Source:* specs/handle · 0.8 behaviour (primitives audit §3) · refusal list:
  [identity.md](identity.md)

### AT identifier

- **SYN-19** (MUST) An `AtIdentifier` is a DID if the text starts with `did:` and a handle
  otherwise. Text starting with `did:` that is not a valid DID is invalid; it is not retried as
  a handle.
  *Source:* specs/lexicon `at-identifier` · 0.8 `AtIdentifier.cs:64` · vectors
  `atidentifier_syntax_*.txt` · test `AtIdentifierTests.Parse_KnowsWhetherItIsADidOrAHandle`
- **SYN-20** (MUST) A handle inside an `AtIdentifier` is normalised as in SYN-16. Exactly one of
  `Did` and `Handle` is non-null.
  *Source:* 0.8 `AtIdentifier.cs:16-32`

### NSID

- **SYN-21** (MUST) An NSID has at least three `.`-separated segments and is at most 317
  characters. Authority segments (all but the last) follow handle label rules: 1–63 characters
  of `[a-zA-Z0-9-]`, not starting or ending with `-`. The first segment starts with a letter.
  There is no separate limit on the authority part: the upstream vectors accept a 287-character
  NSID whose authority exceeds 253 characters, so only the total counts (*Phase 1: the 253-character
  authority limit of the first draft was dropped*).
  *Source:* [specs/nsid](https://atproto.com/specs/nsid) · 0.8 `IdentifierSyntax.cs:99` ·
  vectors `nsid_syntax_*.txt`
- **SYN-22** (MUST) The name segment (the last) is 1–63 ASCII letters and digits and starts with
  a letter.
  *Source:* specs/nsid · test `NsidTests.Parse_NameLengthLimit_IsSixtyThreeCharacters`
- **SYN-23** (MUST) NSIDs are not case-normalised. Equality is ordinal on the text as written.
  *Source:* 0.8 behaviour (primitives audit §3) · see Open points
- **SYN-24** (MUST) `Authority` and `Name` expose the two parts without allocation-heavy
  re-parsing on each access.
  *Source:* 0.8 test `NsidTests.Parse_ExtractsAuthorityNameAndSegments`

### Record key

- **SYN-25** (MUST) A record key is 1–512 characters from `[A-Za-z0-9._:~-]` and is neither `.`
  nor `..`. Record keys are case-sensitive.
  *Source:* [specs/record-key](https://atproto.com/specs/record-key) · 0.8
  `IdentifierSyntax.cs:128` · vectors `recordkey_syntax_*.txt` · test
  `RecordKeyTests.Parse_CharacterOutsideTheAllowedSet_Throws`
- **SYN-26** (MUST) `RecordKey.From(Tid)` yields the TID's text as a record key. There is no
  separate `RecordKey.NewTid()`.
  *Source:* **Decision**, principle 5 ([00 — Vision](../00-vision.md#design-principles), "one way to do it") · 0.8 had `RecordKey.NewTid()` and
  `Tid.NextString()` duplicating `Tid.Next()`

### TID

- **SYN-27** (MUST) A TID is exactly 13 characters of the base32-sortable alphabet
  `234567abcdefghijklmnopqrstuvwxyz`, and its first character is at most `j` (the high bit of the
  64-bit value is 0).
  *Source:* [specs/tid](https://atproto.com/specs/tid) · 0.8 `IdentifierSyntax.cs:140`,
  `Tid.cs:15` · vectors `tid_syntax_*.txt` · test `TidTests.TryParse_HighBit_MustBeClear`
- **SYN-28** (MUST) A TID's 64-bit value is 53 bits of microseconds since the Unix epoch
  followed by a 10-bit clock identifier. `ToInt64`/`FromInt64` round-trip, and ordinal order of
  the text equals numeric order of the value. `CompareTo` compares the values.
  *Phase 1:* a TID is held as an unsigned 64-bit value, because SYN-27 admits first characters up
  to `j` (bit 63 set, `jzzzzzzzzzzzz`): `ToInt64` then returns a negative `long`, `FromInt64`
  accepts any `long` (it throws nothing), and `CompareTo` compares the unsigned values.
  *Source:* specs/tid · test `TidTests.FromInt64_ToInt64_RoundTrip`
- **SYN-29** (MUST) `default(Tid)` is the TID with value 0 (`2222222222222`), which is valid.
  *Source:* **Decision** AG-8 (structs only where `default` is valid)

### TID generator

- **SYN-30** (MUST) Every TID a `TidGenerator` returns is strictly greater than every TID it
  returned before, across threads: the timestamp is `max(now_µs, last + 1)`, published with a
  compare-and-swap.
  *Source:* specs/tid · 0.8 `TidGenerator.cs:57-72` · fixes #114 (same-millisecond collisions) ·
  tests `TidGeneratorTests.Next_100kSequentialCalls_StrictlyIncreasing`,
  `Next_ConcurrentCallers_EachStrictlyIncreasingAndAllDistinct`
- **SYN-31** (MUST) A clock that steps backwards or freezes still yields increasing TIDs (one
  microsecond per call while frozen).
  *Source:* tests `TidGeneratorTests.Next_ClockStepsBackwards_StillIncreases`,
  `Next_FrozenClock_AdvancesOneMicrosecondPerCall`
- **SYN-32** (MUST) The clock identifier is fixed per generator: the given value (0–1023,
  otherwise `ArgumentOutOfRangeException`) or a random one drawn from a CSPRNG. Every TID carries
  it.
  *Source:* 0.8 `TidGenerator.cs:40` · tests `Next_EveryTid_CarriesTheGeneratorsClockId`,
  `Constructor_ClockIdOutOfRange_Throws`
- **SYN-33** (MUST) Time comes from the injected `TimeProvider`. A clock before the epoch counts
  as the epoch. A timestamp beyond 53 bits throws `InvalidOperationException`.
  *Source:* AG-28 · 0.8 `TidGenerator.cs:60-70` · test `Next_TimestampBeyond53Bits_Throws`
- **SYN-34** (MUST) Given the instant and clock identifier of the spec's example, the generator
  produces the spec's example TID.
  *Source:* specs/tid · test `TidGeneratorTests.Next_SpecExampleInstantAndClockId_ProducesSpecExampleTid`
- **SYN-35** (MUST) `TidGenerator.Default` is a static, read-only instance with no
  caller-settable state. `Tid.Next()` uses it. This is permitted under AG-10 because monotonic
  TIDs per process are what the TID spec asks for.
  *Source:* **Decision** · 0.8 `TidGenerator.Shared` (internal)

### AT URI

- **SYN-36** (MUST) An `AtUri` is the restricted Lexicon form
  `at://AUTHORITY[/COLLECTION[/RKEY]]`, where AUTHORITY is a DID or handle (SYN-19), COLLECTION
  an NSID and RKEY a record key. It is at most 8 KiB. It has no query, fragment, trailing slash
  or empty segment, and no `@` in the authority.
  *Source:* [specs/at-uri-scheme](https://atproto.com/specs/at-uri-scheme) (Lexicon restricted
  syntax) · 0.8 `AtUri.cs:20,96-160` · vectors `aturi_syntax_*.txt` · test
  `AtUriTests.Parse_OutsideTheRestrictedSyntax_Throws`
- **SYN-37** (MUST) `Value` keeps the text exactly as given. `Authority` is the parsed
  `AtIdentifier`, so a handle authority reads lower-cased there. Equality is ordinal on `Value`.
  *Source:* 0.8 `AtUri.cs:31-37` · test `AtUriTests.Parse_HandleAuthority_RepoIsLowerCasedHandle`
- **SYN-38** (MUST) `AtUri.Create` builds the URI that `Parse` would read back. A record key
  without a collection throws `ArgumentException`.
  *Source:* tests `AtUriTests.Create_FromComponents_ProducesTheUriParseReads`,
  `Create_RkeyWithoutCollection_Throws`
- **SYN-39** (SHOULD) Component accessors are computed once and cached without locking (a race
  may compute a part twice, to equal values).
  *Source:* 0.8 `AtUri.cs:25-29`

### Space addresses

Spaces are part of the protocol's core vocabulary: a space and every record in it have an
address. `SpaceUri` names a space, `SpaceRecordUri` a record in one, and `AtAddress` is the C# 15
union of those two and `AtUri`, for places that accept an address from either data protocol
([xrpc-client.md](xrpc-client.md#records-in-spaces), [lexicon.md](lexicon.md)).

- **SYN-74** (MUST) A `SpaceUri` is `at://{authority}/space/{spaceType}/{skey}` and a
  `SpaceRecordUri` is `at://{authority}/space/{spaceType}/{skey}/{author}/{collection}/{rkey}`,
  where authority and author are DIDs (never handles), spaceType and collection are NSIDs, and
  skey and rkey are record keys. The literal segment `space` cannot be an NSID (an NSID has at
  least two dots), so space addresses never collide with public AT URIs.
  *Source:* proposal 0016 (at the pin in [spaces.md](spaces.md)) · forum thread "Bringing back
  at://" · 0.8 `Spaces/SpaceUri.cs` · tests
  `SpaceUriTests.Parse_SpaceRef_SplitsItsThreeComponents`, `Parse_RecordUri_SplitsAllSixComponents`,
  `TryParse_RejectsAHandleAuthority`
- **SYN-75** (MUST) One scanner parses all three kinds of address. `AtUri.Parse` rejects space
  addresses with a `FormatException` naming the space type, `SpaceUri.Parse` rejects record
  addresses and public AT URIs, `SpaceRecordUri.Parse` rejects space and public addresses, and
  `AtAddress.TryParse` classifies any address without a second scan. The scheme, the marker segment
  and the segment layout are constants in exactly one place, so an upstream change to the space
  address syntax (SYN-80) is a change in one file.
  *Source:* **Decision** (0.8 parses public and space addresses in unrelated types) · tests
  `SpaceUriTests.Parse_RecordUri_IsRejectedAsASpaceRef`, `Parse_SpaceRef_IsRejectedAsARecordUri`,
  `IsSpaceUri_DistinguishesPermissionedFromPublic`
- **SYN-76** (MUST) Space addresses follow the AT URI restrictions of SYN-36 (no query, fragment,
  trailing slash, empty segment, whitespace or line break) and the same maximum length.
  *Source:* 0.8 `Spaces/SpaceUri.cs` · tests `SpaceUriTests.TryParse_InvalidSpaceRef_ReturnsFalse`,
  `TryParse_InvalidRecordUri_ReturnsFalse`, `TryParse_RejectsASpaceKeyOverTheLengthLimit`,
  `Create_AcceptsASpaceKeyAtTheLengthLimit`
- **SYN-77** (MUST) `Value` keeps the text exactly as given; equality is ordinal on `Value`.
  `SpaceUri.Record` builds the `SpaceRecordUri` that `Parse` would read back, and its `Space`
  equals the `SpaceUri` it was built from.
  *Source:* 0.8 `Spaces/SpaceUri.cs` · tests `SpaceUriTests.Record_BuildsTheSameUriAsParsingOne`,
  `Create_RoundTripsThroughParse`, `Equality_IsByValue`
- **SYN-78** (MUST) The JSON converter (SYN-70) treats `SpaceUri` and `SpaceRecordUri` like every
  other identifier, and `AtAddress` as a string that must parse as one of its three cases: invalid
  text fails deserialization with the JSON path.
  *Source:* 0.8 `IdentifierJsonConverter<SpaceUri>`
- **SYN-79** (SHOULD) Code that accepts "any atproto address" uses `AtAddress`, not string
  inspection.
  *Source:* **Decision**
- **SYN-80** (MUST) The space address syntax is pinned to the protocol revision recorded in
  [spaces.md](spaces.md) (the proposal until the GA specification is published). Upstream is still
  debating the scheme (`at://…/space/…` versus a distinct scheme such as `ats://`); a change is
  absorbed by the single implementation point of SYN-75 and its vectors.
  *Source:* forum "Post-alpha plans" (URI scheme, 2026-10-08: "still furiously debating") ·
  [SPC-51](spaces.md#addressing-implementation)
- **SYN-81** (MUST) `AtAddress` is a C# 15 `union` (no shared base, AG-6a): a `switch` over it
  handles `AtUri`, `SpaceUri` and `SpaceRecordUri`, and its `ToString()` is the case's canonical
  text.
  *Source:* **Decision** · [SPC-52](spaces.md#addressing-implementation)

### Generic URI format

- **SYN-40** (MUST) `UriFormat.IsValid` accepts what the Lexicon `uri` format allows: an RFC 3986
  URI with a scheme, at most 8,192 characters, no whitespace. It is used by Lexicon validation
  ([lexicon.md](lexicon.md)). The vectors `uri_syntax_*.txt`, vendored but unasserted in 0.8,
  are asserted.
  *Source:* [specs/lexicon `uri`](https://atproto.com/specs/lexicon#uri) · tests audit
  (unasserted fixtures)

### Datetime

- **SYN-41** (MUST) A valid datetime matches `yyyy-MM-ddTHH:mm:ss[.f…](Z|±HH:MM)`: upper-case
  `T` and `Z`, a mandatory time zone, 1–20 fractional digits when a `.` is present, at most 64
  characters, a real calendar date (leap years checked), seconds below 60, and not the offset
  `-00:00`. This is the intersection of RFC 3339, ISO 8601 and WHATWG HTML.
  *Source:* [specs/lexicon#datetime](https://atproto.com/specs/lexicon#datetime) · 0.8
  `AtDatetime.cs:285-330` · vectors `datetime_syntax_*.txt`, `datetime_parse_invalid.txt` ·
  test `AtDatetimeTests.TryParse_InvalidFixture_Rejects`
- **SYN-42** (MUST) Year 0000 is valid, except when the offset moves the instant before year 0.
  A valid datetime outside the `DateTimeOffset` range has no instant (`TryGetInstant` returns
  false), but is still valid.
  *Source:* specs/lexicon#datetime · test `AtDatetimeTests.Value_YearZero_IsValidButOutsideTheDateTimeOffsetRange`
- **SYN-43** (MUST) An `AtDatetime` keeps its text exactly. Writing it out (JSON or CBOR) writes
  that text byte for byte.
  *Source:* 0.8 · tests `AtDatetimeTests.Parse_ValidFixture_KeepsTheTextExactly`,
  `Json_AnyString_RoundTripsByteForByte`
- **SYN-44** (MUST) `Now`, `From(DateTimeOffset)` and `From(DateTime)` produce the canonical
  form `yyyy-MM-ddTHH:mm:ss.fffZ` in UTC, truncated to milliseconds, independent of the current
  culture and calendar. `From(DateTime)` treats `Utc` and `Unspecified` as UTC and converts
  `Local`. `Now` reads the injected `TimeProvider`.
  *Source:* 0.8 `AtDatetime.cs:42` · tests `FromDateTimeOffset_WritesTheCanonicalUtcForm`,
  `Now_UnderAnyCulture_IsACanonicalGregorianUtcTimestamp`, `FromDateTime_Local_IsConvertedToUtc`
- **SYN-45** (MUST) `Parse`, `TryParse`, `Now` and `From` only ever produce valid values
  (`IsValid` true). The only producer of an invalid `AtDatetime` is the lenient JSON/CBOR read
  (SYN-46). This is a documented exception to AG-8, required by
  [D-20](../06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types).
  *Source:* **Decision** D-20
- **SYN-46** (MUST) Reading a datetime from JSON (or from DRISL text) never rejects a string
  token. Invalid text is kept verbatim with `IsValid` false. If the text starts with four
  digits and `-` and `DateTimeOffset.TryParse` (invariant culture, assume universal) reads it,
  the instant is recovered. Otherwise there is no instant. A non-string token is a
  `JsonException`.
  *Source:* D-20 · 0.8 `AtDatetime.cs:215-231` (`FromWire`) · tests
  `Json_InvalidFixture_IsReadButNotValid`, `Json_NearMissIso8601_IsNotValidButHasAnInstant`,
  `Json_Unreadable_KeepsTheTextWithoutAnInstant`, `Json_NonStringToken_Throws`
- **SYN-47** (MUST) The instant keeps the written offset. Offsets beyond ±14 hours, which
  `DateTimeOffset` cannot hold, are normalised to UTC.
  *Source:* tests `Value_ValidDatetime_KeepsItsOffset`, `Value_OffsetBeyondFourteenHours_IsNormalizedToUtc`
- **SYN-48** (MUST) Equality is ordinal on the text. Ordering is by instant, then by text.
  Values without an instant sort first. Ordering is consistent with equality.
  *Source:* tests `Equality_IsOrdinalOnTheText`, `CompareTo_OrdersByInstantAcrossOffsets`,
  `CompareTo_ValuesWithoutAnInstant_SortFirst`, `CompareTo_SameInstantDifferentText_IsConsistentWithEquality`

### Numbers carried in strings

<a id="numbers-carried-in-strings"></a>

The atproto data model has no floating-point type. Lexicons that need fractions declare a plain
`string` and write the number as text. `DecimalString` (namespace `ATProtoNet.Data`) gives that
convention a type that reads and writes the text unchanged and parses it the same way on every
machine. Generated code uses it only where the developer opts in per field
([LEX-92](lexicon.md#generated-clients)); hand-written models use it directly.

- **SYN-82** (MUST) A valid `DecimalString` matches the JSON number grammar
  `-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?`, at most `DecimalString.MaxLength` characters:
  no leading `+`, no leading zeros, no group separators, no locale forms (`52,52`), no `NaN` or
  `Infinity`.
  *Source:* **Decision** (maintainer, 2026-10-17) · [RFC 8259 §6](https://www.rfc-editor.org/rfc/rfc8259#section-6)
- **SYN-83** (MUST) A `DecimalString` keeps its text exactly. Writing it out (JSON or DRISL)
  writes that text byte for byte as a string, so a record read and written back keeps its CID
  (`"52.520"` stays `"52.520"`).
  *Source:* **Decision**, as SYN-43 for datetimes
- **SYN-84** (MUST) `Parse`, `TryParse` and `From` only ever produce valid values. `From(double)`
  writes the shortest text that round-trips, `From(decimal)` keeps the scale, both in the
  invariant culture whatever the current one is; `From(double)` refuses `NaN` and infinities
  with `ArgumentOutOfRangeException`.
  *Source:* **Decision** · the `de-DE` failure mode (`double.Parse("52.520")` is 52520 there)
- **SYN-85** (MUST) Reading from JSON (or DRISL text) never rejects a string token: invalid text
  is kept verbatim with `IsValid` false, as SYN-46 does for datetimes. A non-string token
  (a JSON number) is a `JsonException`, because the Lexicon declares a string and a fractional
  number is not atproto data.
  *Source:* [D-20](../06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types)
- **SYN-86** (MUST) `TryGetDouble` and `TryGetDecimal` parse with the invariant culture and
  return false for invalid text, for a result outside the target type's range and, for `double`,
  for a non-finite result.
  *Source:* **Decision**
- **SYN-87** (MUST) Equality is ordinal on the text (`"52.52"` and `"52.520"` are different
  values, as they are different bytes on the wire). There is no numeric ordering; callers compare the
  parsed numbers. `CompareTo`, which `IAtIdentifier<TSelf>` requires, is ordinal on the text.
  *Source:* **Decision**, as SYN-48

A hand-written model can instead put
`[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]`
on a `double` or `decimal` property: System.Text.Json reads and writes it as a string under the
strict SDK options, source generation included. It re-formats the text (`"52.520"` is written back
as `"52.52"`), so a read-modify-write can change the record's CID; `DecimalString` does not.

### Language

- **SYN-49** (MUST) `Language` accepts BCP 47 language tags as RFC 5646 defines them, including
  grandfathered and private-use tags, case-insensitively. It rejects the syntactically invalid
  vectors and also the well-formed-but-invalid ones (repeated variant subtags, repeated
  extension singletons). *Phase 1:* two vectors of `language_syntax_invalid.txt` constrain the
  primary language subtag: `JA` is invalid, so the primary subtag must be lower case (every other
  subtag, the private-use `x` and the grandfathered tags are case-insensitive), and `jaja` is
  invalid, so the four-letter primary subtags RFC 5646 reserves for future use are refused.
  *Source:* [specs/lexicon `language`](https://atproto.com/specs/lexicon#language) · RFC 5646
  §2.1, §2.2.5–2.2.6 · vectors `language_syntax_*.txt`, `language_parse_invalid.txt` (vendored
  but unasserted in 0.8)
- **SYN-50** (MUST) `Language.Value` keeps the text as given. Equality is ordinal
  case-insensitive (BCP 47 tags are case-insensitive).
  *Source:* RFC 5646 §2.1.1

### CID string syntax

- **SYN-60** (MUST) A CID string is `b` followed by unpadded lower-case RFC 4648 base32 that
  decodes to exactly the 36-byte binary CID of [data-model.md](data-model.md#cid) (CIDv1,
  codec `0x71` or `0x55`, sha2-256). The string is therefore 59 characters long.
  *Source:* [specs/data-model](https://atproto.com/specs/data-model#link-and-cid-formats) ·
  0.8 `Cid.cs:34-41,311` · test `CidTests.TryParse_NotABlessedCid_ReturnsFalse`
- **SYN-61** (MUST) The base32 must be canonical: the final character's unused bits are zero.
  Upper-case or mixed-case base32 is invalid.
  *Source:* 0.8 `CidComputation.cs:229`
- **SYN-62** (MUST) CIDv0 (`Qm…`), other multibase prefixes, other codecs and other hash
  functions are invalid. The vectors `cid_syntax_invalid.txt` are asserted. The vectors
  `cid_syntax_valid.txt` are deliberately **not** asserted as valid, because they list generic
  multiformats CIDs that atproto does not bless.
  *Source:* specs/data-model (blessed formats) · 0.8 `SyntaxInteropTests.CidTryParse_InvalidFixture_Rejects`
- **SYN-63** (MUST) A `Cid` parsed from text writes back the same text.
  *Source:* test `CidTests.Deserialize_CidFromJson_WritesBackItsText`

### JSON converters

- **SYN-70** (MUST) Every type in this spec has a System.Text.Json converter, attached so that
  source-generated `JsonSerializerContext`s use it without reflection. Writing emits `Value`.
  *Source:* G6 · 0.8 `Serialization/IdentityJsonConverters.cs`
- **SYN-71** (MUST) Reading an invalid identifier (every type except `AtDatetime` and
  `DecimalString`, which read leniently by SYN-46 and SYN-85) throws a
  `JsonException` carrying the JSON path, with the `FormatException` as its inner exception.
  Identifiers are strict on read.
  *Source:* D-20 · 0.8 behaviour (`migrating-to-0.7.md:236`) · test
  `IdentitySerializationTests`
- **SYN-72** (MUST) A JSON `null` reads as `null` for nullable members. An absent optional member
  is omitted on write.
  *Source:* test `AtDatetimeTests.Json_NullOptional_IsOmitted`
- **SYN-73** (SHOULD) Converters support use as dictionary keys
  (`ReadAsPropertyName`/`WriteAsPropertyName`), so maps keyed by `Did` or `Nsid` serialise
  naturally.
  *Source:* **Decision**

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `Did.MaxLength` | 2,048 | SYN-11 |
| `Did.MinLength` | 7 | SYN-11 |
| `Handle.MaxLength` | 253 | SYN-15 |
| `Handle.MaxLabelLength` | 63 | SYN-15, SYN-21 |
| `Nsid.MaxLength` | 317 | SYN-21 |
| `Nsid.MaxNameLength` | 63 | SYN-22 |
| `RecordKey.MaxLength` | 512 | SYN-25 |
| `Tid.Length` | 13 | SYN-27 |
| `TidGenerator.MaxClockId` | 1,023 | SYN-32 |
| `TidGenerator.MaxTimestamp` | 2⁵³ − 1 µs | SYN-33 |
| `AtUri.MaxLength` | 8,192 | SYN-36 |
| `SpaceUri.MaxLength`, `SpaceRecordUri.MaxLength` | 8,192 | SYN-76 |
| `UriFormat.MaxLength` | 8,192 | SYN-40 |
| `AtDatetime.MaxLength` | 64 | SYN-41 |
| `AtDatetime.MaxFractionDigits` | 20 | SYN-41 |
| `DecimalString.MaxLength` | 64 | SYN-82 (bounds parsing) |
| `Language.MaxLength` | 128 | SYN-49 (new; RFC 5646 sets no maximum, this bounds parsing) |
| `Cid.StringLength` | 59 | SYN-60 |

## Errors

| Failure | Result |
|---|---|
| Invalid text passed to `Parse` | `FormatException` |
| `null` passed to `Parse` | `ArgumentNullException` |
| Invalid identifier in JSON | `JsonException` (path) wrapping `FormatException` |
| Invalid datetime text in JSON | no error: `AtDatetime` with `IsValid == false` |
| Invalid number text in a `DecimalString` field | no error: `DecimalString` with `IsValid == false` |
| `DecimalString.From` with `NaN` or an infinity | `ArgumentOutOfRangeException` |
| Non-string JSON token for any type | `JsonException` |
| Clock identifier outside 0–1023 | `ArgumentOutOfRangeException` |
| TID timestamp past 53 bits | `InvalidOperationException` |
| `AtUri.Create` with a record key but no collection | `ArgumentException` |
| Address of the wrong kind passed to `AtUri.Parse`, `SpaceUri.Parse` or `SpaceRecordUri.Parse` | `FormatException` naming the type it is |

## Conformance and tests

**Vectors** (conformance area *Identifier syntax*, [04 — Testing](../04-testing.md#conformance-areas)):
all 24 files of `syntax/` from atproto-interop-tests `056e5741` (CC0), each asserted line by line
(`cid_syntax_valid.txt` only for "not rejected for being malformed base32"; see SYN-62), plus:

- the reference-regex oracle with seeded mutations (SYN-8);
- line-break cases for every type (SYN-2);
- the TID spec example (SYN-34);
- space address vectors (space and record forms, collisions with public AT URIs, handle
  authorities refused), extracted from 0.8 `SpaceUriTests` and re-checked against the pinned
  proposal.

**New tests for `DecimalString`** (no 0.8 counterpart): text round-trips byte for byte through
JSON and DRISL (`"52.520"`), `From` and `TryGet*` give the same results under `de-DE` as under the
invariant culture, a JSON number token is rejected, invalid text is read with `IsValid` false,
and a generated record with a `fieldTypes` mapping round-trips `community.lexicon.location.geo`.

**0.8 test classes whose scenarios are ported:** `SyntaxInteropTests`, `IdentifierSyntaxTests`,
`IdentifierLineBreakTests`, `IdentifierContractTests` (minus the explicit-cast scenarios),
`DidTests`, `HandleTests`, `AtIdentifierTests`, `NsidTests`, `RecordKeyTests`, `TidTests`,
`TidGeneratorTests`, `AtUriTests`, `SpaceUriTests`, `AtDatetimeTests`, `CidTests` (string
scenarios), `IdentitySerializationTests`.

## Changes from 0.8

| Change | Reason |
|---|---|
| `Parse` throws `FormatException`, not `ArgumentException`. | One error contract (AG-8); 0.8 had two. |
| No explicit casts from `string` (`(Did)"…"`). | D-11: they duplicated `Parse` and hid validation. |
| Sealed classes instead of `sealed record`s. | `with` is meaningless for identifiers; records forced `AtUri` and `Cid` to override `Equals` to skip caches. |
| `AtDatetime` is a class; `Tid` is a struct. | `default(AtDatetime)` was invalid and unserialisable; `default(Tid)` is valid. |
| `RecordKey.NewTid()` and `Tid.NextString()` removed. | Duplicates of `RecordKey.From(Tid.Next())` and `Tid.Next().Value`. |
| `IAtIdentifier<TSelf>` public; `TidGenerator.Default` public. | Generic code and explicit generator use need them; both were internal. |
| `Language` type and `UriFormat` added; their vectors asserted. | 0.8 vendored `language_*` and `uri_*` vectors without asserting them. |
| `DecimalString` added for numbers carried in Lexicon strings. | 0.8's global `AllowReadingFromString` read such text into a hand-written `double`, then wrote it back as a JSON number: a float, which the Lexicon (a string) and the data model both forbid (D-20). |
| `SpaceUri` and `SpaceRecordUri` move to the root namespace, share one scanner with `AtUri`, and lose `explicit operator SpaceUri(string)` (AG-8); `AtAddress` is new. | Spaces are core protocol vocabulary; 0.8 kept both in `ATProtoNet.Spaces` with their own parser and the internal `IIdentifier`. |

## Open points

Defaults accepted (maintainer, 2026-10-17). The implementing phase applies them without further
sign-off, settles any point left to it, follows upstream where a point waits on upstream, and
records the outcome here in the same PR.

1. **NSID case.** The NSID spec treats the authority as a domain (case-insensitive). 0.8 neither
   normalises nor compares case-insensitively. *Default:* keep 0.8 behaviour (no
   normalisation, ordinal equality), because Lexicon resolution and `$type` matching are
   ordinal upstream.
2. **AT URI equality with handle authorities.** Two URIs whose handle authorities differ only in
   case compare unequal (SYN-37). *Default:* keep (ordinal on `Value`), since records
   are addressed by DID in practice.
3. **Generated code for `language` and `uri` formats.** 0.8 maps both to `string`.
   *Default:* keep `string` in generated models (a malformed `langs` entry written by
   some client must not fail a whole feed page) and validate through `Language` and `UriFormat`
   in Lexicon validation. Decide in [lexicon.md](lexicon.md).
4. **`Tid` as a struct.** AG-8 allows it because `default` is valid. *Default:* struct;
   revisit only if the generic `IAtIdentifier<TSelf>` code paths box it heavily.
5. **Space address scheme.** Upstream has not settled `at://{authority}/space/…` versus a
   separate scheme (`ats://`), nor whether space URIs may later address blobs or record versions.
   *Default:* implement the pinned form (SYN-74) behind the single implementation point
   (SYN-75) and follow the GA specification (Q-15); keep the space types distinct even if the scheme
   becomes `at://`, because public and space addresses resolve differently and must not be
   confused in APIs.

## Settled in phase 1

Open points 1, 2, 4 and 5 were applied as defaulted (point 3 is decided in [lexicon.md](lexicon.md)).
What building the types settled or changed:

1. **Parse forms are interface members.** `IAtIdentifier<TSelf>` declares all six `Parse`/`TryParse`
   forms (string, `char` span, UTF-8) as static abstract members, so `Did.Parse(span)` works on the
   type and generic code can call them; the BCL `IParsable`, `ISpanParsable` and `IUtf8SpanParsable`
   members are default implementations over them. Each type states its grammar once, as a generic
   `TryRead<TChar>` over `char` and `byte` (SYN-4), and its six public forms are one-liners over the
   shared `Identifiers` helpers.
2. **Limits are `const` on the types** (`Did.MaxLength`, …); the interface's static `MaxLength` is
   implemented explicitly.
3. **The default clock.** The layer 0 architecture rule counts `TimeProvider.System` as a clock read,
   so the `TimeProvider? = null` defaults of `TidGenerator` and `AtDatetime.Now` use an internal
   `TimeProvider` subclass that overrides nothing, which reads the system clock.
4. **SYN-21, SYN-27/28, SYN-49, SYN-71 and SYN-87** are clarified in place (marked *Phase 1*): no
   separate NSID authority limit, unsigned TID values, a lower-case primary language subtag and no
   four-letter one, `DecimalString` reading leniently, ordinal `DecimalString.CompareTo`.
5. **Additions to the sketch:** `RecordKey.Self` (0.8 had it; singleton records use it), `Tid.Length`,
   `TidGenerator.MaxTimestamp`, `AtDatetime.MaxLength`/`MaxFractionDigits`, `Nsid.MaxNameLength`,
   `Did.MinLength`, and the public converter types listed above.
6. **Not carried over from 0.8:** `SpaceUri.IsSpaceUri` (`AtAddress.TryParse` classifies an address with
   one scan) and `SpaceUri.HostAudience` (service identifiers belong with the space authority in
   [spaces.md](spaces.md)).
7. **`UriFormat`** takes a scheme (a letter, then letters, digits, `+`, `-`, `.`), a colon and a
   non-empty remainder of printable ASCII, which the `uri_syntax_*` vectors confirm. It shares its
   name with `System.UriFormat`, so a file that imports both namespaces must qualify it.
8. **Error messages** quote the offending text up to 64 characters, and name the address type a
   valid address of another kind is (SYN-75).
9. **The oracle (SYN-8)** is the conformance suite's `ParseIdentifier_OracleInput_MatchesTheReferencePattern`
   over `syntax/reference-patterns.json`; the Next adapter also checks, for every vector line of
   every suite, that the string, `char` span and UTF-8 forms and `Parse` agree (SYN-3, SYN-4).
10. **Waivers.** SYN-79 (a guideline for APIs that accept an address) and SYN-80 (follow the upstream
    space syntax) have no test of their own: the first is applied where those APIs are written
    (phases 5 and 8) and the second is the single scanner of SYN-75, which SYN-74 to SYN-77 pin.
