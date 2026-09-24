using System.Net;
using System.Reflection;
using ATProtoNet.Http;
using ATProtoNet.Server;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// The connection settings behind the HTTP clients the SDK creates or registers.
/// </summary>
public class AtProtoHttpTests
{
    [Fact]
    public void CreateHandler_EnablesDecompressionAndBoundsConnections()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(AtProtoHttp.CreateHandler());

        // Without decompression no Accept-Encoding is sent, and a service answers JSON
        // uncompressed (a 185 KB timeline instead of about 12 KB gzipped).
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
    }

    [Fact]
    public void OwnedClients_ShareOneHandler()
    {
        using var first = new AtProtoClient(new AtProtoClientOptions { AutoRefreshSession = false });
        using var second = new AtProtoClient(new AtProtoClientOptions { AutoRefreshSession = false });

        Assert.Same(AtProtoHttp.SharedHandler, HandlerOf(HttpClientOf(first)));
        Assert.Same(AtProtoHttp.SharedHandler, HandlerOf(HttpClientOf(second)));
    }

    [Fact]
    public async Task DisposingAnOwningClient_LeavesTheSharedHandlerUsable()
    {
        new AtProtoClient(new AtProtoClientOptions { AutoRefreshSession = false }).Dispose();

        using var client = AtProtoHttp.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/");

        // A disposed handler throws ObjectDisposedException; a live one fails to connect.
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
    }

    [Theory]
    [InlineData("AtProtoClient")]
    public void AddAtProtoServer_NamedClientUsesTheSdkHandlerSettings(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtProtoServer();

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);

        var primary = PrimaryOf(handler);
        Assert.Equal(DecompressionMethods.All, primary.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromMinutes(5), primary.PooledConnectionLifetime);
    }

    // ──────────────────────────────────────────────────────────
    //  Base URLs
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://pds.example.com", "https://pds.example.com/")]
    [InlineData("https://pds.example.com/", "https://pds.example.com/")]
    [InlineData("https://pds.example.com:8443", "https://pds.example.com:8443/")]
    [InlineData("https://pds.example.com/base", "https://pds.example.com/base/")]
    [InlineData("https://pds.example.com/base//", "https://pds.example.com/base/")]
    [InlineData("http://localhost:2583", "http://localhost:2583/")]
    public void NormalizeBaseUrl_EndsThePathInASlash(string url, string expected)
    {
        Assert.Equal(expected, AtProtoHttp.NormalizeBaseUrl(url).AbsoluteUri);
        Assert.Equal(expected, AtProtoHttp.NormalizeBaseUrl(new Uri(url)).AbsoluteUri);
        Assert.True(AtProtoHttp.TryNormalizeBaseUrl(url, out var normalized));
        Assert.Equal(expected, normalized.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://pds.example.com/base?x=1")]
    [InlineData("https://pds.example.com/?x=1")]
    [InlineData("https://pds.example.com?x=1")]
    [InlineData("https://pds.example.com/?")]
    [InlineData("https://pds.example.com/base#frag")]
    [InlineData("https://pds.example.com/#")]
    public void NormalizeBaseUrl_WithAQueryOrFragment_Throws(string url)
    {
        // Built from the raw string, "…/base?x=1" became "…/base?x=1/": the query sat in front
        // of every request path.
        Assert.Throws<ArgumentException>(() => AtProtoHttp.NormalizeBaseUrl(url));
        Assert.Throws<ArgumentException>(() => AtProtoHttp.NormalizeBaseUrl(new Uri(url)));
        Assert.False(AtProtoHttp.TryNormalizeBaseUrl(url, out _));
    }

    [Theory]
    [InlineData("/xrpc")]          // a file: URI on Unix, relative on Windows
    [InlineData("pds.example.com")]
    [InlineData("ftp://pds.example.com")]
    [InlineData("wss://pds.example.com")]
    public void NormalizeBaseUrl_WithAnythingButAnAbsoluteHttpUrl_Throws(string url)
    {
        var ex = Record.Exception(() => AtProtoHttp.NormalizeBaseUrl(url));

        Assert.True(ex is ArgumentException or UriFormatException, ex?.GetType().Name ?? "no exception");
        Assert.False(AtProtoHttp.TryNormalizeBaseUrl(url, out _));
    }

    [Fact]
    public void NormalizeBaseUrl_WithARelativeUri_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoHttp.NormalizeBaseUrl(new Uri("xrpc", UriKind.Relative)));
        Assert.False(AtProtoHttp.TryNormalizeBaseUrl(null, out _));
    }

    [Fact]
    public void ValidateServiceUrl_WithAQuery_ReportsTheCallersParameter()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AtProtoHttp.ValidateServiceUrl(new Uri("https://pds.example.com/?x=1"), "serviceUrl"));

        Assert.Equal("serviceUrl", ex.ParamName);
    }

    private static HttpClient HttpClientOf(AtProtoClient client) =>
        (HttpClient)typeof(AtProtoClient)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;

    private static HttpMessageHandler HandlerOf(HttpClient client) =>
        (HttpMessageHandler)typeof(HttpMessageInvoker)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;

    private static SocketsHttpHandler PrimaryOf(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        return Assert.IsType<SocketsHttpHandler>(handler);
    }
}
