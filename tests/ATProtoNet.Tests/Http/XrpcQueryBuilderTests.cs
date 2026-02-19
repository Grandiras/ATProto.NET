using ATProtoNet.Http;

namespace ATProtoNet.Tests.Http;

public class XrpcQueryBuilderTests
{
    [Fact]
    public void BuildQueryString_Null_ReturnsEmpty()
    {
        Assert.Equal("", XrpcQueryBuilder.BuildQueryString(null));
    }

    [Fact]
    public void BuildQueryString_AnonymousObject_BuildsQueryString()
    {
        var result = XrpcQueryBuilder.BuildQueryString(new { limit = 25, cursor = "abc" });
        Assert.Contains("limit=25", result);
        Assert.Contains("cursor=abc", result);
        Assert.StartsWith("?", result);
    }

    [Fact]
    public void BuildQueryString_Dictionary_BuildsQueryString()
    {
        var dict = new Dictionary<string, string?> { ["repo"] = "did:plc:abc", ["collection"] = "com.example.test" };
        var result = XrpcQueryBuilder.BuildQueryString(dict);
        Assert.Contains("repo=did%3Aplc%3Aabc", result);
        Assert.Contains("collection=com.example.test", result);
    }

    [Fact]
    public void BuildQueryString_BoolValues_AreLowercase()
    {
        var result = XrpcQueryBuilder.BuildQueryString(new { reverse = true });
        Assert.Contains("reverse=true", result);
    }

    [Fact]
    public void BuildQueryString_NullValues_AreExcluded()
    {
        var dict = new Dictionary<string, string?> { ["key"] = "value", ["empty"] = null };
        var result = XrpcQueryBuilder.BuildQueryString(dict);
        Assert.Contains("key=value", result);
        Assert.DoesNotContain("empty", result);
    }

    [Fact]
    public void BuildQueryString_EmptyObject_ReturnsEmpty()
    {
        Assert.Equal("", XrpcQueryBuilder.BuildQueryString(new { }));
    }

    [Fact]
    public void ToDictionary_Null_ReturnsNull()
    {
        Assert.Null(XrpcQueryBuilder.ToDictionary(null));
    }

    [Fact]
    public void ToDictionary_AnonymousObject_ReturnsDictionary()
    {
        var result = XrpcQueryBuilder.ToDictionary(new { limit = 10, reverse = true });
        Assert.NotNull(result);
        Assert.Equal("10", result["limit"]);
        Assert.Equal("true", result["reverse"]);
    }

    [Fact]
    public void ToDictionary_StringDictionary_ReturnsSameInstance()
    {
        var dict = new Dictionary<string, string?> { ["a"] = "b" };
        var result = XrpcQueryBuilder.ToDictionary(dict);
        Assert.Same(dict, result);
    }
}
