# Standard.site Integration

ATProto.NET provides native support for [Standard.site](https://standard.site) long-form publishing lexicons. Access the client through `client.Site`.

## Overview

Standard.site is a long-form publishing platform built on AT Protocol. It uses three main record types:

| Record Type | NSID | Description |
|-------------|------|-------------|
| Publication | `site.standard.publication` | Blog/site identity |
| Document | `site.standard.document` | Published articles/pages |
| Subscription | `site.standard.graph.subscription` | Follow/subscribe to publications |

`StandardSiteClient` is a thin typed wrapper over `com.atproto.repo.*`, so **every method takes the
repository (DID or handle) as its first argument** — your own (`client.Did!`) for writes, anyone's for
reads.

## Publications

A publication represents a blog or website identity.

### Create a Publication

```csharp
using ATProtoNet.Lexicon.Site.Standard.Publication;

await client.Site.CreatePublicationAsync(client.Did!, new PublicationRecord
{
    Url = "https://myblog.example.com",
    Name = "My Blog",
    Description = "A blog about .NET and AT Protocol",
    BasicTheme = new BasicTheme
    {
        Background = new ThemeColorRgb { R = 255, G = 255, B = 255 },
        Foreground = new ThemeColorRgb { R = 17, G = 24, B = 39 },
        Accent = new ThemeColorRgb { R = 59, G = 130, B = 246 },
        AccentForeground = new ThemeColorRgb { R = 255, G = 255, B = 255 },
    },
}, rkey: "self");
```

`Url` and `Name` are required; the record key for a publication is conventionally `self`.

### Get a Publication

```csharp
var pub = await client.Site.GetPublicationAsync("did:plc:abc123", "self");

Console.WriteLine($"Name: {pub.Value.Name}");
Console.WriteLine($"URL: {pub.Value.Url}");
```

### Update a Publication

```csharp
await client.Site.PutPublicationAsync(client.Did!, "self", new PublicationRecord
{
    Url = "https://myblog.example.com",
    Name = "My Updated Blog",
    Description = "Now with even more AT Protocol content",
});
```

### Delete a Publication

```csharp
await client.Site.DeletePublicationAsync(client.Did!, "self");
```

## Documents

Documents represent published articles, blog posts, or pages.

### Create a Document

```csharp
using ATProtoNet.Lexicon.Site.Standard.Document;

await client.Site.CreateDocumentAsync(client.Did!, new DocumentRecord
{
    Site = $"at://{client.Did}/site.standard.publication/self",
    Title = "Getting Started with ATProto.NET",
    PublishedAt = DateTime.UtcNow.ToString("o"),
    Path = "/getting-started",
    Tags = ["atproto", "dotnet", "tutorial"],
});
```

`Site` (the publication this belongs to, as an `at://` URI or an `https://` URL), `Title`, and
`PublishedAt` are required.

### List Documents

`ListDocumentsAsync` returns the raw `com.atproto.repo.listRecords` response, so each entry's `Value`
is a `JsonElement` — deserialize it to get typed access:

```csharp
var docs = await client.Site.ListDocumentsAsync("did:plc:abc123", limit: 25);

foreach (var entry in docs.Records)
{
    var doc = entry.Value.Deserialize<DocumentRecord>(AtProtoJsonDefaults.Options)!;
    Console.WriteLine($"Title: {doc.Title}");
    Console.WriteLine($"Path: {doc.Path}");
    Console.WriteLine($"Tags: {string.Join(", ", doc.Tags ?? [])}");
}
```

For typed listing and automatic pagination, use a `RecordCollection<T>` instead — see
[Custom Lexicon Records](custom-records.md):

```csharp
var documents = client.GetCollection<DocumentRecord>("site.standard.document");
await foreach (var record in documents.EnumerateFromAsync("did:plc:abc123"))
    Console.WriteLine(record.Value.Title);
```

### Get a Document

```csharp
var doc = await client.Site.GetDocumentAsync("did:plc:abc123", "doc-key");
Console.WriteLine(doc.Value.Title);
```

### Update a Document

```csharp
await client.Site.PutDocumentAsync(client.Did!, "doc-key", new DocumentRecord
{
    Site = $"at://{client.Did}/site.standard.publication/self",
    Title = "Updated: Getting Started with ATProto.NET",
    PublishedAt = originalPublishedAt,
    UpdatedAt = DateTime.UtcNow.ToString("o"),
    Path = "/getting-started",
    Tags = ["atproto", "dotnet", "tutorial", "updated"],
});
```

### Delete a Document

```csharp
await client.Site.DeleteDocumentAsync(client.Did!, "doc-key");
```

## Subscriptions

Subscribe to other publications:

### Subscribe to a Publication

```csharp
using ATProtoNet.Lexicon.Site.Standard.Graph;

await client.Site.CreateSubscriptionAsync(client.Did!, new SubscriptionRecord
{
    Publication = "at://did:plc:publisher/site.standard.publication/self",
});
```

### List Subscriptions

```csharp
var subs = await client.Site.ListSubscriptionsAsync(client.Did!);

foreach (var entry in subs.Records)
{
    var sub = entry.Value.Deserialize<SubscriptionRecord>(AtProtoJsonDefaults.Options)!;
    Console.WriteLine($"Subscribed to: {sub.Publication}");
}
```

### Unsubscribe

```csharp
await client.Site.DeleteSubscriptionAsync(client.Did!, "subscription-key");
```

## Themes

Publications carry an optional `BasicTheme`, built from RGB colors. All four colors are required when
a theme is present:

```csharp
var theme = new BasicTheme
{
    Background = new ThemeColorRgb { R = 255, G = 255, B = 255 },
    Foreground = new ThemeColorRgb { R = 17, G = 24, B = 39 },
    Accent = new ThemeColorRgb { R = 59, G = 130, B = 246 },
    AccentForeground = new ThemeColorRgb { R = 255, G = 255, B = 255 },
};
```

`ThemeColorRgba` adds an integer `A` (alpha) component for the places the Lexicon accepts it.

## Client Pattern

The `StandardSiteClient` follows the same pattern as `client.Bsky`, `client.Chat`, and `client.Ozone` — it wraps AT Protocol repo operations with a typed, discoverable API:

```csharp
// Access via the top-level client
var siteClient = client.Site;

// All CRUD operations go through the named repository, so reads work for any account
var theirDocs = await client.Site.ListDocumentsAsync("did:plc:someoneelse");
```

## Next Steps

- [Custom Lexicon Records](custom-records.md) — Build your own record types
- [API Reference](api-reference.md) — Complete StandardSiteClient methods
