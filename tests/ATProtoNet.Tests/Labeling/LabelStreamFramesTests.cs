using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Models;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Server.Spaces;
using ATProtoNet.Tests.Streaming;

namespace ATProtoNet.Tests.Labeling;

/// <summary>
/// The labeler side of <c>subscribeLabels</c>, pinned against <c>@atproto/common</c>'s
/// <c>cborEncode</c> of the same header and body, and read back by this SDK's own consumer.
/// </summary>
public sealed class LabelStreamFramesTests
{
    private const string Labeler = "wss://labeler.test";

    // cborEncode({ t: '#labels', op: 1 }) followed by cborEncode({ seq: 42, labels: [indigo's label] }).
    private const string LabelsFrame =
        "a2617467236c6162656c73626f7001" +
        "a263736571182a666c6162656c7381a6636374737818323032342d31302d32335431373a35313a31392e3132385a637369675840b8244d" +
        "039993ce1d3bf13e7166d92f2c4b7f3becf482c28cddaa9123f955001f19b5b327ce7c12fc9c07a29644e3426134d0e3f34cfc82c0cb02" +
        "9fe15aa1b93e6373726378206469643a706c633a6e3374696d766f6962356e617537677677643663736861706375726978206469643a70" +
        "6c633a343479626172643636767634347a6b736a6532356f37647a6376616c6b626c61646572756e6e65726376657201";

    private const string InfoFrame =
        "a261746523696e666f626f7001" + "a2646e616d656e4f75746461746564437572736f72676d65737361676567746f6f206f6c64";

    private const string ErrorFrame =
        "a1626f7020" +
        "a2656572726f726c467574757265437572736f72676d65737361676575437572736f7220696e20746865206675747572652e";

    private const string IndigoLabel = """
        {"ver":1,"src":"did:plc:n3timvoib5nau7gvwd6cshap","uri":"did:plc:44ybard66vv44zksje25o7dz","val":"bladerunner","cts":"2024-10-23T17:51:19.128Z","sig":{"$bytes":"uCRNA5mTzh078T5xZtkvLEt/O+z0gsKM3aqRI/lVAB8ZtbMnznwS/JwHopZE40JhNNDj80z8gsDLAp/hWqG5Pg"}}
        """;

    [Fact]
    public void Encode_Labels_MatchesTheReferenceFrame()
    {
        var frame = LabelStreamFrames.Encode(new LabelsEvent
        {
            Seq = 42,
            Labels = [LabelSigningInteropTests.ParseLabel(IndigoLabel)],
        });

        Assert.Equal(LabelsFrame, Convert.ToHexStringLower(frame));
    }

    [Fact]
    public void Encode_Info_MatchesTheReferenceFrame()
        => Assert.Equal(
            InfoFrame,
            Convert.ToHexStringLower(LabelStreamFrames.Encode(new LabelInfoEvent { Name = "OutdatedCursor", Message = "too old" })));

    [Fact]
    public void EncodeError_MatchesTheReferenceFrame()
        => Assert.Equal(
            ErrorFrame,
            Convert.ToHexStringLower(LabelStreamFrames.EncodeError(EventStreamErrors.FutureCursor, "Cursor in the future.")));

    [Fact]
    public void Encode_UnsignedLabel_Throws()
    {
        var unsigned = new Label
        {
            Version = 1,
            Src = Did.Parse("did:plc:n3timvoib5nau7gvwd6cshap"),
            Uri = "did:plc:44ybard66vv44zksje25o7dz",
            Val = "spam",
            Cts = AtDatetime.Now(),
        };

        var ex = Assert.Throws<ArgumentException>(() => LabelStreamFrames.Encode(new LabelsEvent { Seq = 1, Labels = [unsigned] }));
        Assert.Contains("unsigned", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_InfoWithoutAName_Throws()
        => Assert.Throws<ArgumentException>(() => LabelStreamFrames.Encode(new LabelInfoEvent { Name = "" }));

    [Fact]
    public async Task Encode_Frames_AreReadBackByTheConsumerAndVerify()
    {
        const string labelerDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
        using var key = AtProtoCrypto.GenerateP256Key();
        using var impostor = AtProtoCrypto.GenerateP256Key();
        var signer = new LabelSigner(Did.Parse(labelerDid), key);
        var forged = new LabelSigner(Did.Parse(labelerDid), impostor);
        var resolver = new FakeDidDocumentResolver()
            .Publish(labelerDid, LabelVerifierTests.LabelerDocument(labelerDid, key));

        var connector = new ScriptedConnector().Connection(
            LabelStreamFrames.Encode(new LabelsEvent
            {
                Seq = 1,
                Labels = [signer.Sign("did:plc:44ybard66vv44zksje25o7dz", "spam"), forged.Sign("did:plc:44ybard66vv44zksje25o7dz", "!hide")],
            }),
            LabelStreamFrames.Encode(new LabelInfoEvent { Name = "OutdatedCursor" }),
            LabelStreamFrames.Encode(new LabelsEvent
            {
                Seq = 2,
                Labels = [signer.Sign("did:plc:44ybard66vv44zksje25o7dz", "spam", negate: true)],
            }));
        var consumer = new LabelStreamConsumer(
            new LabelStreamConsumerOptions
            {
                ServiceUrl = Labeler,
                Reconnect = StreamTestExtensions.Immediate(0),
                Verifier = new LabelVerifier(resolver),
            },
            connector.Connect);

        var messages = await consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        var first = Assert.IsType<LabelsEvent>(messages[0]);
        Assert.Equal(["spam", "!hide"], first.Labels.Select(l => l.Val));
        Assert.Equal(
            [LabelVerificationStatus.Valid, LabelVerificationStatus.InvalidSignature],
            first.Verification!.Select(v => v.Status));
        Assert.Same(first.Labels[1], first.Verification![1].Label);

        Assert.Equal("OutdatedCursor", Assert.IsType<LabelInfoEvent>(messages[1]).Name);

        var second = Assert.IsType<LabelsEvent>(messages[2]);
        Assert.True(second.Labels.Single().Neg);
        Assert.True(second.Verification!.Single().IsValid);
        Assert.Equal(2, consumer.LastSeq);
    }

    [Fact]
    public async Task ConsumeAsync_WithoutAVerifier_LeavesVerificationUnset()
    {
        var connector = new ScriptedConnector().Connection(LabelStreamFrames.Encode(new LabelsEvent
        {
            Seq = 7,
            Labels = [LabelSigningInteropTests.ParseLabel(IndigoLabel)],
        }));
        var consumer = new LabelStreamConsumer(
            new LabelStreamConsumerOptions { ServiceUrl = Labeler, Reconnect = StreamTestExtensions.Immediate(0) },
            connector.Connect);

        var messages = await consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken).DrainAsync();

        var labels = Assert.IsType<LabelsEvent>(Assert.Single(messages));
        Assert.Null(labels.Verification);
        Assert.Equal("bladerunner", labels.Labels.Single().Val);
    }
}
