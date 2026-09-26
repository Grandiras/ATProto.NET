# Standard.site Integration

ATProto.NET provides native support for [Standard.site](https://standard.site) long-form publishing lexicons. Access the client through `client.Site`.

## Overview

Standard.site is a long-form publishing platform built on AT Protocol. It uses four record types:

| Record Type | NSID | Description |
|-------------|------|-------------|
| Publication | `site.standard.publication` | Blog/site identity |
| Document | `site.standard.document` | Published articles/pages |
| Subscription | `site.standard.graph.subscription` | Follow/subscribe to publications |
| Recommendation | `site.standard.graph.recommend` | Recommend a document |

`StandardSiteClient` is a thin typed wrapper over `com.atproto.repo.*`, so **every method takes the
repository (an `AtIdentifier`: a DID or handle) as its first argument** — your own (`client.Did!`) for
writes, anyone's for reads. Record keys are `RecordKey`s, and the `Get*Async` methods also take the
record's `AtUri`. All of these types live in `ATProtoNet.Identity`.

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
}, rkey: RecordKey.Parse("self"));
```

`Url` and `Name` are required; the record key for a publication is conventionally `self`.

### Get a Publication

```csharp
var pub = await client.Site.GetPublicationAsync(Did.Parse("did:plc:abc123"), RecordKey.Parse("self"));

Console.WriteLine($"Name: {pub.Value.Name}");
Console.WriteLine($"URL: {pub.Value.Url}");

// The same record by its AT URI, for example a subscription's Publication
var same = await client.Site.GetPublicationAsync(
    AtUri.Parse("at://did:plc:abc123/site.standard.publication/self"));
```

### Update a Publication

```csharp
await client.Site.PutPublicationAsync(client.Did!, RecordKey.Parse("self"), new PublicationRecord
{
    Url = "https://myblog.example.com",
    Name = "My Updated Blog",
    Description = "Now with even more AT Protocol content",
});
```

### Delete a Publication

```csharp
await client.Site.DeletePublicationAsync(client.Did!, RecordKey.Parse("self"));
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
    PublishedAt = AtDatetime.Now(),
    Path = "/getting-started",
    Tags = ["atproto", "dotnet", "tutorial"],
});
```

`Site` (the publication this belongs to, as an `at://` URI or an `https://` URL), `Title`, and
`PublishedAt` are required.

### List Documents

`ListDocumentsAsync` returns one `RecordPage<DocumentRecord>`, the same page type
[`RecordCollection<T>`](custom-records.md) uses: each entry carries the typed record, its URI, CID
and record key. A record that is not a valid `DocumentRecord` throws `XrpcResponseFormatException`.

```csharp
var docs = await client.Site.ListDocumentsAsync(Did.Parse("did:plc:abc123"), limit: 25);

foreach (var entry in docs.Records)
{
    Console.WriteLine($"Title: {entry.Value.Title}");
    Console.WriteLine($"Path: {entry.Value.Path}");
    Console.WriteLine($"Tags: {string.Join(", ", entry.Value.Tags ?? [])}");
}
```

`EnumerateDocumentsAsync` fetches the pages for you (as do `EnumeratePublicationsAsync`,
`EnumerateSubscriptionsAsync` and `EnumerateRecommendationsAsync`):

```csharp
await foreach (var entry in client.Site.EnumerateDocumentsAsync(Did.Parse("did:plc:abc123")))
    Console.WriteLine($"{entry.RecordKey}: {entry.Value.Title}");
```

### Get a Document

```csharp
var doc = await client.Site.GetDocumentAsync(Did.Parse("did:plc:abc123"), RecordKey.Parse("doc-key"));
Console.WriteLine(doc.Value.Title);
```

### Update a Document

```csharp
await client.Site.PutDocumentAsync(client.Did!, RecordKey.Parse("doc-key"), new DocumentRecord
{
    Site = $"at://{client.Did}/site.standard.publication/self",
    Title = "Updated: Getting Started with ATProto.NET",
    PublishedAt = originalPublishedAt,
    UpdatedAt = AtDatetime.Now(),
    Path = "/getting-started",
    Tags = ["atproto", "dotnet", "tutorial", "updated"],
});
```

### Delete a Document

```csharp
await client.Site.DeleteDocumentAsync(client.Did!, RecordKey.Parse("doc-key"));
```

## Subscriptions

Subscribe to other publications:

### Subscribe to a Publication

```csharp
using ATProtoNet.Lexicon.Site.Standard.Graph;

await client.Site.CreateSubscriptionAsync(client.Did!, new SubscriptionRecord
{
    Publication = AtUri.Parse("at://did:plc:publisher/site.standard.publication/self"),
});
```

### List Subscriptions

```csharp
var subs = await client.Site.ListSubscriptionsAsync(client.Did!);

foreach (var entry in subs.Records)
{
    var publication = await client.Site.GetPublicationAsync(entry.Value.Publication);
    Console.WriteLine($"Subscribed to: {publication.Value.Name}");
}
```

### Unsubscribe

```csharp
await client.Site.DeleteSubscriptionAsync(client.Did!, RecordKey.Parse("subscription-key"));
```

## Recommendations

Recommend a document to your followers:

```csharp
using ATProtoNet.Lexicon.Site.Standard.Graph;

var created = await client.Site.CreateRecommendationAsync(client.Did!, new RecommendRecord
{
    Document = AtUri.Parse("at://did:plc:author/site.standard.document/doc-key"),
    CreatedAt = AtDatetime.Now(),
});

await foreach (var entry in client.Site.EnumerateRecommendationsAsync(client.Did!))
{
    var doc = await client.Site.GetDocumentAsync(entry.Value.Document);
    Console.WriteLine($"Recommended: {doc.Value.Title}");
}

// Withdraw it again.
await client.Site.DeleteRecommendationAsync(client.Did!, created.Uri.RecordKey!);
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
var theirDocs = await client.Site.ListDocumentsAsync(Did.Parse("did:plc:someoneelse"));
```

## Next Steps

- [Custom Lexicon Records](custom-records.md) — Build your own record types
- [API Reference](api-reference.md) — Complete StandardSiteClient methods
