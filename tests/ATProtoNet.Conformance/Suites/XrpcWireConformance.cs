using System.Text;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// The golden XRPC fixtures of <c>vectors/xrpc/</c>: for each call the exact request on the wire
/// (method, path, query, body, headers) and how the scripted response is decoded or refused.
/// </summary>
public abstract class XrpcWireConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>Every row of 0.8's <c>EndpointRequestTests</c>.</summary>
    public static TheoryData<string> Endpoints() => Data("endpoints-");

    /// <summary>The shapes 0.8's <c>WireFidelityTests</c> pin: bodies, unwrapped outputs, parameter names.</summary>
    public static TheoryData<string> WireFidelity() => Data("wire-fidelity");

    /// <summary>Procedures without output, answered with an empty 200 body (#69).</summary>
    public static TheoryData<string> VoidProcedures() => Data("void-procedures");

    /// <summary>Non-2xx responses, with and without an error envelope.</summary>
    public static TheoryData<string> Errors() => Data("errors");

    /// <summary>The wire-format regression corpus that is observable on an XRPC call.</summary>
    public static TheoryData<string> Regressions() => Data("regressions");

    private static TheoryData<string> Data(string filePrefix) => [.. XrpcFixture.Ids(filePrefix)];

    /// <summary>A query is a GET with its parameters in the URL; a procedure a POST with a JSON body.</summary>
    [Theory]
    [MemberData(nameof(Endpoints))]
    [Trait("Spec", "XRC-16")]
    [Trait("Spec", "XRC-17")]
    [Trait("Spec", "XRC-18")]
    [Trait("Spec", "XRC-21")]
    public Task Invoke_EndpointFixture_SendsTheGoldenRequestAndDecodesTheResponse(string vectorId) => Run(vectorId);

    /// <summary>The Lexicon's own shapes win over the SDK's convenience.</summary>
    [Theory]
    [MemberData(nameof(WireFidelity))]
    [Trait("Spec", "XRC-17")]
    [Trait("Spec", "XRC-18")]
    public Task Invoke_WireFidelityFixture_SendsAndReadsTheLexiconShapes(string vectorId) => Run(vectorId);

    /// <summary>A procedure that declares no output succeeds on any 2xx body, an empty one included.</summary>
    [Theory]
    [MemberData(nameof(VoidProcedures))]
    [Trait("Spec", "XRC-28")]
    public Task Invoke_VoidProcedureWithEmptyBody_Succeeds(string vectorId) => Run(vectorId);

    /// <summary>A failed call raises the error the body names, else the status's generic name.</summary>
    [Theory]
    [MemberData(nameof(Errors))]
    [Trait("Spec", "XRC-33")]
    public Task Invoke_ErrorResponse_RaisesTheNamedError(string vectorId) => Run(vectorId);

    /// <summary>The bugs fixed in 0.x that show on the wire stay fixed.</summary>
    [Theory]
    [MemberData(nameof(Regressions))]
    [Trait("Spec", "XRC-17")]
    [Trait("Spec", "XRC-18")]
    [Trait("Spec", "XRC-19")]
    [Trait("Spec", "XRC-28")]
    [Trait("Spec", "XRC-52")]
    public Task Invoke_RegressionFixture_KeepsTheFixedBehaviour(string vectorId) => Run(vectorId);

    private async Task Run(string vectorId)
    {
        Vector(vectorId);
        var fixture = XrpcFixture.Get(vectorId);
        var handler = new RecordingHandler(fixture.Response);

        var outcome = await Adapter.InvokeAsync(fixture.ToCall(), handler, TestContext.Current.CancellationToken);

        AssertRequest(fixture, Assert.Single(handler.Requests));
        AssertOutcome(fixture, outcome);
    }

    private static void AssertRequest(XrpcFixture fixture, RecordedRequest request)
    {
        var expected = fixture.Request;
        Assert.Equal(expected.Method, request.Method);
        Assert.Equal(XrpcFixture.ServiceUrl, request.Uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal($"/xrpc/{fixture.Nsid}", request.Uri.AbsolutePath);
        Assert.Equal(
            expected.Query.Select(pair => $"{pair[0]}={pair[1]}"),
            request.Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString));

        if (expected.RawQuery is not null)
            Assert.Equal(expected.RawQuery, request.Uri.Query.TrimStart('?'));

        foreach (var (name, value) in expected.Headers)
            Assert.Equal(value, request.Headers.GetValueOrDefault(name.ToLowerInvariant()));
        if (!expected.Headers.ContainsKey("atproto-proxy"))
            Assert.DoesNotContain("atproto-proxy", request.Headers);

        if (expected.ContentType is not null)
            Assert.Equal(expected.ContentType, request.ContentType);
        if (expected.Method is "GET")
            Assert.Empty(request.Body);
        if (expected.Body is { } json)
            Assert.True(JsonChecks.Equal(json, request.Body), $"expected {json.ToJsonString()}\nactual   {Encoding.UTF8.GetString(request.Body)}");
        if (expected.BodyBase64 is { } base64)
            Assert.Equal(Convert.FromBase64String(base64), request.Body);
        foreach (var path in expected.TypeFirst)
            Assert.True(JsonChecks.WritesTypeOnceAndFirst(request.Body, path), $"'{path}' must write one $type, first: {Encoding.UTF8.GetString(request.Body)}");
    }

    private static void AssertOutcome(XrpcFixture fixture, XrpcOutcome outcome)
    {
        var expect = fixture.Expect;
        if (expect.Error is { } error)
        {
            Assert.NotNull(outcome.Failure);
            Assert.Equal(error.Name, outcome.Failure.Error);
            Assert.Equal(error.Status, outcome.Failure.Status);
            if (error.Message is not null)
                Assert.Equal(error.Message, outcome.Failure.Message);
            return;
        }

        Assert.Null(outcome.Failure);
        if ((expect.Output ?? (expect.OutputIsResponse ? fixture.Response.Body : null)) is { } output)
            Assert.True(JsonChecks.Contains(outcome.Output, output), $"expected {output.ToJsonString()}\nactual   {outcome.Output?.ToJsonString()}");
        if (expect.Binary is { } binary)
        {
            Assert.Equal(binary.ContentType, outcome.BinaryContentType);
            Assert.Equal(binary.Base64 is { } b64 ? Convert.FromBase64String(b64) : Encoding.UTF8.GetBytes(binary.Text!), outcome.Binary);
        }
    }
}
