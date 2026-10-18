using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public LexiconResolution ResolveLexicon(string nsid, string didDocumentJson, byte[] getRecordCar)
    {
        var authority = Did.Parse((string)JsonNode.Parse(didDocumentJson)!["id"]!);
        using var http = new HttpClient(new CarHandler(getRecordCar));
        using var resolver = new LexiconResolver(new FakeDidResolver().Publish(didDocumentJson), http);
        try
        {
            var resolved = resolver.ResolveAsync(Nsid.Parse(nsid), authority).GetAwaiter().GetResult();
            return new(true, resolved.Uri.Value, resolved.Cid.Value, resolved.Schema.Id?.Value);
        }
        catch (LexiconResolutionException)
        {
            return new(false);
        }
    }

    /// <inheritdoc />
    public string LexiconDnsName(string nsid) => LexiconResolver.GetDnsName(Nsid.Parse(nsid));

    /// <inheritdoc />
    public bool ValidateLexicon(string lexiconJson) => throw new NotSupportedException("0.8 has no Lexicon schema validation.");

    /// <inheritdoc />
    public bool ValidateRecord(IReadOnlyList<string> lexiconsJson, string rkey, string recordJson) =>
        throw new NotSupportedException("0.8 has no record validation against Lexicon schemas.");

    /// <summary>A PDS answering every <c>getRecord</c> with the vector's proof.</summary>
    private sealed class CarHandler(byte[] car) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/xrpc/com.atproto.sync.getRecord")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new ByteArrayContent(car);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ipld.car");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
