using System.Net;
using ATProtoNet.Net;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Net;

public sealed class DnsOverHttpsTxtResolverTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeXrpcServer _server = new();
    private readonly UntrustedHttp _http;

    public DnsOverHttpsTxtResolverTests() => _http = new(handler: _server);

    public void Dispose() => _http.Dispose();

    private static string Answer(params string[] data) =>
        $$"""{"Status":0,"Answer":[{{string.Join(",", data.Select(d => $$"""{"name":"_atproto.alice.example.com.","type":16,"TTL":300,"data":{{System.Text.Json.JsonSerializer.Serialize(d)}}}"""))}}]}""";

    [Fact]
    [Trait("Spec", "IDN-11")]
    public async Task QueryAsync_DefaultEndpoint_QueriesGoogleWithNameAndType()
    {
        _server.On("dns.google/resolve", Answer("\"did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\""));

        var records = await new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct);

        Assert.Equal(["did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"], records);
        Assert.Equal(new Uri("https://dns.google/resolve?name=_atproto.alice.example.com&type=TXT"), _server.Last.Uri);
        Assert.Equal("application/dns-json", _server.Last.Header("Accept"));
        Assert.Equal(new Uri("https://dns.google/resolve"), DnsOverHttpsTxtResolver.DefaultEndpoint);
    }

    [Fact]
    [Trait("Spec", "IDN-11")]
    public async Task QueryAsync_ConfiguredEndpoint_IsQueriedInsteadOfTheDefault()
    {
        _server.On("cloudflare-dns.com/dns-query", Answer("\"did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\""));

        await new DnsOverHttpsTxtResolver(_http, new Uri("https://cloudflare-dns.com/dns-query")).QueryAsync("_atproto.alice.example.com", Ct);

        Assert.Equal(new Uri("https://cloudflare-dns.com/dns-query?name=_atproto.alice.example.com&type=TXT"), Assert.Single(_server.Requests).Uri);
    }

    [Theory]
    [Trait("Spec", "IDN-11")]
    [InlineData("\"did=did:plc:aaaa\" \"aaaaaaaaaaaaaaaaaaaa\"", "did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("\"did=did:plc:aaaa\"\"aaaaaaaaaaaaaaaaaaaa\"", "did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("\"with \\\"quotes\\\" inside\"", "with \"quotes\" inside")]
    [InlineData("unquoted value", "unquoted value")]
    public async Task QueryAsync_CharacterStrings_AreReassembled(string data, string expected)
    {
        _server.On("dns.google/resolve", Answer(data));

        var records = await new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct);

        Assert.Equal([expected], records);
    }

    [Fact]
    [Trait("Spec", "IDN-11")]
    public async Task QueryAsync_CnameAlongTheWay_IsSkipped()
    {
        _server.On("dns.google/resolve", """
            {"Status":0,"Answer":[
              {"name":"_atproto.alice.example.com.","type":5,"TTL":300,"data":"elsewhere.example.net."},
              {"name":"elsewhere.example.net.","type":16,"TTL":300,"data":"\"did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\""}]}
            """);

        var records = await new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct);

        Assert.Equal(["did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"], records);
    }

    [Fact]
    [Trait("Spec", "IDN-11")]
    public async Task QueryAsync_NoAnswerSection_IsEmpty()
    {
        _server.On("dns.google/resolve", """{"Status":3}""");

        Assert.Empty((await new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct))!);
    }

    [Theory]
    [Trait("Spec", "IDN-11")]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task QueryAsync_HttpError_IsNoAnswer(HttpStatusCode status)
    {
        _server.On("dns.google/resolve", status, "{}");

        Assert.Null(await new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct));
    }

    [Theory]
    [Trait("Spec", "IDN-10")]
    [Trait("Spec", "IDN-11")]
    [InlineData("not json")]
    [InlineData("""{"Answer":[{"type":1.5,"data":"x"}]}""")]
    public async Task QueryAsync_MalformedAnswer_ThrowsMalformed(string body)
    {
        _server.On("dns.google/resolve", body);

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct));

        Assert.Equal(UntrustedFetchError.Malformed, ex.Error);
        Assert.Equal(HttpStatusCode.OK, ex.Status);
    }

    [Fact]
    [Trait("Spec", "IDN-11")]
    public async Task QueryAsync_AnswerOverTheCap_IsRefused()
    {
        _server.On("dns.google/resolve", Answer(new string('x', DnsOverHttpsTxtResolver.MaxResponseBytes)));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => new DnsOverHttpsTxtResolver(_http).QueryAsync("_atproto.alice.example.com", Ct));

        Assert.Equal(UntrustedFetchError.ResponseTooLarge, ex.Error);
    }

    [Theory]
    [Trait("Spec", "IDN-11")]
    [InlineData("http://dns.internal/resolve")]
    [InlineData("https://dns.google/resolve?ct=json")]
    [InlineData("https://dns.google/resolve#x")]
    public void Constructor_EndpointThatIsNotPlainHttps_IsRefused(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new DnsOverHttpsTxtResolver(_http, new Uri(endpoint)));

    [Fact]
    [Trait("Spec", "IDN-11")]
    public void Constructor_EndpointWithAPort_IsRefusedWithoutTheOptOut()
    {
        Assert.Throws<ArgumentException>(() => new DnsOverHttpsTxtResolver(_http, new Uri("https://doh.example.com:8443/resolve")));
        using var development = new UntrustedHttp(new() { AllowPrivateNetworks = true }, handler: _server);
        _ = new DnsOverHttpsTxtResolver(development, new Uri("https://doh.example.com:8443/resolve"));
    }
}
