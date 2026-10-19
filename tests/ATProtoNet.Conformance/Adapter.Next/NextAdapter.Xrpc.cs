using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public Task<XrpcOutcome> InvokeAsync(XrpcFixtureCall call, HttpMessageHandler handler, CancellationToken cancellationToken) => throw Later(5);
}
