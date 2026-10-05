using System.Text;
using ATProtoNet.Http;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientAdminAuthTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientAdminAuthTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
    }

    [Fact]
    public async Task SetAdminCredentials_SendsBasicAuthHeader()
    {
        _stub.On("com.atproto.admin.getInviteCodes", "{}");
        _xrpc.SetAdminCredentials("hunter2");
        await _xrpc.QueryAsync<object>("com.atproto.admin.getInviteCodes");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:hunter2"));

        var request = Assert.Single(_stub.Requests);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(expected, request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SetAdminCredentials_WithCustomUser_UsesThatUser()
    {
        _stub.On("com.atproto.admin.getInviteCodes", "{}");
        _xrpc.SetAdminCredentials("hunter2", "moderator");
        await _xrpc.QueryAsync<object>("com.atproto.admin.getInviteCodes");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("moderator:hunter2"));

        Assert.Equal(expected, Assert.Single(_stub.Requests).Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SessionToken_TakesPriorityOverAdminCredentials()
    {
        _stub.On("com.atproto.server.getSession", "{}");
        _xrpc.SetAdminCredentials("hunter2");
        _xrpc.SetTokens("session-token");

        await _xrpc.QueryAsync<object>("com.atproto.server.getSession");

        var request = Assert.Single(_stub.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("session-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task WithoutAdminCredentials_SendsNoAuthorizationHeader()
    {
        _stub.On("com.atproto.server.describeServer", "{}");

        await _xrpc.QueryAsync<object>("com.atproto.server.describeServer");

        Assert.Null(Assert.Single(_stub.Requests).Headers.Authorization);
    }

    [Fact]
    public void SetAdminCredentials_WithEmptyPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => _xrpc.SetAdminCredentials(""));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _stub.Dispose();
        GC.SuppressFinalize(this);
    }
}
