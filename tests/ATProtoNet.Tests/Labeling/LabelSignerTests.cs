using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Labeling;

public sealed class LabelSignerTests : IDisposable
{
    private static readonly Did Labeler = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
    private const string Post = "at://did:plc:44ybard66vv44zksje25o7dz/app.bsky.feed.post/3l6oveex3ii2l";

    private readonly AtProtoKey _key = AtProtoCrypto.GenerateP256Key();

    public void Dispose() => _key.Dispose();

    private static Label Unsigned(string val = "spam", Did? src = null) => new()
    {
        Src = src ?? Labeler,
        Uri = Post,
        Val = val,
        Cts = AtDatetime.Parse("2026-09-26T10:00:00.000Z"),
    };

    [Fact]
    public void Sign_Label_SetsVersionAndAVerifyingSignature()
    {
        var signer = new LabelSigner(Labeler, _key);

        var signed = signer.Sign(Unsigned());

        Assert.Equal(LabelSigning.Version, signed.Version);
        Assert.Equal(64, signed.Sig!.Length);
        Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(signed, signer.SigningKey));
        Assert.Equal(_key.ToDidKey(), signer.SigningKey);
    }

    [Fact]
    public void Sign_Subject_StampsTheCurrentTimeAndSetsTheFields()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-09-26T12:34:56.789Z"));
        var cid = Cid.Parse("bafyreifxykqhed72s26cr4i64rxvrtofeqrly3j4vjzbkvo3ckkjbxjqtq");
        var expires = AtDatetime.Parse("2026-10-26T00:00:00.000Z");
        var signer = new LabelSigner(Labeler, _key, clock);

        var signed = signer.Sign(Post, "!hide", cid, negate: true, expiresAt: expires);

        Assert.Equal(Labeler, signed.Src);
        Assert.Equal(Post, signed.Uri);
        Assert.Equal(cid, signed.Cid);
        Assert.Equal("!hide", signed.Val);
        Assert.True(signed.Neg);
        Assert.Equal("2026-09-26T12:34:56.789Z", signed.Cts.ToString());
        Assert.Equal(expires, signed.Exp);
        Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(signed, signer.SigningKey));
    }

    [Fact]
    public void Sign_NotNegated_OmitsNeg()
    {
        var signed = new LabelSigner(Labeler, _key).Sign(Post, "spam");

        Assert.Null(signed.Neg);
        Assert.DoesNotContain("\"neg\"", JsonSerializer.Serialize(signed, AtProtoJsonDefaults.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_ReplacesAnExistingSignatureAndDropsUndeclaredFields()
    {
        var signer = new LabelSigner(Labeler, _key);
        var label = JsonSerializer.Deserialize<Label>(
            """{"src":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","uri":"did:plc:44ybard66vv44zksje25o7dz","val":"spam","cts":"2026-09-26T10:00:00.000Z","sig":{"$bytes":"AQID"},"extra":true}""",
            AtProtoJsonDefaults.Options)!;

        var signed = signer.Sign(label);

        Assert.Null(signed.ExtensionData);
        Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(signed, signer.SigningKey));
    }

    [Fact]
    public void Sign_AnotherLabelersLabel_Throws()
    {
        var signer = new LabelSigner(Labeler, _key);

        var ex = Assert.Throws<ArgumentException>(() => signer.Sign(Unsigned(src: Did.Parse("did:plc:44ybard66vv44zksje25o7dz"))));
        Assert.Contains("signs only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_UnknownVersion_Throws()
    {
        var label = Unsigned();
        var versioned = new Label { Version = 2, Src = label.Src, Uri = label.Uri, Val = label.Val, Cts = label.Cts };

        Assert.Throws<ArgumentException>(() => new LabelSigner(Labeler, _key).Sign(versioned));
    }

    [Fact]
    public void Sign_EmptyValue_Throws()
        => Assert.Throws<ArgumentException>(() => new LabelSigner(Labeler, _key).Sign(Unsigned("")));

    [Fact]
    public void Sign_ValueLength_IsBoundedAt128Utf8Bytes()
    {
        // 64 two-byte characters: the Lexicon's maxLength counts UTF-8 bytes, not characters.
        var value = new string('é', 64);
        var signer = new LabelSigner(Labeler, _key);

        Assert.Equal(value, signer.Sign(Unsigned(value)).Val);
        Assert.Throws<ArgumentException>(() => signer.Sign(Unsigned(value + "a")));
        Assert.Throws<ArgumentException>(() => signer.Sign(Unsigned(new string('a', 129))));
    }

    [Fact]
    public void Sign_InvalidTimestamps_Throw()
    {
        var signer = new LabelSigner(Labeler, _key);
        var lenient = JsonSerializer.Deserialize<AtDatetime>("\"2026-09-26 10:00:00\"", AtProtoJsonDefaults.Options);

        Assert.Throws<ArgumentException>(() => signer.Sign(new Label { Src = Labeler, Uri = Post, Val = "spam", Cts = lenient }));
        Assert.Throws<ArgumentException>(() => signer.Sign(new Label { Src = Labeler, Uri = Post, Val = "spam", Cts = default }));
        Assert.Throws<ArgumentException>(() => signer.Sign(new Label
        {
            Src = Labeler,
            Uri = Post,
            Val = "spam",
            Cts = AtDatetime.Now(),
            Exp = lenient,
        }));
    }

    [Fact]
    public void Sign_EmptySubject_Throws()
        => Assert.Throws<ArgumentException>(() => new LabelSigner(Labeler, _key).Sign("", "spam"));

    [Fact]
    public void Sign_PublicKeyOnly_Throws()
    {
        using var publicOnly = AtProtoCrypto.FromDidKey(_key.ToDidKey());

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => new LabelSigner(Labeler, publicOnly).Sign(Unsigned()));
    }

    [Fact]
    public async Task Sign_ConcurrentCalls_AllVerify()
    {
        var signer = new LabelSigner(Labeler, _key);

        var labels = await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            Task.Run(() => signer.Sign(Post, $"value-{i}"), TestContext.Current.CancellationToken)));

        Assert.All(labels, label => Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(label, signer.SigningKey)));
    }
}
