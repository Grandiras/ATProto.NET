using System.Text.RegularExpressions;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Labeler;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Labeler;

public class LabelerClientTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly LabelerClient _labeler;

    public LabelerClientTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _labeler = new LabelerClient(_xrpc);
    }

    [Fact]
    public async Task GetServices_SendsCorrectRequest()
    {
        _stub.On("app.bsky.labeler.getServices", """{"views":[]}""");

        var result = await _labeler.GetServicesAsync(
            [Did.Parse("did:plc:labeler1"), Did.Parse("did:plc:labeler2")],
            detailed: true);

        var request = Assert.Single(_stub.Requests);
        Assert.Contains("detailed=true", request.Query);
        Assert.NotNull(result);
        Assert.Empty(result.Views);

        // XRPC arrays travel as repeated keys, never as one comma-joined value.
        Assert.Equal(2, Regex.Matches(request.Query, "dids=", RegexOptions.IgnoreCase).Count);
        Assert.Contains("dids=did%3aplc%3alabeler1", request.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dids=did%3aplc%3alabeler2", request.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%2C", request.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetServices_WithoutDetailed_OmitsParam()
    {
        _stub.On("app.bsky.labeler.getServices", """{"views":[]}""");

        await _labeler.GetServicesAsync([Did.Parse("did:plc:labeler1")]);

        Assert.DoesNotContain("detailed", Assert.Single(_stub.Requests).Query);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _stub.Dispose();
    }
}
