using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ATProtoNet.Tests.Lexicon.App.Bsky;

/// <summary>
/// An XRPC service stand-in: answers each method with the responses scripted for it, in order
/// (the last one repeats), and records every request it saw.
/// </summary>
internal sealed class ScriptedXrpcHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<RecordedRequest, HttpResponseMessage>>> _scripts =
        new(StringComparer.Ordinal);

    /// <summary>Every request, in the order it arrived.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>The requests to one method.</summary>
    public IEnumerable<RecordedRequest> To(string nsid) => Requests.Where(r => r.Nsid == nsid);

    /// <summary>Queues a response for a method.</summary>
    public ScriptedXrpcHandler On(string nsid, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        if (!_scripts.TryGetValue(nsid, out var queue))
            _scripts[nsid] = queue = new Queue<Func<RecordedRequest, HttpResponseMessage>>();
        queue.Enqueue(respond);
        return this;
    }

    /// <summary>Queues a JSON response for a method.</summary>
    public ScriptedXrpcHandler On(string nsid, string json) => On(nsid, _ => JsonResponse(json));

    /// <summary>A 200 response with a JSON body.</summary>
    public static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>An XRPC error response.</summary>
    public static HttpResponseMessage ErrorResponse(HttpStatusCode status, string error, string? message = null) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { error, message }), Encoding.UTF8, "application/json"),
    };

    /// <summary>A client whose calls all reach this handler.</summary>
    public AtProtoClient CreateClient() =>
        new(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            new HttpClient(this, disposeHandler: false),
            null,
            null);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsolutePath["/xrpc/".Length..],
            request.RequestUri.Query.TrimStart('?'),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content?.Headers.ContentLength,
            body,
            request.Headers.TryGetValues("atproto-proxy", out var proxy) ? proxy.Single() : null);
        Requests.Add(recorded);

        if (!_scripts.TryGetValue(recorded.Nsid, out var queue) || queue.Count == 0)
            return ErrorResponse(HttpStatusCode.NotImplemented, "MethodNotImplemented", $"No script for {recorded.Nsid}");

        var respond = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return respond(recorded);
    }

    /// <summary>One request as the handler saw it.</summary>
    internal sealed record RecordedRequest(
        HttpMethod Method,
        string Nsid,
        string Query,
        string? ContentType,
        long? ContentLength,
        byte[] Body,
        string? Proxy)
    {
        /// <summary>The query parameters.</summary>
        public NameValueCollection Parameters => HttpUtility.ParseQueryString(Query);

        /// <summary>Every value of a repeated query parameter, in order.</summary>
        public string[] ValuesOf(string key) => Parameters.GetValues(key) ?? [];

        /// <summary>The body parsed as JSON.</summary>
        public JsonElement JsonBody => JsonDocument.Parse(Body).RootElement;

        /// <summary>The body as text.</summary>
        public string BodyText => Encoding.UTF8.GetString(Body);
    }
}
