using System.Net;
using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Http;

public class XrpcClientRepoRevTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;

    public XrpcClientRepoRevTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
    }

    private static HttpResponseMessage WithRepoRev(string? rev)
    {
        var response = HttpStub.JsonResponse("{}");
        if (rev is not null)
            response.Headers.TryAddWithoutValidation("Atproto-Repo-Rev", rev);
        return response;
    }

    [Fact]
    public async Task QueryAsync_ExtractsRepoRevHeader()
    {
        _stub.On("com.atproto.server.describeServer", _ => WithRepoRev("3jzhpt2dsby2u"));

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task ProcedureAsync_ExtractsRepoRevHeader()
    {
        _stub.On("com.atproto.repo.createRecord", _ => WithRepoRev("3jzhpt2dsby2x"));

        await _xrpc.ProcedureAsync<JsonElement>("com.atproto.repo.createRecord", new { });

        Assert.Equal("3jzhpt2dsby2x", _xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task LatestRepoRev_UpdatesOnSubsequentRequests()
    {
        _stub.On("com.atproto.server.describeServer", _ => WithRepoRev("3jzhpt2dsby2u"));
        _stub.On("com.atproto.server.describeServer", _ => WithRepoRev("3jzhpt2dsby2z"));

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2z", _xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task LatestRepoRev_IsNullWhenNoHeader()
    {
        _stub.On("com.atproto.server.describeServer", _ => WithRepoRev(null));

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");

        Assert.Null(_xrpc.LatestRepoRev);
    }

    [Fact]
    public async Task LatestRepoRev_RetainsPreviousValueWhenHeaderMissing()
    {
        _stub.On("com.atproto.server.describeServer", _ => WithRepoRev("3jzhpt2dsby2u"));
        _stub.On("com.atproto.server.describeServer", _ => WithRepoRev(null));

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);

        await _xrpc.QueryAsync<JsonElement>("com.atproto.server.describeServer");
        Assert.Equal("3jzhpt2dsby2u", _xrpc.LatestRepoRev);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _stub.Dispose();
    }
}
