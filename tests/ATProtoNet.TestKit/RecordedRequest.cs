using System.Collections.Specialized;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ATProtoNet.TestKit;

/// <summary>One request as <see cref="FakeXrpcServer"/> saw it.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ContentType, long? ContentLength, byte[] Body, HttpRequestHeaders Headers)
{
    /// <summary>The NSID of an XRPC request, or <see langword="null"/>.</summary>
    public string? Nsid => Uri.AbsolutePath.StartsWith("/xrpc/", StringComparison.Ordinal) ? Uri.AbsolutePath["/xrpc/".Length..] : null;

    /// <summary>The route key without a host: the NSID, or the path.</summary>
    public string Key => Nsid ?? Uri.AbsolutePath;

    /// <summary>The route key with the host: <c>{host}/{nsid}</c> or <c>{host}{path}</c>.</summary>
    public string HostKey => Nsid is { } nsid ? $"{Uri.Host}/{nsid}" : Uri.Host + Uri.AbsolutePath;

    /// <summary>The raw query string, without the leading <c>?</c>.</summary>
    public string Query => Uri.Query.TrimStart('?');

    /// <summary>The query parameters.</summary>
    public NameValueCollection Parameters => HttpUtility.ParseQueryString(Query);

    /// <summary>Every value of a repeated query parameter, in order.</summary>
    public string[] ValuesOf(string key) => Parameters.GetValues(key) ?? [];

    /// <summary>The body as text.</summary>
    public string BodyText => Encoding.UTF8.GetString(Body);

    /// <summary>The body parsed as JSON.</summary>
    public JsonElement JsonBody => JsonDocument.Parse(Body).RootElement;

    /// <summary>The first value of a request header, or <see langword="null"/>.</summary>
    public string? Header(string name) => Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>Asserts the body equals <paramref name="expectedJson"/> as JSON (property order ignored).</summary>
    public void AssertJsonBody(string expectedJson) =>
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(expectedJson).RootElement, JsonBody), $"expected {expectedJson}\nactual   {BodyText}");
}
