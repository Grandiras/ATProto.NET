# Custom Lexicon Records

The AT Protocol allows any application to define its own data schemas using [Lexicons](https://atproto.com/guides/lexicon). ATProto.NET provides a typed `RecordCollection<T>` API that makes working with custom records as easy as using a database collection.

## Key Concept

In AT Protocol:
- **One account** can be used across **many applications**
- Each app defines its own **Lexicon** (schema namespace, e.g., `com.example.todo`)
- Records are stored in the user's **PDS** (Personal Data Server) in named **collections**
- Each collection has an **NSID** (e.g., `com.example.todo.item`)

## Defining Record Types

### Using AtProtoRecord Base Class

The `AtProtoRecord` base class handles the `$type` and `createdAt` fields. Implement
`IAtProtoRecord` as well, so the record type names its collection once and
`client.GetCollection<TodoItem>()` needs no NSID:

```csharp
using System.Text.Json.Serialization;
using ATProtoNet;
using ATProtoNet.Identity;

public class TodoItem : AtProtoRecord, IAtProtoRecord
{
    // The Lexicon NSID of the collection, which is also the record's $type
    public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");

    public override string Type => Collection;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("dueDate")]
    public AtDatetime? DueDate { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
```

`CreateAsync` sets `createdAt` to the current time when it is unset, so this is written as:
```json
{
  "$type": "com.example.todo.item",
  "createdAt": "2024-01-15T10:30:00.000Z",
  "title": "Buy groceries",
  "completed": false,
  "priority": 2,
  "tags": ["shopping", "errands"]
}
```

> **Note on `$type` and custom serializer options.** `System.Text.Json` does not inherit
> `[JsonPropertyName]` through a property `override`, so the SDK guarantees the `$type` name with a
> contract modifier rather than the attribute alone. `AtProtoJsonDefaults.Options`, which every SDK
> client uses (and therefore `RecordCollection<T>` / `RepoClient`), applies it automatically — write
> the plain `override` above and nothing else. For different settings, copy those options
> (`new JsonSerializerOptions(AtProtoJsonDefaults.Options) { WriteIndented = true }`), which keeps
> it. If you build `JsonSerializerOptions` from scratch instead, add the modifier so you get the
> same guarantee:
>
> ```csharp
> var options = new JsonSerializerOptions
> {
>     TypeInfoResolver = new DefaultJsonTypeInfoResolver
>     {
>         Modifiers = { AtProtoJsonDefaults.ApplyRecordTypeDiscriminator },
>     },
> };
> ```
>
> Without it, the record writes both `"$type"` (from the base member) and a stray `"type"` (from your
> override).

### Using Plain C# Classes

You don't have to extend `AtProtoRecord`. Any serializable class works, and it can implement
`IAtProtoRecord` too:

```csharp
public class Bookmark : IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.bookmarks.bookmark");

    [JsonPropertyName("$type")]
    public string Type => Collection;

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
```

## Getting a Collection

```csharp
var todos = client.GetCollection<TodoItem>();
```

The collection comes from `TodoItem.Collection`, and should match your Lexicon definition. By convention, it follows reverse-domain notation: `com.yourcompany.appname.recordtype`.

The SDK's own record models implement `IAtProtoRecord` too, so the same works for them:
`client.GetCollection<PostRecord>()`, `GetCollection<ProfileRecord>()`, `GetCollection<DocumentRecord>()`
and so on for every `app.bsky`, `chat.bsky` and `site.standard` record.

When the collection is only known at run time, or the type does not implement `IAtProtoRecord`,
pass it explicitly. A type that does declare a collection refuses any other one with an
`ArgumentException`, so a record never lands in a collection its `$type` does not match:

```csharp
using ATProtoNet.Identity;

var notes = client.GetCollection<Note>(Nsid.Parse(settings.NotesCollection));
```

Collections, record keys, CIDs and AT URIs are typed (`Nsid`, `RecordKey`, `Cid`, `AtUri`; see
[Identity Types](identity-types.md)). A value the API hands you, like `created.RecordKey` below, is
already typed; parse a literal with `RecordKey.Parse("…")`.

## CRUD Operations

### Create

```csharp
var created = await todos.CreateAsync(new TodoItem
{
    Title = "Buy groceries",
    Priority = 2,
    Tags = ["shopping"],
});

Console.WriteLine($"URI: {created.Uri}");
Console.WriteLine($"CID: {created.Cid}");
Console.WriteLine($"Record Key: {created.RecordKey}");
Console.WriteLine($"Commit: {created.Commit?.Rev}");
```

Every write returns a `RecordRef`: the record's `Uri`, the `Cid` of the version written, its
`RecordKey`, and the `Commit` that wrote it. `ToStrongRef()` turns it into the
`com.atproto.repo.strongRef` other records use to point at that version. When the record is an
`AtProtoRecord` whose `CreatedAt` is `null`, `CreateAsync` sets it to the current time first;
a value you set is kept.

The server generates a TID-based record key. You can also specify one:

```csharp
var created = await todos.CreateAsync(
    new TodoItem { Title = "Custom key" },
    rkey: RecordKey.Parse("my-custom-key"));
```

### Read

```csharp
var item = await todos.GetAsync(created.RecordKey);

Console.WriteLine($"Title: {item.Value.Title}");
Console.WriteLine($"URI: {item.Uri}");
Console.WriteLine($"CID: {item.Cid}");
Console.WriteLine($"Key: {item.RecordKey}");
```

Every typed read returns a `RecordView<T>`: `Uri`, `Cid`, `Value` and `RecordKey`. Each record is
deserialized once, straight from the response. `GetAsync` throws `XrpcException` with
`RecordNotFound` when there is no such record; when absence is an expected answer, use
`FindAsync`, which returns `null` instead:

```csharp
var maybe = await todos.FindAsync(RecordKey.Parse("maybe-there"));
if (maybe is null)
    Console.WriteLine("Not created yet");
```

Only `RecordNotFound` means absent: a malformed key or a missing repository still throws.

### Update (Put)

`PutAsync` is an upsert — it creates the record if it doesn't exist, or replaces it if it does:

```csharp
await todos.PutAsync(created.RecordKey, new TodoItem
{
    Title = "Buy groceries",
    Completed = true,
    Priority = 2,
});
```

For optimistic concurrency, pass the expected CID:

```csharp
await todos.PutAsync(
    created.RecordKey,
    updatedItem,
    swapRecord: item.Cid);  // Fails if record was modified since read
```

### Delete

```csharp
await todos.DeleteAsync(created.RecordKey);
```

### Check Existence

```csharp
bool exists = await todos.ExistsAsync(RecordKey.Parse("some-record-key")); // FindAsync(...) is not null
```

## Listing Records

### Paginated Listing

```csharp
var page = await todos.ListAsync(limit: 25);

foreach (var record in page.Records)
{
    Console.WriteLine($"[{record.RecordKey}] {record.Value.Title}");
}

// Check for more pages
if (page.HasMore)
{
    var nextPage = await todos.ListAsync(limit: 25, cursor: page.Cursor);
}
```

### Enumerate All Records

For iterating over all records with automatic pagination:

```csharp
await foreach (var record in todos.EnumerateAsync())
{
    Console.WriteLine($"{record.RecordKey}: {record.Value.Title}");
}

// With custom page size (the server's default when omitted)
await foreach (var record in todos.EnumerateAsync(pageSize: 50))
{
    // Process each record
}
```

Enumeration stops when the server returns no cursor, an empty one, or one it already returned.

### Reverse Order

```csharp
var page = await todos.ListAsync(limit: 25, reverse: true);
```

## Reading Other Users' Data

One of the key features of AT Protocol is that records are public by default. You can read records from any user's repository:

```csharp
var other = Did.Parse("did:plc:otherperson");

// Read a specific record from another user (FindFromAsync returns null when it is absent)
var item = await todos.GetFromAsync(other, RecordKey.Parse("record-key"));

// List records from another user
var page = await todos.ListFromAsync(other, limit: 50);

// Enumerate all of their records
await foreach (var record in todos.EnumerateFromAsync(other))
{
    Console.WriteLine(record.Value.Title);
}
```

## Multiple Collections (Multi-App)

A single AT Protocol account supports data from many applications by using different collection NSIDs:

```csharp
await client.LoginAsync("alice.example.com", "app-password");

// Different apps, same account, different collections
var todos = client.GetCollection<TodoItem>();
var bookmarks = client.GetCollection<Bookmark>();
var notes = client.GetCollection<Note>();
var recipes = client.GetCollection<Recipe>();

// Each collection is independent
await todos.CreateAsync(new TodoItem { Title = "Cook dinner" });
await recipes.CreateAsync(new Recipe { Name = "Pasta Carbonara" });
```

## Record Type Design Guidelines

### NSID Naming Convention

Follow reverse-domain notation for your Lexicon NSIDs:

```
com.yourcompany.appname.recordtype
```

Examples:
- `com.example.todo.item`
- `com.example.todo.project`
- `com.example.bookmarks.bookmark`
- `com.example.bookmarks.folder`
- `org.myorg.inventory.product`

### Field Naming

Use camelCase for JSON fields (AT Protocol convention):

```csharp
public class MyRecord : AtProtoRecord, IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.myapp.record");

    public override string Type => Collection;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("lastModified")]
    public AtDatetime? LastModified { get; set; }

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }
}
```

### Timestamps

Type Lexicon `datetime` fields as `AtDatetime`. It keeps the exact text it was read with, so a
record you read and write back keeps its CID, and it writes the canonical form
(`yyyy-MM-ddTHH:mm:ss.fffZ`) for values you create:

```csharp
[JsonPropertyName("dueDate")]
public AtDatetime? DueDate { get; set; }

// Set like this:
record.DueDate = AtDatetime.Now();
record.DueDate = AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(3));
record.DueDate = AtDatetime.Parse("2024-06-15T14:30:00.000Z");

// Read it back:
if (record.DueDate?.TryGetValue(out var due) == true)
    Console.WriteLine(due.LocalDateTime);
```

Reading is lenient: a value that isn't a valid atproto datetime is kept verbatim with
`IsValid == false` rather than failing the whole record. `AtProtoRecord.CreatedAt` is an
`AtDatetime?`: `null` until you set it or `CreateAsync` stamps it, and exactly what was stored
(or `null`, when nothing was) for a record you read, so writing that record back with `PutAsync`
never invents a creation time.

### References Between Records

Use AT URIs to reference other records:

```csharp
public class Comment : AtProtoRecord, IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.comment");

    public override string Type => Collection;

    [JsonPropertyName("todoUri")]
    public required AtUri TodoUri { get; set; }  // AT URI to the todo item

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}
```

### Blobs (Images, Files)

First upload, then reference:

```csharp
// Upload a blob
var blobResponse = await client.Repo.UploadBlobAsync(
    filePath: "/path/to/image.jpg",
    mimeType: "image/jpeg");

// Reference it in your record
public class PhotoRecord : AtProtoRecord, IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.photos.photo");

    public override string Type => Collection;

    [JsonPropertyName("image")]
    public BlobRef? Image { get; set; }

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";
}
```

## Unions and unknown fields

Lexicons evolve: new union variants appear (Bluesky's gallery embed did, in 2026), and records
gain optional fields. The SDK is built so that data it doesn't know about neither breaks a
response nor gets lost when you write a record back. The same two mechanisms are available to
your own record types.

### Unions

A Lexicon union maps to an abstract base class marked `[AtProtoUnion]`, with one subclass per
variant, discriminated by `$type`. Declare the variants you know with `[JsonDerivedType]`, using
the discriminator the Lexicon defines: `<nsid>#<defName>` for a `defs` entry, or the bare NSID
for a `main` def.

Unions are **open** unless the Lexicon says `"closed": true`, so an open union also names an
*unknown* variant. A `$type` the base neither declares nor has registered reads as that variant.
It keeps the whole object as a `JsonElement` and writes it back byte-for-byte:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

[AtProtoUnion(typeof(UnknownAttribution))]
[JsonDerivedType(typeof(AuthorAttribution), "com.example.recipe.defs#attributionAuthor")]
[JsonDerivedType(typeof(SourceAttribution), "com.example.recipe.defs#attributionSource")]
public abstract class RecipeAttribution : LexObject;

public sealed class AuthorAttribution : RecipeAttribution
{
    [JsonPropertyName("did")]
    public string Did { get; set; } = "";
}

public sealed class SourceAttribution : RecipeAttribution
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

// Needs exactly this constructor shape; the SDK calls it with the $type and the raw object.
public sealed class UnknownAttribution(string type, JsonElement raw)
    : RecipeAttribution, IUnknownUnionVariant
{
    public string Type { get; } = type;
    public JsonElement Raw { get; } = raw;
}
```

A `switch` over the base should handle the unknown arm. The compiler won't insist, since the
base isn't sealed:

```csharp
var credit = recipe.Attribution switch
{
    AuthorAttribution a => a.Did,
    SourceAttribution s => s.Url,
    UnknownAttribution u => $"(unsupported attribution {u.Type})",
    _ => null,
};
```

For a closed union, write `[AtProtoUnion(Closed = true)]` and no unknown variant. An unknown
`$type` then fails with a `JsonException`, which is what the Lexicon asks for. In the SDK only
`com.atproto.repo.applyWrites` is closed.

Behavior worth knowing:

- `$type` may appear anywhere in the object; the Bluesky appview often puts it last.
- A variant writes its `$type` first, whether you serialize it through the base or on its own.
- The SDK's Bluesky, chat, Ozone and `com.atproto` unions follow this pattern, each with an
  `Unknown{Base}` variant: `UnknownEmbed`, `UnknownEmbedView`, `UnknownFacetFeature`,
  `UnknownThreadNode`, `UnknownModEvent`, `UnknownConvoLogEntry` and so on. The Spaces models
  don't yet.
- A plain `[JsonPolymorphic]` base still works. It keeps `System.Text.Json`'s own, strict
  behavior, so an unknown `$type` fails the whole response.

### Registering variants of a union you don't own

To add a variant to a union declared elsewhere, such as your own embed type on the SDK's
`EmbedBase`, register it once at startup:

```csharp
LexiconTypeRegistry.Instance
    .RegisterUnionVariant<EmbedBase, RecipeEmbed>("com.example.recipe.embed");
```

That is all it takes. `AtProtoJsonDefaults.Options`, which every SDK client, `RecordCollection<T>`
and Jetstream's `GetRecord<T>()` use, consults the registry. A registered variant then reads as
your type everywhere instead of as `UnknownEmbed`, and writes with its `$type`. Reads check the
registry on every discriminator the base doesn't declare, so even a late registration is picked
up. Still, register before you first *serialize* the variant, because its contract is built on
first use.

A discriminator can map to only one type per base: registering one that is already declared or
registered for a different type throws `ArgumentException`. The same goes for a base that isn't a
union.

### Unknown fields

Every SDK model of a Lexicon record or object derives from `LexObject`, and so does
`AtProtoRecord`. `LexObject` has an `ExtensionData` dictionary. Any field a model doesn't declare
lands there on read and is written back after the declared ones:

```csharp
// A newer app wrote "priority", which this TodoItem doesn't declare.
var item = await todos.GetAsync(rkey);
item.Value.Completed = true;

await todos.PutAsync(rkey, item.Value, swapRecord: item.Cid); // "priority" is still there
```

- `ExtensionData` stays `null` when there is nothing extra, so a model that matches the wire
  costs nothing.
- `$type` never ends up in `ExtensionData` for a record or a union variant. The record's own
  `$type` wins on write.
- Your plain classes (not derived from `LexObject` or `AtProtoRecord`) drop unknown fields as
  before. Derive from `LexObject` to opt in.
- Views that come back from the appview have `ExtensionData` too. It's a quick way to read a new
  field before the SDK models it:
  `post.ExtensionData?.TryGetValue("bookmarkCount", out var count) == true`.

`client.Bsky.UpdateProfileAsync` is built on this. It reads the profile, lets your callback edit
it (`p => p.Description = "…"`), and writes it back guarded by `swapRecord`. Every field you
didn't touch survives, including ones this SDK version doesn't know about.

## Error Handling

```csharp
using ATProtoNet.Http;

try
{
    var item = await todos.GetAsync(RecordKey.Parse("nonexistent-key"));
}
catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
{
    Console.WriteLine("Record does not exist"); // or use FindAsync, which returns null
}
catch (XrpcAuthenticationException)
{
    Console.WriteLine("Sign in first: own-repository calls need a session");
}
catch (XrpcException ex)
{
    Console.WriteLine($"XRPC Error: {ex.Error} — {ex.ErrorMessage}");
    Console.WriteLine($"Status: {ex.StatusCode}");
}
```

## Next Steps

- [Custom XRPC Endpoints](custom-xrpc.md) — Define and call custom Lexicon methods
- [Batch Operations](batch-operations.md) — Atomic multi-record writes
- [Blob Upload](blob-upload.md) — Upload images and files

## Distributing Lexicons as NuGet Packages

A package that ships Lexicon types needs no registration for its own records and unions; the
attributes on the types are enough. It needs registration only for variants it adds to unions
declared elsewhere, like a custom embed on `EmbedBase`. Bundle those in an `ILexiconPlugin`:

```csharp
using ATProtoNet.Serialization;

public class MyAppLexicons : ILexiconPlugin
{
    public void Register(ILexiconTypeRegistrar registrar)
    {
        registrar.RegisterUnionVariant<EmbedBase, CustomEmbed>("com.example.embed.custom");
    }
}
```

Consumers load it once at startup:

```csharp
LexiconTypeRegistry.Instance.LoadPlugin<MyAppLexicons>();
```

From then on every SDK client reads and writes the plugin's variants, alongside the ones the SDK
declares.
