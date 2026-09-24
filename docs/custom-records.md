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

The `AtProtoRecord` base class automatically handles the `$type` and `createdAt` fields:

```csharp
using System.Text.Json.Serialization;
using ATProtoNet;

public class TodoItem : AtProtoRecord
{
    // The Lexicon NSID for this record type
    [JsonPropertyName("$type")]
    public override string Type => "com.example.todo.item";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
```

This serializes to:
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

You don't have to extend `AtProtoRecord`. Any serializable class works:

```csharp
public class Bookmark
{
    [JsonPropertyName("$type")]
    public string Type => "com.example.bookmarks.bookmark";

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
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
```

The collection NSID should match your Lexicon definition. By convention, it follows reverse-domain notation: `com.yourcompany.appname.recordtype`.

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
```

The server generates a TID-based record key. You can also specify one:

```csharp
var created = await todos.CreateAsync(
    new TodoItem { Title = "Custom key" },
    rkey: "my-custom-key");
```

### Read

```csharp
var item = await todos.GetAsync(created.RecordKey);

Console.WriteLine($"Title: {item.Value.Title}");
Console.WriteLine($"URI: {item.Uri}");
Console.WriteLine($"CID: {item.Cid}");
Console.WriteLine($"Key: {item.RecordKey}");
```

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
bool exists = await todos.ExistsAsync("some-record-key");
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

// With custom page size
await foreach (var record in todos.EnumerateAsync(pageSize: 50))
{
    // Process each record
}
```

### Reverse Order

```csharp
var page = await todos.ListAsync(limit: 25, reverse: true);
```

## Reading Other Users' Data

One of the key features of AT Protocol is that records are public by default. You can read records from any user's repository:

```csharp
// Read a specific record from another user
var item = await todos.GetFromAsync("did:plc:otherperson", "record-key");

// List records from another user
var page = await todos.ListFromAsync("did:plc:otherperson", limit: 50);

// Enumerate all of their records
await foreach (var record in todos.EnumerateFromAsync("did:plc:otherperson"))
{
    Console.WriteLine(record.Value.Title);
}
```

## Multiple Collections (Multi-App)

A single AT Protocol account supports data from many applications by using different collection NSIDs:

```csharp
await client.LoginAsync("alice.example.com", "app-password");

// Different apps, same account, different collections
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
var bookmarks = client.GetCollection<Bookmark>("com.example.bookmarks.bookmark");
var notes = client.GetCollection<Note>("com.example.notes.note");
var recipes = client.GetCollection<Recipe>("com.example.recipes.recipe");

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
public class MyRecord : AtProtoRecord
{
    [JsonPropertyName("$type")]
    public override string Type => "com.example.myapp.record";

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("lastModified")]
    public string? LastModified { get; set; }

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }
}
```

### Timestamps

Use ISO 8601 format for date/time fields:

```csharp
[JsonPropertyName("dueDate")]
public string? DueDate { get; set; }

// Set like this:
record.DueDate = DateTime.UtcNow.ToString("o");
record.DueDate = "2024-06-15T14:30:00.000Z";
```

### References Between Records

Use AT URIs to reference other records:

```csharp
public class Comment : AtProtoRecord
{
    [JsonPropertyName("$type")]
    public override string Type => "com.example.todo.comment";

    [JsonPropertyName("todoUri")]
    public string TodoUri { get; set; } = "";  // AT URI to the todo item

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
public class PhotoRecord : AtProtoRecord
{
    [JsonPropertyName("$type")]
    public override string Type => "com.example.photos.photo";

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
- The SDK's Bluesky, Ozone and `com.atproto` unions follow this pattern, each with an
  `Unknown{Base}` variant: `UnknownEmbed`, `UnknownEmbedView`, `UnknownFacetFeature`,
  `UnknownThreadNode`, `UnknownModEvent` and so on. The Spaces models don't yet.
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

`AtProtoClient.UpdateProfileAsync` is built on this. It reads the profile, lets your callback edit
it (`p => p.Description = "…"`), and writes it back guarded by `swapRecord`. Every field you
didn't touch survives, including ones this SDK version doesn't know about.

## Error Handling

```csharp
using ATProtoNet.Http;

try
{
    var item = await todos.GetAsync("nonexistent-key");
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "RecordNotFound")
{
    Console.WriteLine("Record does not exist");
}
catch (AtProtoHttpException ex)
{
    Console.WriteLine($"XRPC Error: {ex.ErrorType} — {ex.ErrorMessage}");
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
