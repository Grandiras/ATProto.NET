using System.Text.Json;
using ATProtoNet.Lexicon.Site.Standard.Document;
using ATProtoNet.Lexicon.Site.Standard.Graph;
using ATProtoNet.Lexicon.Site.Standard.Publication;
using ATProtoNet.Models;

namespace ATProtoNet.Tests.Lexicon.Site.Standard;

public class StandardSiteModelTests
{
    // ──────────────────────────────────────────────────────────
    //  Publication models
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void PublicationRecord_HasCorrectType()
    {
        var pub = new PublicationRecord { Url = "https://example.com", Name = "Test" };
        Assert.Equal("site.standard.publication", pub.Type);
    }

    [Fact]
    public void PublicationRecord_SerializesRequiredFields()
    {
        var pub = new PublicationRecord { Url = "https://example.com", Name = "My Blog" };
        var json = JsonSerializer.Serialize(pub);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("site.standard.publication", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal("https://example.com", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("My Blog", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void PublicationRecord_SerializesOptionalFields()
    {
        var pub = new PublicationRecord
        {
            Url = "https://example.com",
            Name = "My Blog",
            Description = "A great blog",
            Preferences = new PublicationPreferences { ShowInDiscover = true }
        };

        var json = JsonSerializer.Serialize(pub);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("A great blog", doc.RootElement.GetProperty("description").GetString());
        Assert.True(doc.RootElement.GetProperty("preferences").GetProperty("showInDiscover").GetBoolean());
    }

    [Fact]
    public void PublicationRecord_RoundTrips()
    {
        var pub = new PublicationRecord
        {
            Url = "https://example.com",
            Name = "My Blog",
            Description = "A great blog"
        };

        var json = JsonSerializer.Serialize(pub);
        var deserialized = JsonSerializer.Deserialize<PublicationRecord>(json)!;

        Assert.Equal(pub.Url, deserialized.Url);
        Assert.Equal(pub.Name, deserialized.Name);
        Assert.Equal(pub.Description, deserialized.Description);
    }

    // ──────────────────────────────────────────────────────────
    //  Theme models
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void BasicTheme_HasCorrectType()
    {
        var theme = new BasicTheme
        {
            Background = new ThemeColorRgb { R = 255, G = 255, B = 255 },
            Foreground = new ThemeColorRgb { R = 0, G = 0, B = 0 },
            Accent = new ThemeColorRgb { R = 59, G = 130, B = 246 },
            AccentForeground = new ThemeColorRgb { R = 255, G = 255, B = 255 }
        };

        Assert.Equal("site.standard.theme.basic", theme.Type);
    }

    [Fact]
    public void ThemeColorRgb_SerializesWithType()
    {
        var color = new ThemeColorRgb { R = 59, G = 130, B = 246 };
        var json = JsonSerializer.Serialize(color);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("site.standard.theme.color#rgb", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal(59, doc.RootElement.GetProperty("r").GetInt32());
        Assert.Equal(130, doc.RootElement.GetProperty("g").GetInt32());
        Assert.Equal(246, doc.RootElement.GetProperty("b").GetInt32());
    }

    [Fact]
    public void ThemeColorRgba_SerializesWithAlpha()
    {
        var color = new ThemeColorRgba { R = 0, G = 0, B = 0, A = 50 };
        var json = JsonSerializer.Serialize(color);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("site.standard.theme.color#rgba", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal(50, doc.RootElement.GetProperty("a").GetInt32());
    }

    [Fact]
    public void BasicTheme_FullRoundTrip()
    {
        var theme = new BasicTheme
        {
            Background = new ThemeColorRgb { R = 255, G = 255, B = 255 },
            Foreground = new ThemeColorRgb { R = 31, G = 41, B = 55 },
            Accent = new ThemeColorRgb { R = 59, G = 130, B = 246 },
            AccentForeground = new ThemeColorRgb { R = 255, G = 255, B = 255 }
        };

        var json = JsonSerializer.Serialize(theme);
        var deserialized = JsonSerializer.Deserialize<BasicTheme>(json)!;

        Assert.Equal(255, deserialized.Background.R);
        Assert.Equal(31, deserialized.Foreground.R);
        Assert.Equal(59, deserialized.Accent.R);
    }

    // ──────────────────────────────────────────────────────────
    //  Document models
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void DocumentRecord_HasCorrectType()
    {
        var doc = new DocumentRecord
        {
            Site = "https://example.com",
            Title = "Test",
            PublishedAt = "2024-01-20T14:30:00.000Z"
        };
        Assert.Equal("site.standard.document", doc.Type);
    }

    [Fact]
    public void DocumentRecord_SerializesRequiredFields()
    {
        var record = new DocumentRecord
        {
            Site = "at://did:plc:abc/site.standard.publication/xyz",
            Title = "Getting Started",
            PublishedAt = "2024-01-20T14:30:00.000Z"
        };

        var json = JsonSerializer.Serialize(record);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("site.standard.document", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal("at://did:plc:abc/site.standard.publication/xyz", doc.RootElement.GetProperty("site").GetString());
        Assert.Equal("Getting Started", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("2024-01-20T14:30:00.000Z", doc.RootElement.GetProperty("publishedAt").GetString());
    }

    [Fact]
    public void DocumentRecord_SerializesOptionalFields()
    {
        var record = new DocumentRecord
        {
            Site = "https://example.com",
            Title = "A Post",
            PublishedAt = "2024-01-20T14:30:00.000Z",
            Path = "/blog/a-post",
            Description = "An article about things",
            TextContent = "Full text here",
            Tags = ["tutorial", "atproto"],
            UpdatedAt = "2024-02-01T10:00:00.000Z"
        };

        var json = JsonSerializer.Serialize(record);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("/blog/a-post", doc.RootElement.GetProperty("path").GetString());
        Assert.Equal("An article about things", doc.RootElement.GetProperty("description").GetString());
        Assert.Equal("Full text here", doc.RootElement.GetProperty("textContent").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("tags").GetArrayLength());
        Assert.Equal("2024-02-01T10:00:00.000Z", doc.RootElement.GetProperty("updatedAt").GetString());
    }

    [Fact]
    public void DocumentRecord_WithBskyPostRef()
    {
        var record = new DocumentRecord
        {
            Site = "https://example.com",
            Title = "Post with comments",
            PublishedAt = "2024-01-20T14:30:00.000Z",
            BskyPostRef = new StrongRef { Uri = "at://did:plc:abc/app.bsky.feed.post/xyz", Cid = "bafytest" }
        };

        var json = JsonSerializer.Serialize(record);
        var doc = JsonDocument.Parse(json);

        var bskyRef = doc.RootElement.GetProperty("bskyPostRef");
        Assert.Equal("at://did:plc:abc/app.bsky.feed.post/xyz", bskyRef.GetProperty("uri").GetString());
        Assert.Equal("bafytest", bskyRef.GetProperty("cid").GetString());
    }

    [Fact]
    public void DocumentRecord_RoundTrips()
    {
        var record = new DocumentRecord
        {
            Site = "at://did:plc:abc/site.standard.publication/xyz",
            Title = "Test Post",
            PublishedAt = "2024-01-20T14:30:00.000Z",
            Path = "/blog/test",
            Tags = ["test"]
        };

        var json = JsonSerializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<DocumentRecord>(json)!;

        Assert.Equal(record.Site, deserialized.Site);
        Assert.Equal(record.Title, deserialized.Title);
        Assert.Equal(record.PublishedAt, deserialized.PublishedAt);
        Assert.Equal(record.Path, deserialized.Path);
        Assert.Single(deserialized.Tags!);
    }

    // ──────────────────────────────────────────────────────────
    //  Subscription models
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SubscriptionRecord_HasCorrectType()
    {
        var sub = new SubscriptionRecord
        {
            Publication = "at://did:plc:abc/site.standard.publication/xyz"
        };
        Assert.Equal("site.standard.graph.subscription", sub.Type);
    }

    [Fact]
    public void SubscriptionRecord_Serializes()
    {
        var sub = new SubscriptionRecord
        {
            Publication = "at://did:plc:abc/site.standard.publication/xyz"
        };

        var json = JsonSerializer.Serialize(sub);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("site.standard.graph.subscription", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal("at://did:plc:abc/site.standard.publication/xyz",
            doc.RootElement.GetProperty("publication").GetString());
    }

    [Fact]
    public void SubscriptionRecord_RoundTrips()
    {
        var sub = new SubscriptionRecord
        {
            Publication = "at://did:plc:abc/site.standard.publication/xyz"
        };

        var json = JsonSerializer.Serialize(sub);
        var deserialized = JsonSerializer.Deserialize<SubscriptionRecord>(json)!;

        Assert.Equal(sub.Publication, deserialized.Publication);
    }

    // ──────────────────────────────────────────────────────────
    //  AtProtoClient integration
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void AtProtoClient_ExposesSiteProperty()
    {
        var client = new AtProtoClientBuilder()
            .WithInstanceUrl("https://pds.example.com")
            .Build();

        Assert.NotNull(client.Site);
    }
}
