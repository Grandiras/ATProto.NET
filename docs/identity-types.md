# Identity Types

ATProto.NET provides strongly-typed wrappers for all AT Protocol identifiers. These types validate input, normalize values, and prevent common mistakes at compile time.

Validation follows the atproto specs and is checked against the official
[`atproto-interop-tests`](https://github.com/bluesky-social/atproto-interop-tests) syntax fixtures.
Parsing is strict: data from other implementations or older records that may not conform should go
through `TryParse` rather than `Parse`.

Every identifier type is an immutable `sealed record` with the same shape:

- `Parse(string)` throws `ArgumentException` (`ArgumentNullException` for `null`); `TryParse(string?, out T?)` returns `false` instead.
- It implements `IParsable<T>`, `ISpanParsable<T>` and `IComparable<T>`, so generic code and minimal-API parameter binding can parse it. The interface `Parse` members throw `FormatException`, as that contract expects.
- Equality and ordering are ordinal on the string value.
- `string s = id;` converts implicitly (a `null` identifier gives `null`); `(T)"…"` converts explicitly and throws like `Parse`.
- It serializes to and from a JSON string. An invalid value fails deserialization with a `JsonException` carrying the JSON path.

## DID

A Decentralized Identifier — the persistent, unique identifier for every AT Protocol account.

```csharp
using ATProtoNet.Identity;

// Parse (throws on invalid input)
var did = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");

// TryParse (returns false on invalid input)
if (Did.TryParse("did:plc:abc", out var parsed))
    Console.WriteLine(parsed);

// Properties
did.Method              // "plc"
did.MethodSpecificId    // "z72i7hdynmk6r22z27h6tvur"

// Conversion
string s = did;                    // Implicit to string
Did d = (Did)"did:plc:abc123";    // Explicit from string

// Equality
did == Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur")  // true
```

### Validation Rules
- Must start with `did:`
- Second segment is the method (lowercase letters only)
- Third segment is the method-specific ID: letters, digits, `.`, `_`, `-`, `:` and `%`, but it may not end in `:` or `%`
- Maximum length: 2048 characters

## Handle

A human-readable domain-name identifier.

```csharp
var handle = Handle.Parse("alice.bsky.social");

// Normalization
Handle.Parse("Alice.Bsky.Social").Value   // "alice.bsky.social" (lowercased)
Handle.Parse("@alice.bsky.social").Value  // "alice.bsky.social" (@ stripped)

// Properties
handle.Value    // "alice.bsky.social"

// Validation
Handle.TryParse("not valid!", out _)   // false
Handle.TryParse("a]b.com", out _)      // false
```

### Validation Rules
- Valid domain name format
- Maximum length: 253 characters
- Leading `@` is stripped automatically

## AtIdentifier

A discriminated union that can hold either a DID or a Handle. Useful for API parameters that accept either.

```csharp
var id1 = AtIdentifier.Parse("did:plc:abc123");
var id2 = AtIdentifier.Parse("alice.bsky.social");

if (id1.IsDid)
    Console.WriteLine($"DID: {id1.Did}");
else if (id1.IsHandle)
    Console.WriteLine($"Handle: {id1.Handle}");

// Get the underlying value
string value = id1.Value;  // Works for both

// Factory methods, or the implicit conversions from Did and Handle
var fromDid = AtIdentifier.FromDid(Did.Parse("did:plc:abc"));
AtIdentifier fromHandle = Handle.Parse("alice.bsky.social");
```

## NSID (Namespaced Identifier)

Identifies a Lexicon type or method.

```csharp
var nsid = Nsid.Parse("com.example.todo.item");

nsid.Authority    // "com.example.todo" (reversed domain)
nsid.Name         // "item"
nsid.Segments     // ["com", "example", "todo", "item"]
nsid.Value        // "com.example.todo.item"
```

### Validation Rules
- At least 3 dot-separated segments
- Maximum length: 317 characters
- Domain segments: 1-63 letters, digits or hyphens, not starting or ending with a hyphen; the first may not start with a digit
- Name (last segment): 1-63 letters or digits, starting with a letter

## AtUri

An AT Protocol URI, referencing a specific record or collection.

```csharp
var uri = AtUri.Parse("at://did:plc:abc/com.example.todo.item/3k2la");

uri.Authority    // "did:plc:abc" (string, as written)
uri.Repo         // AtIdentifier: the authority as a DID or (lower-cased) handle
uri.Collection   // Nsid? "com.example.todo.item"
uri.RecordKey    // RecordKey? "3k2la"

// Collection-level URI (no record key)
var collUri = AtUri.Parse("at://did:plc:abc/com.example.todo.item");
collUri.RecordKey  // null

// Create from components: an AtIdentifier (DID or handle), then an optional Nsid and RecordKey
var created = AtUri.Create(
    Did.Parse("did:plc:abc"), Nsid.Parse("com.example.todo.item"), RecordKey.Parse("3k2la"));
```

### Format
```
at://authority[/collection[/recordKey]]
```

This is the restricted syntax Lexicon `at-uri` fields use. The authority must be a DID or a handle
(without `@`), the collection an NSID and the record key a valid record key. Query strings (`?`),
fragments (`#`), trailing slashes and extra path segments are rejected. Maximum length: 8 KiB.

## TID (Timestamp Identifier)

A 13-character, base32-sortable identifier. Used as the default record key format.

```csharp
var tid = Tid.Next();          // Generate a new TID
string s = Tid.NextString();   // …or straight to its string form

tid.Value      // "3k2la7rxjgs2t" (13 chars)

// Parse
var parsed = Tid.Parse("3k2la7rxjgs2t");

// Raw 64-bit value
long raw = tid.ToInt64();
var same = Tid.FromInt64(raw);

// Successive TIDs are strictly increasing, even within one microsecond or across threads
var a = Tid.Next();
var b = Tid.Next();
// b.CompareTo(a) > 0
```

`Tid.Next()` and `RecordKey.NewTid()` draw from one process-wide `TidGenerator`. Create your own
generator to fix the clock identifier (for example, one per worker in a cluster) or to inject a
`TimeProvider` in tests:

```csharp
var generator = new TidGenerator(clockId: 7, timeProvider: TimeProvider.System);
Tid rev = generator.Next();
```

A generator never repeats a TID and never goes backwards: when the clock has not advanced (or has
stepped back) since the previous TID, it uses the previous timestamp plus one microsecond.

### Properties
- Exactly 13 characters from `234567abcdefghijklmnopqrstuvwxyz`
- The first character is one of `234567abcdefghij` (the top bit is always zero)
- Encodes a microsecond timestamp + a 10-bit clock ID
- Ordinal string order equals numeric order

## RecordKey

A validated record key for use in AT URIs and API calls.

```csharp
var rkey = RecordKey.Parse("3k2la7rxjgs2t");

// Special constant
RecordKey.Self  // "self" — used by profile records

// Generate a new TID-based key
var generated = RecordKey.NewTid();
```

### Validation Rules
- 1-512 characters
- Cannot be `.` or `..`
- Valid characters: alphanumeric, `.`, `-`, `_`, `~`, `:`

## CID (Content Identifier)

A content-addressed hash identifier for a specific record version or blob.

```csharp
var cid = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");
cid.Value    // The full CID string
cid.Codec    // CidCodec.DagCbor (records, commits, MST nodes) or CidCodec.Raw (blobs)
cid.Digest   // ReadOnlyMemory<byte>: the 32-byte SHA-256 digest
cid.ToBytes() // The 36-byte binary CID
```

Only the CID form the atproto data model allows is accepted: CIDv1, codec DRISL/DAG-CBOR (`0x71`)
or raw (`0x55`), a SHA-256 digest, written as lower-case base32 with the `b` prefix
(`bafyrei…` or `bafkrei…`). Legacy CIDv0 (`Qm…`) and other encodings fail to parse.

## AtDatetime

A Lexicon `datetime`: an RFC 3339 timestamp. Unlike the identifiers it is a `readonly record
struct`, and it keeps the exact text it was read with, so a record you read and write back
re-serializes byte for byte and keeps its CID.

```csharp
var now = AtDatetime.Now();                                   // "2026-09-24T12:30:45.123Z"
var fromDto = AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow);
var fromDt = AtDatetime.FromDateTime(DateTime.UtcNow);        // Unspecified is taken as UTC
var parsed = AtDatetime.Parse("1985-04-12T23:20:50.123-07:00"); // kept exactly as written

parsed.IsValid                  // true: a valid atproto datetime
parsed.Value                    // DateTimeOffset, in the offset it was written with
parsed.TryGetValue(out var dto) // false when there is no instant to read
parsed.ToString()               // "1985-04-12T23:20:50.123-07:00"
```

- **Strict construction.** `Parse`/`TryParse` accept only valid atproto datetimes (checked against
  the interop fixtures). `Now`, `FromDateTimeOffset` and `FromDateTime` always write the canonical
  `yyyy-MM-ddTHH:mm:ss.fffZ` in the invariant culture, truncated to milliseconds.
- **Lenient reading.** JSON never fails on a string: records in the wild carry timestamps without
  a time zone or with a lower-case `z`. Such a value is kept verbatim with `IsValid == false`;
  `TryGetValue` still recovers the instant from a near-miss ISO 8601 form (no time zone reads as
  UTC) and returns `false` for anything else.
- **Equality** is ordinal on the text (`…50Z` and `…50.000Z` are different values), and
  **ordering** is by instant, with the text breaking ties. `<`, `>`, `<=` and `>=` are defined.
- `default(AtDatetime)` has no text; serializing it throws. Use `AtDatetime?` for optional fields.
- `FromDateTimeOffset(someDateTime)` compiles through `DateTime`'s implicit conversion, which reads
  `DateTimeKind.Unspecified` as local time. Call `FromDateTime` to read it as UTC.

## In Models and Clients

The `com.atproto.*` models and clients, `RecordCollection<T>` and `AtProtoClient` take and return
these types wherever the Lexicon field or parameter has an identifier or `datetime` format:
`repo` is an `AtIdentifier`, `collection` an `Nsid`, `rkey` a `RecordKey`, `cid`/`swapRecord`/
`swapCommit` a `Cid`, `uri` an `AtUri`, and `createdAt`/`indexedAt` an `AtDatetime`. The
`app.bsky.*`, `chat.bsky.*`, `tools.ozone.*`, `site.standard.*`, spaces and streaming surfaces follow.

- Values from the API are already typed, so passing them on needs no conversion.
- Parse string literals at the edge, once: `Did.Parse("did:plc:…")`, `Nsid.Parse("com.example.todo.item")`.
  Keep constants in `static readonly` fields.
- A `Did` or `Handle` converts implicitly to `AtIdentifier`, and every type converts implicitly to
  `string`.
- A response carrying an invalid identifier fails to deserialize: the client throws
  `XrpcResponseFormatException` with the `JsonException` (and its JSON path) inside.

## JSON Serialization

All identity types serialize/deserialize automatically with `System.Text.Json`:

```csharp
using ATProtoNet.Serialization;

// Use the SDK's default JSON options for correct serialization
var options = AtProtoJsonDefaults.Options;

var json = JsonSerializer.Serialize(did, options);      // "did:plc:abc123"
var back = JsonSerializer.Deserialize<Did>(json, options);

// Works with all identity types
JsonSerializer.Serialize(handle, options);   // "alice.bsky.social"
JsonSerializer.Serialize(nsid, options);     // "com.example.todo.item"
JsonSerializer.Serialize(uri, options);      // "at://did:plc:abc/col/rkey"
```

The converters are attached to the types themselves, so any `JsonSerializerOptions` picks them up.
`SpaceUri` and `SpaceRecordUri` serialize the same way.
