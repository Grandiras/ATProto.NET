using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Http;
using ATProtoNet.Serialization;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    /// <inheritdoc />
    public async Task<XrpcOutcome> InvokeAsync(XrpcFixtureCall call, HttpMessageHandler handler, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(handler, disposeHandler: false);
        using var client = new AtProtoClient(new() { InstanceUrl = call.ServiceUrl, AutoRefreshSession = false }, http);
        if (call.ClientProxy is not null)
            client.SetProxy(call.ClientProxy);
        try
        {
            return await LegacyXrpc.CallAsync(client, call, cancellationToken) switch
            {
                null => new(),
                XrpcStreamResponse stream => await ReadAsync(stream, cancellationToken),
                var result => new(LegacyXrpc.ToLexiconJson(call.Nsid, result)),
            };
        }
        catch (XrpcException ex)
        {
            return new(Failure: new(ex.Error, (int)ex.StatusCode, ex.ErrorMessage));
        }
    }

    private static async Task<XrpcOutcome> ReadAsync(XrpcStreamResponse stream, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            using var content = new MemoryStream();
            await stream.Content.CopyToAsync(content, cancellationToken);
            return new(Binary: content.ToArray(), BinaryContentType: stream.ContentType);
        }
    }
}
