using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Tests.Server;

// ── Handlers: each echoes the parameters it was bound with ────

public sealed class BindingParams
{
    [JsonPropertyName("uris")]
    public List<string>? Uris { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("subjects")]
    public IReadOnlyList<AtUri>? Subjects { get; init; }

    [JsonPropertyName("actor")]
    public Did? Actor { get; init; }

    [JsonPropertyName("collection")]
    public Nsid? Collection { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("reverse")]
    public bool? Reverse { get; init; }

    [JsonPropertyName("flags")]
    public List<bool>? Flags { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

public sealed class BindingQuery : IXrpcQuery<BindingParams, BindingParams>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.bind");

    public Task<BindingParams> HandleAsync(BindingParams parameters, HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(parameters);
}

public sealed class RequiredParams
{
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }
}

public sealed class RequiredParamsQuery : IXrpcQuery<RequiredParams, RequiredParams>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.bindRequired");

    public Task<RequiredParams> HandleAsync(RequiredParams parameters, HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(parameters);
}

public sealed record ConstructorParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count);

public sealed class ConstructorParamsQuery : IXrpcQuery<ConstructorParams, ConstructorParams>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.bindConstructor");

    public Task<ConstructorParams> HandleAsync(ConstructorParams parameters, HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(parameters);
}

// ── Tests ─────────────────────────────────────────────────────

public class XrpcQueryBindingTests : IAsyncLifetime
{
    private const string ActorDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Uri1 = "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3l3qo2vuowo2b";
    private const string Uri2 = "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3l3qo2vuowo2c";

    private XrpcTestHost? _host;

    public async ValueTask InitializeAsync()
    {
        _host = await XrpcTestHost.StartAsync(services =>
        {
            services.AddXrpcEndpoint<BindingQuery>();
            services.AddXrpcEndpoint<RequiredParamsQuery>();
            services.AddXrpcEndpoint<ConstructorParamsQuery>();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Fact]
    public async Task Bind_SingleValueForList_BindsOneElementList()
    {
        var bound = await BindAsync($"uris={Uri1}");

        Assert.Equal([Uri1], Strings(bound.GetProperty("uris")));
    }

    [Fact]
    public async Task Bind_SingleValueForArray_BindsOneElementArray()
    {
        var bound = await BindAsync("tags=news");

        Assert.Equal(["news"], Strings(bound.GetProperty("tags")));
    }

    [Fact]
    public async Task Bind_RepeatedKeys_BindsEveryValueInOrder()
    {
        var bound = await BindAsync($"uris={Uri1}&uris={Uri2}");

        Assert.Equal([Uri1, Uri2], Strings(bound.GetProperty("uris")));
    }

    [Fact]
    public async Task Bind_BracketedKeys_AreAcceptedForCollections()
    {
        var bound = await BindAsync($"uris[]={Uri1}&uris[]={Uri2}&tags[]=a");

        Assert.Equal([Uri1, Uri2], Strings(bound.GetProperty("uris")));
        Assert.Equal(["a"], Strings(bound.GetProperty("tags")));
    }

    [Fact]
    public async Task Bind_PlainAndBracketedKeys_AreCombined()
    {
        var bound = await BindAsync($"uris={Uri1}&uris[]={Uri2}");

        Assert.Equal([Uri1, Uri2], Strings(bound.GetProperty("uris")));
    }

    [Fact]
    public async Task Bind_TypedIdentifiers_BindThroughTheirParsers()
    {
        var bound = await BindAsync($"actor={ActorDid}&collection=app.bsky.feed.post&subjects={Uri1}");

        Assert.Equal(ActorDid, bound.GetProperty("actor").GetString());
        Assert.Equal("app.bsky.feed.post", bound.GetProperty("collection").GetString());
        Assert.Equal([Uri1], Strings(bound.GetProperty("subjects")));
    }

    [Fact]
    public async Task Bind_InvalidDid_ReturnsInvalidRequestNamingTheParameter()
    {
        var (error, message) = await BindErrorAsync("actor=not-a-did");

        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Equal("Invalid value for query parameter 'actor'.", message);
    }

    [Fact]
    public async Task Bind_InvalidNsid_ReturnsInvalidRequest()
    {
        var (error, message) = await BindErrorAsync("collection=nodots");

        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("'collection'", message);
    }

    [Fact]
    public async Task Bind_InvalidElementInIdentifierList_ReturnsInvalidRequest()
    {
        var (error, message) = await BindErrorAsync($"subjects={Uri1}&subjects=https://example.com");

        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("'subjects'", message);
    }

    [Fact]
    public async Task Bind_Integer_BindsFromText()
    {
        var bound = await BindAsync("limit=25");

        Assert.Equal(25, bound.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task Bind_InvalidInteger_ReturnsInvalidRequestWithoutTypeNames()
    {
        var (error, message) = await BindErrorAsync("limit=abc");

        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Equal("Invalid value for query parameter 'limit'.", message);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Bind_Boolean_BindsWithoutAConverter(string text, bool expected)
    {
        var bound = await BindAsync($"reverse={text}");

        Assert.Equal(expected, bound.GetProperty("reverse").GetBoolean());
    }

    [Fact]
    public async Task Bind_BooleanList_BindsEachValue()
    {
        var bound = await BindAsync("flags=true&flags=false");

        Assert.Equal([true, false], bound.GetProperty("flags").EnumerateArray().Select(e => e.GetBoolean()));
    }

    [Fact]
    public async Task Bind_InvalidBoolean_ReturnsInvalidRequest()
    {
        var (error, message) = await BindErrorAsync("reverse=yes");

        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("'reverse'", message);
    }

    [Fact]
    public async Task Bind_RepeatedScalar_ReturnsInvalidRequest()
    {
        var (error, message) = await BindErrorAsync("limit=1&limit=2");

        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Contains("'limit'", message);
    }

    [Fact]
    public async Task Bind_BracketedKeyForScalar_IsIgnored()
    {
        var bound = await BindAsync("cursor[]=abc");

        Assert.False(bound.TryGetProperty("cursor", out _));
    }

    [Fact]
    public async Task Bind_UnknownParameters_AreIgnored()
    {
        var bound = await BindAsync("cursor=abc&somethingElse=1");

        Assert.Equal("abc", bound.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task Bind_ValueNeedingJsonEscaping_RoundTripsVerbatim()
    {
        const string cursor = "a\"b\\cé\n}";

        var bound = await BindAsync($"cursor={Uri.EscapeDataString(cursor)}");

        Assert.Equal(cursor, bound.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task Bind_MissingRequiredParameter_ReturnsInvalidRequestNamingIt()
    {
        var response = await _host!.Client.GetAsync("/xrpc/com.example.bindRequired");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(XrpcErrors.InvalidRequest, error);
        Assert.Equal("Missing required query parameter 'actor'.", message);
    }

    [Fact]
    public async Task Bind_ConstructorBoundType_Binds()
    {
        var response = await _host!.Client.GetAsync("/xrpc/com.example.bindConstructor?name=x&count=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("x", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Bind_ManyRequests_ReuseNoStateBetweenThem()
    {
        var tasks = Enumerable.Range(0, 64).Select(async i =>
        {
            var bound = await BindAsync($"limit={i}&tags={i}&tags={i + 1}");
            Assert.Equal(i, bound.GetProperty("limit").GetInt32());
            Assert.Equal([$"{i}", $"{i + 1}"], Strings(bound.GetProperty("tags")));
        });

        await Task.WhenAll(tasks);
    }

    private async Task<JsonElement> BindAsync(string query)
    {
        var response = await _host!.Client.GetAsync($"/xrpc/com.example.bind?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<(string Error, string Message)> BindErrorAsync(string query)
    {
        var response = await _host!.Client.GetAsync($"/xrpc/com.example.bind?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await XrpcTestHost.ReadErrorAsync(response);
    }

    private static string[] Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(e => e.GetString()!)];
}
