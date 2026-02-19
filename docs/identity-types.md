# Identity Types

ATProto.NET provides strongly-typed wrappers for all AT Protocol identifiers. These types validate input, normalize values, and prevent common mistakes at compile time.

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
- Third segment is the method-specific ID
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
handle.Segments // ["alice", "bsky", "social"]

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

// Factory methods
var fromDid = AtIdentifier.FromDid(Did.Parse("did:plc:abc"));
var fromHandle = AtIdentifier.FromHandle(Handle.Parse("alice.bsky.social"));
```

## NSID (Namespaced Identifier)

Identifies a Lexicon type or method.

```csharp
var nsid = Nsid.Parse("com.example.todo.item");

nsid.Authority    // "example.com" (reversed domain)
nsid.Name         // "item"
nsid.Segments     // ["com", "example", "todo", "item"]
nsid.Value        // "com.example.todo.item"
```

### Validation Rules
- At least 3 dot-separated segments
- Maximum length: 317 characters
- Each segment follows specific character rules

## AtUri

An AT Protocol URI, referencing a specific record or collection.

```csharp
var uri = AtUri.Parse("at://did:plc:abc/com.example.todo.item/3k2la");

uri.Authority    // "did:plc:abc"
uri.Collection   // "com.example.todo.item"
uri.RecordKey    // "3k2la"
uri.Repo         // "did:plc:abc" (alias for Authority)

// Collection-level URI (no record key)
var collUri = AtUri.Parse("at://did:plc:abc/com.example.todo.item");
collUri.RecordKey  // null

// Create from components
var created = AtUri.Create("did:plc:abc", "com.example.todo.item", "3k2la");
```

### Format
```
at://authority/collection/recordKey
```

## TID (Timestamp Identifier)

A 13-character, base32-sortable identifier. Used as the default record key format.

```csharp
var tid = Tid.Next();  // Generate a new TID

tid.Value      // "3k2la7rxjgs2t" (13 chars)
tid.Timestamp  // Microsecond timestamp

// Parse
var parsed = Tid.Parse("3k2la7rxjgs2t");

// TIDs sort chronologically
var a = Tid.Next();
Thread.Sleep(1);
var b = Tid.Next();
// b > a is true (string comparison works for sorting)
```

### Properties
- Exactly 13 characters
- Base32-sortable encoding
- Encodes microsecond timestamp + 10-bit random clock ID
- Chronologically ordered

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
- Valid characters: alphanumeric, `.`, `-`, `_`, `~`, `:`, `%`

## CID (Content Identifier)

A content-addressed hash identifier for a specific record version.

```csharp
var cid = Cid.Parse("bafyreidfayvfkicgmdl3ebhqhvvd3oevkmpoh5eoyltcy73c6aaoqc7srca");
cid.Value  // The full CID string
```

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

Custom JSON converters are registered in `AtProtoJsonDefaults.Options` for all identity types.
