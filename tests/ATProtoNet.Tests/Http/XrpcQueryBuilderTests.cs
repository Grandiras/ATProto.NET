using System.Globalization;
using ATProtoNet.Http;

namespace ATProtoNet.Tests.Http;

public class XrpcQueryBuilderTests
{
    [Fact]
    public void ToQueryParams_Null_ReturnsNull()
    {
        Assert.Null(XrpcQueryBuilder.ToQueryParams(null));
    }

    [Fact]
    public void ToQueryParams_EmptyObject_ReturnsNull()
    {
        Assert.Null(XrpcQueryBuilder.ToQueryParams(new { }));
    }

    [Fact]
    public void ToQueryParams_AnonymousObject_ReturnsPairs()
    {
        var result = XrpcQueryBuilder.ToQueryParams(new { limit = 10, reverse = true });

        Assert.NotNull(result);
        var pairs = result.ToList();
        Assert.Contains(pairs, kv => kv.Key == "limit" && kv.Value == "10");
        Assert.Contains(pairs, kv => kv.Key == "reverse" && kv.Value == "true");
    }

    [Fact]
    public void ToQueryParams_StringDictionary_ReturnsSameInstance()
    {
        var dict = new Dictionary<string, string?> { ["a"] = "b" };
        Assert.Same(dict, XrpcQueryBuilder.ToQueryParams(dict));
    }

    [Fact]
    public void ToQueryParams_Dictionary_ReturnsPairs()
    {
        var dict = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:abc",
            ["collection"] = "com.example.test",
        };

        var pairs = XrpcQueryBuilder.ToQueryParams(dict)!.ToList();

        Assert.Contains(pairs, kv => kv.Key == "repo" && kv.Value == "did:plc:abc");
        Assert.Contains(pairs, kv => kv.Key == "collection" && kv.Value == "com.example.test");
    }

    [Fact]
    public void ToQueryParams_NullValues_AreExcluded()
    {
        var pairs = XrpcQueryBuilder.ToQueryParams(new { key = "value", empty = (string?)null })!.ToList();

        Assert.Contains(pairs, kv => kv.Key == "key");
        Assert.DoesNotContain(pairs, kv => kv.Key == "empty");
    }

    [Fact]
    public void ToQueryParams_EnumerableValue_ExpandsToRepeatedKeys()
    {
        var pairs = XrpcQueryBuilder.ToQueryParams(new { uris = new[] { "a", "b", "c" } })!.ToList();

        var values = pairs.Where(kv => kv.Key == "uris").Select(kv => kv.Value);
        Assert.Equal(new[] { "a", "b", "c" }, values);
    }

    [Fact]
    public void ToQueryParams_EnumerableOfBools_RendersEachLowercase()
    {
        var pairs = XrpcQueryBuilder.ToQueryParams(new { flags = new[] { true, false } })!.ToList();

        Assert.Equal(new[] { "true", "false" }, pairs.Select(kv => kv.Value));
    }

    [Fact]
    public void ToQueryParams_NonIntegralNumber_UsesInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var pairs = XrpcQueryBuilder.ToQueryParams(new { ratio = 1.5 })!.ToList();

            // de-DE would render "1,5", which the server cannot parse.
            Assert.Equal("1.5", Assert.Single(pairs).Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
