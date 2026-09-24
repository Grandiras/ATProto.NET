using System.Globalization;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Http;

public class XrpcParamsTests
{
    [Fact]
    public void Add_NullValues_AreDropped()
    {
        var parameters = new XrpcParams()
            .Add("a", (string?)null)
            .Add("b", (int?)null)
            .Add("c", (bool?)null)
            .Add("d", (long?)null)
            .Add("e", (DateTimeOffset?)null)
            .Add("f", (double?)null);

        Assert.Empty(parameters);
    }

    [Fact]
    public void Add_PreservesCallOrder()
    {
        var parameters = new XrpcParams()
            .Add("actor", "alice.example.com")
            .Add("limit", 25)
            .Add("cursor", "abc");

        Assert.Equal(
            new[] { "actor", "limit", "cursor" },
            parameters.Select(kv => kv.Key));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Add_Bool_RendersLowercase(bool value, string expected)
    {
        var parameters = new XrpcParams().Add("detailed", value);

        Assert.Equal(expected, Assert.Single(parameters).Value);
    }

    [Fact]
    public void Add_Int_UsesInvariantCulture()
    {
        // ar-SA renders digits with Arabic-Indic numerals under some ICU versions.
        using var _ = new CultureScope("ar-SA");

        var parameters = new XrpcParams().Add("limit", 25);

        Assert.Equal("25", Assert.Single(parameters).Value);
    }

    [Fact]
    public void Add_Long_RendersFullValue()
    {
        var parameters = new XrpcParams().Add("cursor", 1_725_000_000_123_456L);

        Assert.Equal("1725000000123456", Assert.Single(parameters).Value);
    }

    [Fact]
    public void Add_DateTimeOffset_RendersIso8601InUtc()
    {
        var parameters = new XrpcParams()
            .Add("since", new DateTimeOffset(2026, 9, 24, 14, 30, 5, 123, TimeSpan.FromHours(2)));

        Assert.Equal("2026-09-24T12:30:05.123Z", Assert.Single(parameters).Value);
    }

    [Fact]
    public void Add_DateTimeOffset_IgnoresTheCurrentCultureAndCalendar()
    {
        // th-TH formats with the Buddhist calendar (year 2569) and fi-FI once used '.' as the
        // time separator; a query parameter must not follow either.
        foreach (var culture in new[] { "th-TH", "fi-FI", "ar-SA" })
        {
            using var _ = new CultureScope(culture);

            var parameters = new XrpcParams()
                .Add("since", new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));

            Assert.Equal("2026-09-24T12:00:00.000Z", Assert.Single(parameters).Value);
        }
    }

    [Fact]
    public void Add_SpanFormattable_UsesInvariantCulture()
    {
        using var _ = new CultureScope("de-DE");

        var parameters = new XrpcParams().Add("ratio", (double?)1.5);

        Assert.Equal("1.5", Assert.Single(parameters).Value);
    }

    [Fact]
    public void Add_Enum_UsesItsJsonName()
    {
        var parameters = new XrpcParams()
            .Add("sort", (SortOrder?)SortOrder.MostRecent)
            .Add("filter", (SortOrder?)SortOrder.Renamed);

        Assert.Equal(new[] { "mostRecent", "top-rated" }, parameters.Select(kv => kv.Value));
    }

    [Fact]
    public void AddAll_EmitsOnePairPerElement()
    {
        var parameters = new XrpcParams().AddAll("uris", ["at://a", "at://b"]);

        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string>("uris", "at://a"),
                new KeyValuePair<string, string>("uris", "at://b"),
            },
            parameters);
    }

    [Fact]
    public void AddAll_NullOrEmpty_ContributesNothing()
    {
        var parameters = new XrpcParams()
            .AddAll("missing", null)
            .AddAll("empty", []);

        Assert.Empty(parameters);
    }

    // ──────────────────────────────────────────────────────────
    //  From: loosely typed parameter objects
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void From_Null_ReturnsNull()
    {
        Assert.Null(XrpcParams.From(null));
    }

    [Fact]
    public void From_EmptyObject_ReturnsNull()
    {
        Assert.Null(XrpcParams.From(new { }));
    }

    [Fact]
    public void From_XrpcParams_ReturnsSameInstance()
    {
        var parameters = new XrpcParams().Add("a", "b");

        Assert.Same(parameters, XrpcParams.From(parameters));
    }

    [Fact]
    public void From_AnonymousObject_ReturnsPairs()
    {
        var pairs = XrpcParams.From(new { limit = 10, reverse = true })!.ToList();

        Assert.Contains(pairs, kv => kv.Key == "limit" && kv.Value == "10");
        Assert.Contains(pairs, kv => kv.Key == "reverse" && kv.Value == "true");
    }

    [Fact]
    public void From_SameAnonymousTypeTwice_ReadsEachInstancesValues()
    {
        // The property list is cached per type; the values must still come from each instance.
        var first = XrpcParams.From(new { cursor = "a" })!.Single().Value;
        var second = XrpcParams.From(new { cursor = "b" })!.Single().Value;

        Assert.Equal(("a", "b"), (first, second));
    }

    [Fact]
    public void From_StringDictionary_KeepsPairsAndDropsNulls()
    {
        var dict = new Dictionary<string, string?> { ["a"] = "b", ["c"] = null };

        var pairs = XrpcParams.From(dict)!.ToList();

        Assert.Equal(new[] { new KeyValuePair<string, string>("a", "b") }, pairs);
    }

    [Fact]
    public void From_Dictionary_ReturnsPairs()
    {
        var dict = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:abc",
            ["collection"] = "com.example.test",
        };

        var pairs = XrpcParams.From(dict)!.ToList();

        Assert.Contains(pairs, kv => kv.Key == "repo" && kv.Value == "did:plc:abc");
        Assert.Contains(pairs, kv => kv.Key == "collection" && kv.Value == "com.example.test");
    }

    [Fact]
    public void From_NullValues_AreExcluded()
    {
        var pairs = XrpcParams.From(new { key = "value", empty = (string?)null })!.ToList();

        Assert.Contains(pairs, kv => kv.Key == "key");
        Assert.DoesNotContain(pairs, kv => kv.Key == "empty");
    }

    [Fact]
    public void From_EnumerableValue_ExpandsToRepeatedKeys()
    {
        var pairs = XrpcParams.From(new { uris = new[] { "a", "b", "c" } })!.ToList();

        var values = pairs.Where(kv => kv.Key == "uris").Select(kv => kv.Value);
        Assert.Equal(new[] { "a", "b", "c" }, values);
    }

    [Fact]
    public void From_EnumerableOfBools_RendersEachLowercase()
    {
        var pairs = XrpcParams.From(new { flags = new[] { true, false } })!.ToList();

        Assert.Equal(new[] { "true", "false" }, pairs.Select(kv => kv.Value));
    }

    [Fact]
    public void From_NonIntegralNumber_UsesInvariantCulture()
    {
        using var _ = new CultureScope("de-DE");

        var pairs = XrpcParams.From(new { ratio = 1.5 })!.ToList();

        // de-DE would render "1,5", which the server cannot parse.
        Assert.Equal("1.5", Assert.Single(pairs).Value);
    }

    [Fact]
    public void From_DateTimeOffset_RendersIso8601InUtc()
    {
        using var _ = new CultureScope("en-US");

        var pairs = XrpcParams.From(new
        {
            since = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero),
        })!.ToList();

        // Not "9/24/2026 12:00:00 PM +00:00", which is what ToString() produces.
        Assert.Equal("2026-09-24T12:00:00.000Z", Assert.Single(pairs).Value);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void From_DateTime_RendersIso8601InUtc(DateTimeKind kind)
    {
        var pairs = XrpcParams.From(new { since = new DateTime(2026, 9, 24, 12, 0, 0, 5, kind) })!.ToList();

        // An unspecified kind is taken as UTC rather than the machine's local zone.
        Assert.Equal("2026-09-24T12:00:00.005Z", Assert.Single(pairs).Value);
    }

    [Fact]
    public void From_LocalDateTime_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local);

        var pairs = XrpcParams.From(new { since = local })!.ToList();

        Assert.Equal(
            local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            Assert.Single(pairs).Value);
    }

    [Fact]
    public void From_Enum_UsesItsJsonName()
    {
        var pairs = XrpcParams.From(new { sort = SortOrder.MostRecent, filter = SortOrder.Renamed })!.ToList();

        // Not "MostRecent" — the name the same enum has in a JSON body.
        Assert.Equal(new[] { "mostRecent", "top-rated" }, pairs.Select(kv => kv.Value));
    }

    [Fact]
    public void From_IdentifierTypes_RenderTheirValue()
    {
        var pairs = XrpcParams.From(new
        {
            repo = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"),
            uri = AtUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3l6ov0"),
            collection = Nsid.Parse("app.bsky.feed.post"),
        })!.ToList();

        Assert.Equal(
            new[]
            {
                "did:plc:ewvi7nxzyoun6zhxrhs64oiz",
                "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3l6ov0",
                "app.bsky.feed.post",
            },
            pairs.Select(kv => kv.Value));
    }

    private enum SortOrder
    {
        MostRecent,

        [JsonStringEnumMemberName("top-rated")]
        Renamed,
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
