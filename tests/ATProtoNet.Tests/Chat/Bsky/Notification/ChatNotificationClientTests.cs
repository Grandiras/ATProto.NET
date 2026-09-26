using ATProtoNet.Http;
using ATProtoNet.Lexicon.Chat.Bsky.Notification;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Notification;

public class ChatNotificationClientTests : IDisposable
{
    private const string Preferences =
        """{"preferences":{"chat":{"include":"all","push":true},"chatRequest":{"include":"follows","push":false}}}""";

    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly ChatNotificationClient _notification;

    public ChatNotificationClientTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        var xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        xrpc.SetTokens("test-token");
        _notification = new ChatNotificationClient(xrpc);
    }

    [Fact]
    public async Task GetPreferencesAsync_GetsWithProxy_UnwrapsThePreferences()
    {
        _stub.On("chat.bsky.notification.getPreferences", Preferences);

        var preferences = await _notification.GetPreferencesAsync();

        var request = Assert.Single(_stub.To("chat.bsky.notification.getPreferences"));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Equal((ChatPreferenceInclude.All, true), (preferences.Chat.Include, preferences.Chat.Push));
        Assert.Equal((ChatPreferenceInclude.Follows, false), (preferences.ChatRequest.Include, preferences.ChatRequest.Push));
    }

    [Fact]
    public async Task PutPreferencesAsync_OnlyOnePreference_SendsOnlyIt()
    {
        _stub.On("chat.bsky.notification.putPreferences", Preferences);

        var preferences = await _notification.PutPreferencesAsync(
            chatRequest: new ChatPreference { Include = ChatPreferenceInclude.Follows, Push = false });

        var request = Assert.Single(_stub.To("chat.bsky.notification.putPreferences"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.Equal("""{"chatRequest":{"include":"follows","push":false}}""", request.BodyText);
        Assert.True(preferences.Chat.Push);
    }

    public void Dispose() => _httpClient.Dispose();
}
