using System.Collections.Concurrent;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Net;
using ATProtoNet.TestKit;
using ATProtoNet.Tests.Net;

namespace ATProtoNet.Tests.Identity;

internal static class IdentityFixtures
{
    /// <summary>atproto.com's document, as plc.directory served it on 2026-09-25.</summary>
    public const string AtprotoDotCom =
        """{"@context":["https://www.w3.org/ns/did/v1","https://w3id.org/security/multikey/v1","https://w3id.org/security/suites/secp256k1-2019/v1"],"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","alsoKnownAs":["at://atproto.com"],"verificationMethod":[{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto","type":"Multikey","controller":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","publicKeyMultibase":"zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w"}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://enoki.us-east.host.bsky.network"}]}""";

    public static DidDocument Parse(string json) => DidDocument.Parse(Encoding.UTF8.GetBytes(json));

    /// <summary>A minimal atproto document for <paramref name="did"/>.</summary>
    public static string Document(string did, string handle = "example.com") => FakeIdentityNetwork.DidDocument(did, handle, TestIds.Pds, TestKeys.K256Multibase);
}

/// <summary>A scripted <see cref="IDnsTxtResolver"/> that records the names it is asked for.</summary>
internal sealed class TxtResolver(Func<string, CancellationToken, Task<IReadOnlyList<string>?>> query) : IDnsTxtResolver
{
    private readonly ConcurrentQueue<string> _names = new();

    public IReadOnlyCollection<string> Names => _names;

    /// <summary>The DNS side of <paramref name="network"/>.</summary>
    public static TxtResolver Over(FakeIdentityNetwork network) => new((name, _) => Task.FromResult<IReadOnlyList<string>?>([.. network.Txt(name)]));

    /// <summary>A resolver that never answers until cancelled.</summary>
    public static TxtResolver Hanging() => new((_, ct) => Bodies.Hang<IReadOnlyList<string>?>(ct));

    public Task<IReadOnlyList<string>?> QueryAsync(string name, CancellationToken cancellationToken = default)
    {
        _names.Enqueue(name);
        return query(name, cancellationToken);
    }
}
