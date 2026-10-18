using System.Net;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.TestKit;

/// <summary>
/// The one HTTP stand-in for unit tests: answers each route with the responses scripted for it,
/// in order (the last one repeats), and records every request. A route is the NSID for
/// <c>/xrpc/{nsid}</c> and the path otherwise, optionally prefixed by a host
/// (<c>"plc.directory/did:plc:…"</c>) when one server stands in for several.
/// </summary>
/// <remarks>
/// A request to an unscripted route throws instead of returning a default response, so a call
/// the test did not expect cannot pass as a legitimate 404.
/// </remarks>
public sealed class FakeXrpcServer : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<Responder>> _routes = new(StringComparer.Ordinal);
    private readonly List<RecordedRequest> _requests = [];
    private Responder? _fallback;

    /// <summary>Produces the response to one recorded request.</summary>
    public delegate Task<HttpResponseMessage> Responder(RecordedRequest request, CancellationToken cancellationToken);

    /// <summary>Every request, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <summary>The most recent request.</summary>
    public RecordedRequest Last => Requests[^1];

    /// <summary>The requests to one route.</summary>
    public IEnumerable<RecordedRequest> To(string route) => Requests.Where(r => r.Key == route || r.HostKey == route);

    /// <summary>Forgets the requests recorded so far; scripted routes stay.</summary>
    public void ClearRequests()
    {
        lock (_gate) _requests.Clear();
    }

    /// <summary>Queues a response for a route.</summary>
    public FakeXrpcServer On(string route, Responder respond)
    {
        lock (_gate)
        {
            if (!_routes.TryGetValue(route, out var queue)) _routes[route] = queue = new();
            queue.Enqueue(respond);
        }
        return this;
    }

    /// <summary>Queues a synchronously produced response for a route.</summary>
    public FakeXrpcServer On(string route, Func<RecordedRequest, HttpResponseMessage> respond) =>
        On(route, (request, _) => Task.FromResult(respond(request)));

    /// <summary>Queues a 200 JSON response for a route.</summary>
    public FakeXrpcServer On(string route, string json) => On(route, _ => Responses.Json(json));

    /// <summary>Queues a JSON response with a status for a route.</summary>
    public FakeXrpcServer On(string route, HttpStatusCode status, string json) => On(route, _ => Responses.Json(json, status));

    /// <summary>Answers every unscripted request instead of throwing.</summary>
    public FakeXrpcServer Fallback(Responder respond)
    {
        lock (_gate) _fallback = respond;
        return this;
    }

    /// <summary>An <see cref="HttpClient"/> whose requests all reach this server.</summary>
    public HttpClient CreateHttpClient() => new(this, disposeHandler: false);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Content-Length can change from null to the buffered size once the body is read.
        var contentType = request.Content?.Headers.ContentType?.MediaType;
        var contentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, contentType, contentLength, body, request.Headers);
        var respond = Route(recorded);
        var response = await respond(recorded, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }

    private Responder Route(RecordedRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
            foreach (var key in (string[])[request.HostKey, request.Key])
                if (_routes.TryGetValue(key, out var queue) && queue.Count > 0)
                    return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return _fallback ?? throw new InvalidOperationException(
                $"FakeXrpcServer: nothing scripted for {request.Method} {request.HostKey}. Script it with On(…), " +
                "or fix the code under test if it should not have made this request.");
        }
    }
}

/// <summary>Canned responses for <see cref="FakeXrpcServer"/>.</summary>
public static class Responses
{
    /// <summary>A JSON response.</summary>
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A JSON response serializing <paramref name="body"/> with its property names as declared.</summary>
    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => Json(JsonSerializer.Serialize(body), status);

    /// <summary>A plain-text response.</summary>
    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    /// <summary>A binary response.</summary>
    public static HttpResponseMessage Bytes(byte[] body, string contentType, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new(contentType);
        return response;
    }

    /// <summary>A response with a status and an empty body.</summary>
    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new ByteArrayContent([]) };

    /// <summary>An XRPC error response.</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, string error, string? message = null) => Json(new { error, message }, status);

    /// <summary>A host that accepts the request and never answers until it is cancelled.</summary>
    public static Task<HttpResponseMessage> Never(RecordedRequest request, CancellationToken cancellationToken)
    {
        var never = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => never.TrySetCanceled(cancellationToken));
        return never.Task;
    }
}
