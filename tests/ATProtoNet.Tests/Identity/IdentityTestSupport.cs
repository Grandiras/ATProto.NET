using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>Answers each request with a scripted response, and records what was asked.</summary>
public sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private readonly ConcurrentQueue<Uri> _requests = new();

    public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    /// <summary>Every request URI, in the order the requests were sent.</summary>
    public IReadOnlyList<Uri> Requests => [.. _requests];

    /// <summary>How many requests reached the handler: the network calls a real client would have made.</summary>
    public int Count => _requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request.RequestUri!);
        var response = await respond(request, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("") };

    /// <summary>A DNS-over-HTTPS JSON answer carrying TXT records, each already in presentation form.</summary>
    public static HttpResponseMessage TxtAnswer(params string[] records) =>
        Json("{\"Status\":0,\"Answer\":[" +
             string.Join(',', records.Select(r => $"{{\"type\":16,\"data\":{System.Text.Json.JsonSerializer.Serialize(r)}}}")) +
             "]}");

    /// <summary>A host that accepts the request and never answers, until the request is cancelled.</summary>
    public static async Task<HttpResponseMessage> Never(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }
}

/// <summary>
/// A raw HTTP/1.1 server on a loopback port, answering every connection with a canned response,
/// for exercising the real socket handler: truncated bodies, broken encodings, redirects.
/// </summary>
public sealed class LoopbackServer : IDisposable
{
    private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _connections;

    public LoopbackServer(Func<int, byte[]> respond)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync(respond);
    }

    public LoopbackServer(Func<int, string> respond)
        : this(port => Encoding.Latin1.GetBytes(respond(port)))
    {
    }

    public int Port { get; }

    public int Connections => Volatile.Read(ref _connections);

    private async Task AcceptAsync(Func<int, byte[]> respond)
    {
        while (true)
        {
            System.Net.Sockets.TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }

            Interlocked.Increment(ref _connections);
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var buffer = new byte[8192];
                    try
                    {
                        // The request headers: a GET carries nothing after them.
                        var read = 0;
                        while (read < buffer.Length && buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8) < 0)
                        {
                            var n = await stream.ReadAsync(buffer.AsMemory(read));
                            if (n == 0)
                                return;
                            read += n;
                        }

                        await stream.WriteAsync(respond(Port));
                        await stream.FlushAsync();
                        await Task.Delay(50);
                    }
                    catch (Exception)
                    {
                        // The client gave up; nothing to answer.
                    }
                }
            });
        }
    }

    public void Dispose() => _listener.Stop();
}

/// <summary>A clock that moves only when told to.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public ManualClock()
        : this(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>DID documents as a directory would serve them.</summary>
public static class DidDocs
{
    /// <summary>atproto.com's document, as plc.directory served it on 2026-09-25.</summary>
    public const string AtprotoDotCom =
        """{"@context":["https://www.w3.org/ns/did/v1","https://w3id.org/security/multikey/v1","https://w3id.org/security/suites/secp256k1-2019/v1"],"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","alsoKnownAs":["at://atproto.com"],"verificationMethod":[{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto","type":"Multikey","controller":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","publicKeyMultibase":"zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w"}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://enoki.us-east.host.bsky.network"}]}""";

    /// <summary>A minimal atproto document.</summary>
    public static string Json(string did, string? handle = null, string? pds = "https://pds.example.com", string? signingKey = null)
    {
        var aka = handle is null ? "[]" : $"[\"at://{handle}\"]";
        var methods = signingKey is null
            ? "[]"
            : $$"""[{"id":"{{did}}#atproto","type":"Multikey","controller":"{{did}}","publicKeyMultibase":"{{signingKey["did:key:".Length..]}}"}]""";
        var services = pds is null
            ? "[]"
            : $$"""[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{pds}}"}]""";
        return $$"""{"id":"{{did}}","alsoKnownAs":{{aka}},"verificationMethod":{{methods}},"service":{{services}}}""";
    }

    /// <summary>A minimal atproto document, parsed.</summary>
    public static DidDocument Parse(string did, string? handle = null, string? pds = "https://pds.example.com", string? signingKey = null) =>
        System.Text.Json.JsonSerializer.Deserialize<DidDocument>(
            Json(did, handle, pds, signingKey), ATProtoNet.Serialization.AtProtoJsonDefaults.Options)!;
}
