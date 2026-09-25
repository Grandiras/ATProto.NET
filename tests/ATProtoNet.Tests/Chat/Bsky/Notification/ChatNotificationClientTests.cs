using ATProtoNet.Http;
using ATProtoNet.Lexicon.Chat.Bsky.Notification;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Notification;

public class ChatNotificationClientTests : IDisposable
{
    private const string Preferences =
        """{"preferences":{"chat":{"include":"all","push":true},"chatRequest":{"include":"follows","push":false}}}""";

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly ChatNotificationClient _notification;

    public ChatNotificationClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pds.example.com/") };
        var xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        xrpc.SetTokens("test-token");
        _notification = new ChatNotificationClient(xrpc);
    }

    [Fact]
    public async Task GetPreferencesAsync_GetsWithProxy_UnwrapsThePreferences()
    {
        HttpRequestMessage? captured = null;
        _handler.ResponseFactory = request =>
        {
            captured = request;
            return Json(Preferences);
        };

        var preferences = await _notification.GetPreferencesAsync();

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/xrpc/chat.bsky.notification.getPreferences", captured.RequestUri!.PathAndQuery);
        Assert.Equal(ServiceProxy.BskyChatHeader, captured.Headers.GetValues("atproto-proxy").Single());
        Assert.Equal((ChatPreferenceInclude.All, true), (preferences.Chat.Include, preferences.Chat.Push));
        Assert.Equal((ChatPreferenceInclude.Follows, false), (preferences.ChatRequest.Include, preferences.ChatRequest.Push));
    }

    [Fact]
    public async Task PutPreferencesAsync_OnlyOnePreference_SendsOnlyIt()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        _handler.ResponseFactory = request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(Preferences);
        };

        var preferences = await _notification.PutPreferencesAsync(
            chatRequest: new ChatPreference { Include = ChatPreferenceInclude.Follows, Push = false });

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/xrpc/chat.bsky.notification.putPreferences", captured.RequestUri!.PathAndQuery);
        Assert.Equal(ServiceProxy.BskyChatHeader, captured.Headers.GetValues("atproto-proxy").Single());
        Assert.Equal("""{"chatRequest":{"include":"follows","push":false}}""", body);
        Assert.True(preferences.Chat.Push);
    }

    private static HttpResponseMessage Json(string json) => new()
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
            _ => new HttpResponseMessage { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(ResponseFactory(request));
    }
}
