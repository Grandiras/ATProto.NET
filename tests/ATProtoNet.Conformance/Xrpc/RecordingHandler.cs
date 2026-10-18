using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ATProtoNet.Conformance;

/// <summary>The transport under test: records every request and answers each with a fixture's scripted response.</summary>
public sealed class RecordingHandler(XrpcFixture.ScriptedResponse response) : HttpMessageHandler
{
    /// <summary>Every request received, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content;
        var headers = request.Headers.Concat(content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(", ", h.Value));
        var body = content is null ? [] : await content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new(request.Method.Method, request.RequestUri!, content?.Headers.ContentType?.MediaType, headers, body));

        var reply = new HttpResponseMessage((HttpStatusCode)response.Status) { Content = new ByteArrayContent(ReplyBody()) };
        reply.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(response.ContentType);
        foreach (var (name, value) in response.Headers)
            reply.Headers.TryAddWithoutValidation(name, value);
        return reply;
    }

    private byte[] ReplyBody() =>
        response.Body is { } json ? Encoding.UTF8.GetBytes(json.ToJsonString())
        : response.Text is { } text ? Encoding.UTF8.GetBytes(text)
        : response.Base64 is { } base64 ? Convert.FromBase64String(base64)
        : [];
}

/// <summary>A request as it reached the wire.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The absolute request URI.</param>
/// <param name="ContentType">The body's media type.</param>
/// <param name="Headers">Request and content headers, names lower-cased, values joined with ", ".</param>
/// <param name="Body">The body bytes.</param>
public sealed record RecordedRequest(string Method, Uri Uri, string? ContentType, IReadOnlyDictionary<string, string> Headers, byte[] Body);
