using System.Net;
using System.Text;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

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
