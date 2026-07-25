using System.Text;
using ATProtoNet.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientAdminAuthTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientAdminAuthTests()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/")
        };
        _xrpc = new XrpcClient(_httpClient, NullLogger.Instance);
    }

    [Fact]
    public async Task SetAdminCredentials_SendsBasicAuthHeader()
    {
        _xrpc.SetAdminCredentials("hunter2");
        await _xrpc.QueryAsync<object>("com.atproto.admin.getInviteCodes");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:hunter2"));

        Assert.Equal("Basic", _handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal(expected, _handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SetAdminCredentials_WithCustomUser_UsesThatUser()
    {
        _xrpc.SetAdminCredentials("hunter2", "moderator");
        await _xrpc.QueryAsync<object>("com.atproto.admin.getInviteCodes");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("moderator:hunter2"));

        Assert.Equal(expected, _handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SessionToken_TakesPriorityOverAdminCredentials()
    {
        _xrpc.SetAdminCredentials("hunter2");
        _xrpc.SetTokens("session-token");

        await _xrpc.QueryAsync<object>("com.atproto.server.getSession");

        Assert.Equal("Bearer", _handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("session-token", _handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ClearAdminCredentials_RemovesAuthorizationHeader()
    {
        _xrpc.SetAdminCredentials("hunter2");
        _xrpc.ClearAdminCredentials();

        await _xrpc.QueryAsync<object>("com.atproto.server.describeServer");

        Assert.Null(_handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task WithoutAdminCredentials_SendsNoAuthorizationHeader()
    {
        await _xrpc.QueryAsync<object>("com.atproto.server.describeServer");

        Assert.Null(_handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public void HasAdminCredentials_ReflectsCredentialState()
    {
        Assert.False(_xrpc.HasAdminCredentials);

        _xrpc.SetAdminCredentials("hunter2");
        Assert.True(_xrpc.HasAdminCredentials);

        _xrpc.ClearAdminCredentials();
        Assert.False(_xrpc.HasAdminCredentials);
    }

    [Fact]
    public void SetAdminCredentials_WithEmptyPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => _xrpc.SetAdminCredentials(""));
    }

    public void Dispose()
    {
        _xrpc.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
