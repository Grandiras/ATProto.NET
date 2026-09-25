using System.Net;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Tests.Ozone;

/// <summary>
/// An <see cref="AtProtoClient"/> over a handler that answers each request with the next scripted
/// JSON body and records what was sent.
/// </summary>
internal sealed class OzoneTestClient : IDisposable
{
    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _httpClient;

    public OzoneTestClient()
    {
        _httpClient = new HttpClient(_handler);
        Client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://ozone.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public AtProtoClient Client { get; }

    public IReadOnlyList<SentRequest> Requests => _handler.Requests;

    /// <summary>The one request sent so far.</summary>
    public SentRequest Sent => Assert.Single(_handler.Requests);

    /// <summary>Queues the body of the next response.</summary>
    public void Respond(string json) => _handler.Responses.Enqueue(json);

    public void Dispose()
    {
        Client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Queue<string> Responses { get; } = new();

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Responses.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }
}

/// <summary>One request an <see cref="OzoneTestClient"/> sent.</summary>
internal sealed record SentRequest(HttpMethod Method, Uri Uri, string? Body)
{
    /// <summary>The method NSID from the path.</summary>
    public string Nsid => Uri.AbsolutePath["/xrpc/".Length..];

    /// <summary>The unescaped query string, with its leading <c>?</c>; empty when there is none.</summary>
    public string Query => Uri.UnescapeDataString(Uri.Query);

    /// <summary>The JSON body.</summary>
    public JsonElement Json => JsonSerializer.Deserialize<JsonElement>(Body ?? throw new InvalidOperationException("No body was sent."));

    /// <summary>Asserts a query (GET, no body) of <paramref name="nsid"/>.</summary>
    public SentRequest IsQuery(string nsid)
    {
        Assert.Equal(HttpMethod.Get, Method);
        Assert.Equal(nsid, Nsid);
        Assert.Null(Body);
        return this;
    }

    /// <summary>Asserts a procedure (POST) of <paramref name="nsid"/>.</summary>
    public SentRequest IsProcedure(string nsid)
    {
        Assert.Equal(HttpMethod.Post, Method);
        Assert.Equal(nsid, Nsid);
        return this;
    }
}
