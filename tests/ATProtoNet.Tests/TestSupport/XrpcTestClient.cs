using System.Net.Http;
using ATProtoNet.Auth;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// An <see cref="AtProtoClient"/> wired to a fresh <see cref="HttpStub"/>: the default fixture for
/// a test that calls a Lexicon client and asserts on the request it sent, or the result it
/// produced from a scripted response.
/// </summary>
internal sealed class XrpcTestClient : IDisposable
{
    /// <summary>The stub every call from <see cref="Client"/> reaches.</summary>
    public HttpStub Stub { get; } = new();

    /// <summary>The <see cref="HttpClient"/> the stub answers.</summary>
    public HttpClient Http { get; }

    /// <summary>The client under test.</summary>
    public AtProtoClient Client { get; }

    /// <summary>
    /// Builds the fixture. <paramref name="configure"/> can change any option — auth, proxy,
    /// rate limiting — before <see cref="Client"/> is constructed.
    /// </summary>
    public XrpcTestClient(
        string instanceUrl = "https://pds.example.com",
        Action<AtProtoClientOptions>? configure = null,
        IAtProtoSessionStore? sessionStore = null)
    {
        Http = new HttpClient(Stub, disposeHandler: false);

        var options = new AtProtoClientOptions { InstanceUrl = instanceUrl, AutoRefreshSession = false };
        configure?.Invoke(options);

        Client = new AtProtoClient(options, Http, sessionStore, null);
    }

    /// <summary>Queues a response for a method or path. See <see cref="HttpStub.On(string, Func{HttpStub.RecordedRequest, HttpResponseMessage})"/>.</summary>
    public XrpcTestClient On(string nsidOrPath, Func<HttpStub.RecordedRequest, HttpResponseMessage> respond)
    {
        Stub.On(nsidOrPath, respond);
        return this;
    }

    /// <summary>Queues a 200 JSON response for a method or path.</summary>
    public XrpcTestClient On(string nsidOrPath, string json)
    {
        Stub.On(nsidOrPath, json);
        return this;
    }

    /// <summary>Queues a response with a given status and JSON body for a method or path.</summary>
    public XrpcTestClient On(string nsidOrPath, System.Net.HttpStatusCode status, string json)
    {
        Stub.On(nsidOrPath, status, json);
        return this;
    }

    /// <summary>Answers any unscripted request with a 200 JSON response. See <see cref="HttpStub.Fallback(string)"/>.</summary>
    public XrpcTestClient Fallback(string json)
    {
        Stub.Fallback(json);
        return this;
    }

    /// <summary>Answers any unscripted request. See <see cref="HttpStub.Fallback(Func{HttpStub.RecordedRequest, HttpResponseMessage})"/>.</summary>
    public XrpcTestClient Fallback(Func<HttpStub.RecordedRequest, HttpResponseMessage> respond)
    {
        Stub.Fallback(respond);
        return this;
    }

    /// <summary>Forgets every request recorded so far. See <see cref="HttpStub.ClearRequests"/>.</summary>
    public void ClearRequests() => Stub.ClearRequests();

    /// <summary>Every request the stub saw, in order.</summary>
    public IReadOnlyList<HttpStub.RecordedRequest> Requests => Stub.Requests;

    /// <summary>The requests to one method or path.</summary>
    public IEnumerable<HttpStub.RecordedRequest> To(string nsidOrPath) => Stub.To(nsidOrPath);

    /// <summary>The most recent request. See <see cref="HttpStub.Last"/>.</summary>
    public HttpStub.RecordedRequest Last => Stub.Last;

    /// <summary>See <see cref="HttpStub.AssertCall"/>.</summary>
    public HttpStub.RecordedRequest AssertCall(string nsidOrPath, HttpMethod method, string? proxy = null) =>
        Stub.AssertCall(nsidOrPath, method, proxy);

    /// <summary>See <see cref="HttpStub.AssertGet"/>.</summary>
    public HttpStub.RecordedRequest AssertGet(string nsidOrPath, string expectedQuery = "", string? proxy = null) =>
        Stub.AssertGet(nsidOrPath, expectedQuery, proxy);

    /// <summary>See <see cref="HttpStub.AssertPost"/>.</summary>
    public HttpStub.RecordedRequest AssertPost(string nsidOrPath, string? expectedBody = null, string? proxy = null) =>
        Stub.AssertPost(nsidOrPath, expectedBody, proxy);

    public void Dispose()
    {
        Client.Dispose();
        Http.Dispose();
        Stub.Dispose();
    }
}
