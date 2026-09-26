using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ATProtoNet.Server.Tap;
using ATProtoNet.Tap;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server;

/// <summary>
/// The Tap webhook receiver: Tap's Basic admin auth checked in constant time, a bounded body, and
/// the status codes Tap retries on.
/// </summary>
public sealed class TapWebhookTests : IAsyncDisposable
{
    private const string Event =
        """{"id":7,"type":"identity","identity":{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","handle":"atproto.com","is_active":true,"status":"active"}}""";

    private readonly List<TapEvent> _received = [];
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private async Task<HttpClient> StartAsync(Action<WebApplication> map, Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        services?.Invoke(builder.Services);
        _app = builder.Build();
        map(_app);
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private Task<HttpClient> StartAsync(Action<TapWebhookOptions>? configure = null, Func<TapEvent, Task>? handler = null) =>
        StartAsync(app => app.MapTapWebhook("/tap/webhook", (evt, _) =>
        {
            _received.Add(evt);
            return handler?.Invoke(evt) ?? Task.CompletedTask;
        }, configure ?? (o => o.AdminPassword = "secret")));

    private static HttpRequestMessage Post(string? authorization = "Basic YWRtaW46c2VjcmV0", string body = Event)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/tap/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private static string Basic(string credentials) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

    [Fact]
    public async Task Post_AuthenticatedEvent_IsHandledAndAcknowledged()
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(Post());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evt = Assert.IsType<TapIdentityEvent>(Assert.Single(_received));
        Assert.Equal(7, evt.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer secret")]
    [InlineData("Basic not-base64!")]
    [InlineData("Basic YWRtaW4=")] // "admin", no password
    public async Task Post_WithoutCredentials_IsUnauthorized(string? authorization)
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(Post(authorization));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Empty(_received);
    }

    [Theory]
    [InlineData("admin:wrong")]
    [InlineData("admin:secre")]
    [InlineData("admin:secrets")]
    [InlineData("root:secret")]
    [InlineData(":secret")]
    public async Task Post_WrongCredentials_IsUnauthorized(string credentials)
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(Post(Basic(credentials)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task Post_PasswordWithColons_IsAccepted()
    {
        // Tap's Go client sends SetBasicAuth("admin", password); only the first colon separates.
        var client = await StartAsync(o => o.AdminPassword = "s:e:c");

        using var response = await client.SendAsync(Post(Basic("admin:s:e:c")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_DeclaredBodyOverTheLimit_Is413()
    {
        var client = await StartAsync(o =>
        {
            o.AdminPassword = "secret";
            o.MaxBodyBytes = 64;
        });

        using var response = await client.SendAsync(Post());

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task Post_UndeclaredBodyOverTheLimit_Is413()
    {
        var client = await StartAsync(o =>
        {
            o.AdminPassword = "secret";
            o.MaxBodyBytes = 64;
        });
        using var request = Post();
        request.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Event)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"id":1,"type":"commit"}""")]
    public async Task Post_NotATapEvent_IsAcknowledgedAndReported(string body)
    {
        // Refused, Tap would resend it forever and hold back the repository's later events.
        var unreadable = new List<TapUnreadableEvent>();
        var client = await StartAsync(o =>
        {
            o.AdminPassword = "secret";
            o.OnUnreadableEvent = unreadable.Add;
        });

        using var response = await client.SendAsync(Post(body: body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_received);
        var reported = Assert.Single(unreadable);
        Assert.Equal(body, System.Text.Encoding.UTF8.GetString(reported.Body));
        Assert.NotNull(reported.Error);
    }

    [Fact]
    public async Task Post_NotATapEventWithoutAHook_IsAcknowledged()
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(Post(body: """{"id":1,"type":"commit"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task Post_HandlerFails_Is500SoTapRetries()
    {
        var client = await StartAsync(handler: _ => throw new InvalidOperationException("The index is down."));

        using var response = await client.SendAsync(Post());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task MapTapWebhook_NoPassword_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(configure: _ => { }));
    }

    [Fact]
    public async Task MapTapWebhook_AllowUnauthenticated_AcceptsWithoutCredentials()
    {
        var client = await StartAsync(o => o.AllowUnauthenticated = true);

        using var response = await client.SendAsync(Post(authorization: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_received);
    }

    [Fact]
    public async Task MapTapWebhookOfHandler_ResolvesTheHandlerFromServices()
    {
        var handler = new RecordingHandler();
        var client = await StartAsync(
            app => app.MapTapWebhook<RecordingHandler>("/tap/webhook", o => o.AdminPassword = "secret"),
            services => services.AddSingleton(handler));

        using var response = await client.SendAsync(Post());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7, Assert.Single(handler.Received).Id);
    }

    [Fact]
    public void IsAuthorized_AcceptsTheHeaderTheTapClientFormats()
    {
        Assert.True(TapWebhookExtensions.IsAuthorized(TapClient.FormatAdminAuthHeader("secret"), "secret"));
        Assert.True(TapWebhookExtensions.IsAuthorized("basic YWRtaW46c2VjcmV0", "secret"));
        Assert.False(TapWebhookExtensions.IsAuthorized(TapClient.FormatAdminAuthHeader("secret"), "other"));
    }

    private sealed class RecordingHandler : ITapEventHandler
    {
        public List<TapEvent> Received { get; } = [];

        public Task HandleAsync(TapEvent evt, CancellationToken cancellationToken)
        {
            Received.Add(evt);
            return Task.CompletedTask;
        }
    }
}
