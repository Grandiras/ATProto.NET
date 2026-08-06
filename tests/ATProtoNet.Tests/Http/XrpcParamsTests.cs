using System.Globalization;
using ATProtoNet.Http;

namespace ATProtoNet.Tests.Http;

public class XrpcParamsTests
{
    [Fact]
    public void Add_NullValues_AreDropped()
    {
        var parameters = new XrpcParams()
            .Add("a", (string?)null)
            .Add("b", (int?)null)
            .Add("c", (bool?)null);

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
        var previous = Thread.CurrentThread.CurrentCulture;
        // ar-SA renders digits with Arabic-Indic numerals under some ICU versions.
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            var parameters = new XrpcParams().Add("limit", 25);

            Assert.Equal("25", Assert.Single(parameters).Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AddAll_EmitsOnePairPerElement()
    {
        var parameters = new XrpcParams().AddAll("uris", ["at://a", "at://b"]);

        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string?>("uris", "at://a"),
                new KeyValuePair<string, string?>("uris", "at://b"),
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
}
