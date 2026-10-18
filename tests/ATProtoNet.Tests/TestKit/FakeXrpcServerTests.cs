using System.Net;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.TestKit;

public class FakeXrpcServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_UnscriptedRoute_Throws()
    {
        using var http = new FakeXrpcServer().CreateHttpClient();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync("https://pds.example.com/xrpc/com.atproto.server.describeServer", Ct));
        Assert.Contains("com.atproto.server.describeServer", error.Message);
    }

    [Fact]
    public async Task SendAsync_QueuedResponses_AnswersInOrderAndRepeatsTheLast()
    {
        var server = new FakeXrpcServer().On("app.example.get", "{\"n\":1}").On("app.example.get", "{\"n\":2}");
        using var http = server.CreateHttpClient();
        var bodies = new List<string>();
        for (var i = 0; i < 3; i++)
            bodies.Add(await http.GetStringAsync("https://pds.example.com/xrpc/app.example.get", Ct));
        Assert.Equal(["{\"n\":1}", "{\"n\":2}", "{\"n\":2}"], bodies);
    }

    [Fact]
    public async Task SendAsync_HostRoute_WinsOverPathRoute()
    {
        var server = new FakeXrpcServer().On("/doc", "{\"any\":true}").On("a.example/doc", "{\"a\":true}");
        using var http = server.CreateHttpClient();
        Assert.Equal("{\"a\":true}", await http.GetStringAsync("https://a.example/doc", Ct));
        Assert.Equal("{\"any\":true}", await http.GetStringAsync("https://b.example/doc", Ct));
    }

    [Fact]
    public async Task SendAsync_Post_RecordsMethodQueryAndBody()
    {
        var server = new FakeXrpcServer().On("app.example.put", _ => Responses.Status(HttpStatusCode.OK));
        using var http = server.CreateHttpClient();
        await http.PostAsync("https://pds.example.com/xrpc/app.example.put?a=1&a=2", new StringContent("{\"x\":1}"), Ct);
        var request = Assert.Single(server.To("app.example.put"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(["1", "2"], request.ValuesOf("a"));
        request.AssertJsonBody("{ \"x\": 1 }");
    }

    [Fact]
    public async Task Never_Cancelled_Cancels()
    {
        var server = new FakeXrpcServer().On("app.example.hang", Responses.Never);
        using var http = server.CreateHttpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var call = http.GetAsync("https://pds.example.com/xrpc/app.example.hang", cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }
}
