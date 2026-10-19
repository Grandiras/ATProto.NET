# Identifiers and string formats

The vocabulary types of the protocol (`Did`, `Handle`, `AtIdentifier`, `Nsid`, `RecordKey`, `Tid`,
`AtUri`, `AtDatetime`, `Cid`, `Language`) live in the `ATProtoNet` namespace. Each one validates
when it is created, so a value you hold is always well-formed. Specification:
[design/specs/syntax.md](../design/specs/syntax.md).

## Parse and TryParse

`Parse` throws `FormatException` (and `ArgumentNullException` for `null`); `TryParse` never throws.
Both take a `string`, a `ReadOnlySpan<char>` or UTF-8 `ReadOnlySpan<byte>`, and agree on every input.
There is no conversion *from* `string`: a conversion that can throw should be visible.

<!-- snippet: string userInput; -->
```csharp
using ATProtoNet;

var did = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
Console.WriteLine(did.Method);            // plc
Console.WriteLine(did.MethodSpecificId);  // z72i7hdynmk6r22z27h6tvur

if (!AtIdentifier.TryParse(userInput, out var account))
    return;

// A handle is normalized (lower case, one leading '@' stripped); a DID is kept as written.
Console.WriteLine(account.IsDid ? account.Did.Value : account.Handle.Value);

string text = did;                         // to string is implicit
AtIdentifier either = did;                 // so is a Did or Handle to AtIdentifier
```

Values are sealed classes compared by their text, so they work as dictionary keys and sort ordinally.
`Tid` is a struct, because `default` is a valid TID.

## Generic code

`IAtIdentifier<T>` is the constraint every identifier type implements:

```csharp
static T? ReadOrNull<T>(string? text) where T : class, IAtIdentifier<T> => T.TryParse(text, out var value) ? value : null;

var nsid = ReadOrNull<Nsid>("app.bsky.feed.post");
```

## TIDs and datetimes

`Tid.Next()` is strictly increasing in a process. A `TidGenerator` takes a clock identifier and a
`TimeProvider`, so tests control time:

```csharp
var generator = new TidGenerator(clockId: 7, timeProvider: TimeProvider.System);
RecordKey key = RecordKey.From(generator.Next());

AtDatetime createdAt = AtDatetime.Now();   // 2026-10-17T12:00:00.000Z, always UTC to the millisecond
AtDatetime parsed = AtDatetime.Parse("1985-04-12T23:20:50.123+02:00");
if (parsed.TryGetInstant(out var instant))
    Console.WriteLine(instant.Offset);     // +02:00, the offset as written
```

An `AtDatetime` keeps its text exactly, so a record you read and write back keeps its CID. Reading
from JSON never rejects a string: a malformed timestamp is kept with `IsValid` false.

## AT URIs and space addresses

<!-- snippet: Did did; -->
```csharp
var post = AtUri.Parse("at://alice.bsky.social/app.bsky.feed.post/3k2la");
Console.WriteLine(post.Authority.Value);   // alice.bsky.social
Console.WriteLine(post.Collection);        // app.bsky.feed.post

var space = SpaceUri.Parse("at://did:plc:z72i7hdynmk6r22z27h6tvur/space/com.example.forum/main");
SpaceRecordUri entry = space.Record(did, Nsid.Parse("com.example.post"), RecordKey.Parse("1"));
```

`AtAddress` accepts an address from either data protocol and tells you which one it got:

```csharp continued
string Describe(AtAddress address) => address switch
{
    AtUri uri => $"public record in {uri.Authority}",
    SpaceUri space => $"space of type {space.SpaceType}",
    SpaceRecordUri record => $"record {record.Path} in a space",
};

Console.WriteLine(Describe(AtAddress.Parse(entry.Value)));
```

## JSON

Every type serializes as a JSON string, and an invalid value fails with a `JsonException` that
carries the JSON path. The converters are attached to the types, so source-generated contexts use
them without reflection, and they work for dictionary keys.

## Numbers in strings

The data model has no floating-point type. A Lexicon that needs a fraction declares a `string`, and
`DecimalString` (`ATProtoNet.Data`) carries it unchanged and parses it the same on every machine:

```csharp
using ATProtoNet.Data;

var latitude = DecimalString.Parse("52.520");
if (latitude.TryGetDecimal(out var value))
    Console.WriteLine(value);              // 52.520, whatever the current culture

DecimalString written = DecimalString.From(52.520m);   // "52.520": the scale is kept
```

## Other string formats

`Language` validates a BCP 47 tag, and `ATProtoNet.UriFormat.IsValid` the Lexicon `uri` format.
Write the namespace out for `UriFormat`: `System.UriFormat` is a different type of the same name.

```csharp
Console.WriteLine(Language.TryParse("pt-BR", out _));
Console.WriteLine(ATProtoNet.UriFormat.IsValid("https://example.com/path"));
```
