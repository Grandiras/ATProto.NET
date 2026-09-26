using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// An HTTP service stand-in for unit tests: answers each route with the responses scripted for
/// it, in order (the last one repeats), and records every request it saw. A route is keyed by its
/// XRPC method NSID for a request to <c>/xrpc/{nsid}</c>, or by the full path otherwise.
/// </summary>
/// <remarks>
/// A request to a route with nothing scripted for it fails loudly — it throws — rather than
/// returning some default response a test forgot to script; that keeps an unscripted call from
/// being silently misread as a legitimate 404 or 501 from the service.
/// </remarks>
internal sealed class HttpStub : HttpMessageHandler
{
    private readonly Lock _gate = new();

    private readonly Dictionary<string, Queue<Func<RecordedRequest, HttpResponseMessage>>> _routes =
        new(StringComparer.Ordinal);

    private readonly List<RecordedRequest> _requests = [];

    private Func<RecordedRequest, HttpResponseMessage>? _fallback;

    /// <summary>
    /// An artificial delay before every response, applied before the route is even looked up.
    /// For tests of client-side timeouts and cancellation.
    /// </summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Every request, in the order it arrived.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>The requests to one method or path.</summary>
    public IEnumerable<RecordedRequest> To(string nsidOrPath) => Requests.Where(r => r.Key == nsidOrPath);

    /// <summary>
    /// Forgets every request recorded so far — for a test that signs in (or otherwise sets up
    /// state) before the calls it actually wants to assert on. Scripted routes are unaffected.
    /// </summary>
    public void ClearRequests()
    {
        lock (_gate)
            _requests.Clear();
    }

    /// <summary>Queues a response for a method or path.</summary>
    public HttpStub On(string nsidOrPath, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            if (!_routes.TryGetValue(nsidOrPath, out var queue))
                _routes[nsidOrPath] = queue = new Queue<Func<RecordedRequest, HttpResponseMessage>>();
            queue.Enqueue(respond);
        }
        return this;
    }

    /// <summary>Queues a 200 JSON response for a method or path.</summary>
    public HttpStub On(string nsidOrPath, string json) => On(nsidOrPath, _ => JsonResponse(json));

    /// <summary>Queues a response with a given status and JSON body for a method or path.</summary>
    public HttpStub On(string nsidOrPath, HttpStatusCode status, string json) =>
        On(nsidOrPath, _ => JsonResponse(json, status));

    /// <summary>
    /// Answers any request whose route has nothing scripted for it, instead of throwing. For a
    /// test that exercises a cross-cutting concern (headers, retries, timeouts) across many
    /// endpoints without caring which one a given call names.
    /// </summary>
    public HttpStub Fallback(Func<RecordedRequest, HttpResponseMessage> respond)
    {
        lock (_gate)
            _fallback = respond;
        return this;
    }

    /// <summary>Answers any unscripted request with a 200 JSON response.</summary>
    public HttpStub Fallback(string json) => Fallback(_ => JsonResponse(json));

    /// <summary>A response with a JSON body.</summary>
    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>An XRPC error response.</summary>
    public static HttpResponseMessage ErrorResponse(HttpStatusCode status, string error, string? message = null) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { error, message }), Encoding.UTF8, "application/json"),
    };

    /// <summary>A client whose calls all reach this stub.</summary>
    public AtProtoClient CreateClient(string instanceUrl = "https://pds.example.com") =>
        new(
            new AtProtoClientOptions { InstanceUrl = instanceUrl, AutoRefreshSession = false },
            new HttpClient(this, disposeHandler: false),
            null,
            null);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        // Read before buffering the body: HttpContent.Headers.ContentLength can turn from null
        // (a non-seekable stream, sent chunked) into the buffered byte count once read.
        var contentType = request.Content?.Headers.ContentType?.MediaType;
        var contentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri,
            path.StartsWith("/xrpc/", StringComparison.Ordinal) ? path["/xrpc/".Length..] : null,
            contentType,
            contentLength,
            body,
            request.Headers);

        Func<RecordedRequest, HttpResponseMessage> respond;
        lock (_gate)
        {
            _requests.Add(recorded);

            if (!_routes.TryGetValue(recorded.Key, out var queue) || queue.Count == 0)
            {
                if (_fallback is null)
                {
                    throw new InvalidOperationException(
                        $"HttpStub: no script for {request.Method} {recorded.Key}. Script it with On(...), " +
                        "or fix the code under test if it should not have made this request.");
                }

                respond = _fallback;
            }
            else
            {
                respond = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            }
        }

        return respond(recorded);
    }

    /// <summary>One request as the stub saw it.</summary>
    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Nsid,
        string? ContentType,
        long? ContentLength,
        byte[] Body,
        HttpRequestHeaders Headers)
    {
        /// <summary>The path routes are keyed by: the NSID under <c>/xrpc/</c>, or the full path otherwise.</summary>
        public string Key => Nsid ?? Path;

        /// <summary>The request path.</summary>
        public string Path => Uri.AbsolutePath;

        /// <summary>The raw query string, without the leading '?'.</summary>
        public string Query => Uri.Query.TrimStart('?');

        /// <summary>The query parameters.</summary>
        public NameValueCollection Parameters => HttpUtility.ParseQueryString(Query);

        /// <summary>Every value of a repeated query parameter, in order.</summary>
        public string[] ValuesOf(string key) => Parameters.GetValues(key) ?? [];

        /// <summary>The body parsed as JSON.</summary>
        public JsonElement JsonBody => JsonDocument.Parse(Body).RootElement;

        /// <summary>The body as text.</summary>
        public string BodyText => Encoding.UTF8.GetString(Body);

        /// <summary>The <c>Authorization</c> header, if any.</summary>
        public string? Authorization => Headers.Authorization?.ToString();

        /// <summary>The <c>atproto-proxy</c> header, if any.</summary>
        public string? Proxy => HeaderOrDefault("atproto-proxy");

        /// <summary>
        /// The <c>User-Agent</c> header, joined as it goes on the wire (product tokens joined
        /// with spaces) — unlike <see cref="HeaderOrDefault"/>, which would only see the first token.
        /// </summary>
        public string? UserAgent => Headers.UserAgent.Count > 0 ? Headers.UserAgent.ToString() : null;

        /// <summary>The first value of a request header, or <see langword="null"/>.</summary>
        public string? HeaderOrDefault(string name) =>
            Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
