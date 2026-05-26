# Standard.site Integration

ATProto.NET provides native support for [Standard.site](https://standard.site) long-form publishing lexicons. Access the client through `client.Site`.

## Overview

Standard.site is a long-form publishing platform built on AT Protocol. It uses three main record types:

| Record Type | NSID | Description |
|-------------|------|-------------|
| Publication | `site.standard.publication` | Blog/site identity |
| Document | `site.standard.document` | Published articles/pages |
| Subscription | `site.standard.graph.subscription` | Follow/subscribe to publications |

## Publications

A publication represents a blog or website identity.

### Create a Publication

```csharp
await client.Site.CreatePublicationAsync(new PublicationRecord
{
    Url = "https://myblog.example.com",
    Name = "My Blog",
    Description = "A blog about .NET and AT Protocol",
    Theme = new BasicTheme
    {
        PrimaryColor = new ThemeColorRgb { R = 59, G = 130, B = 246 },
    },
});
```

### Get a Publication

```csharp
var pub = await client.Site.GetPublicationAsync(did: "did:plc:abc123", rkey: "self");
Console.WriteLine($"Name: {pub.Value.Name}");
Console.WriteLine($"URL: {pub.Value.Url}");
```

### Update a Publication

```csharp
await client.Site.UpdatePublicationAsync("self", new PublicationRecord
{
    Url = "https://myblog.example.com",
    Name = "My Updated Blog",
    Description = "Now with even more AT Protocol content",
});
```

### Delete a Publication

```csharp
await client.Site.DeletePublicationAsync(rkey: "self");
```

## Documents

Documents represent published articles, blog posts, or pages.

### Create a Document

```csharp
await client.Site.CreateDocumentAsync(new DocumentRecord
{
    Title = "Getting Started with ATProto.NET",
    Path = "/getting-started",
    Tags = ["atproto", "dotnet", "tutorial"],
    CreatedAt = DateTime.UtcNow.ToString("o"),
});
```

### List Documents

```csharp
var docs = await client.Site.ListDocumentsAsync(limit: 25);

foreach (var doc in docs.Records)
{
    Console.WriteLine($"Title: {doc.Value.Title}");
    Console.WriteLine($"Path: {doc.Value.Path}");
    Console.WriteLine($"Tags: {string.Join(", ", doc.Value.Tags ?? [])}");
}
```

### Get a Document

```csharp
var doc = await client.Site.GetDocumentAsync(did: "did:plc:abc123", rkey: "doc-key");
```

### Update a Document

```csharp
await client.Site.UpdateDocumentAsync("doc-key", new DocumentRecord
{
    Title = "Updated: Getting Started with ATProto.NET",
    Path = "/getting-started",
    Tags = ["atproto", "dotnet", "tutorial", "updated"],
});
```

### Delete a Document

```csharp
await client.Site.DeleteDocumentAsync(rkey: "doc-key");
```

## Subscriptions

Subscribe to other publications:

### Subscribe to a Publication

```csharp
await client.Site.CreateSubscriptionAsync(new SubscriptionRecord
{
    Subject = "did:plc:publisher",
});
```

### List Subscriptions

```csharp
var subs = await client.Site.ListSubscriptionsAsync();

foreach (var sub in subs.Records)
{
    Console.WriteLine($"Subscribed to: {sub.Value.Subject}");
}
```

### Unsubscribe

```csharp
await client.Site.DeleteSubscriptionAsync(rkey: "subscription-key");
```

## Themes

Publications support theming with color definitions:

```csharp
// RGB color
var color = new ThemeColorRgb { R = 59, G = 130, B = 246 };

// RGBA color (with transparency)
var colorWithAlpha = new ThemeColorRgba { R = 59, G = 130, B = 246, A = 0.8 };

// Basic theme
var theme = new BasicTheme
{
    PrimaryColor = color,
};
```

## Client Pattern

The `StandardSiteClient` follows the same pattern as `client.Bsky`, `client.Chat`, and `client.Ozone` — it wraps AT Protocol repo operations with a typed, discoverable API:

```csharp
// Access via the top-level client
var siteClient = client.Site;

// All CRUD operations go through the user's PDS repository
```

## Next Steps

- [Custom Lexicon Records](custom-records.md) — Build your own record types
- [API Reference](api-reference.md) — Complete StandardSiteClient methods
