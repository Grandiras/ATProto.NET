using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.Server.Spaces;

namespace ATProtoNet.Tests.Labeling;

public sealed class LabelVerifierTests : IDisposable
{
    private const string Labeler = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Post = "at://did:plc:44ybard66vv44zksje25o7dz/app.bsky.feed.post/3l6oveex3ii2l";

    private readonly AtProtoKey _labelKey = AtProtoCrypto.GenerateP256Key();
    private readonly AtProtoKey _accountKey = AtProtoCrypto.GenerateP256Key();

    public void Dispose()
    {
        _labelKey.Dispose();
        _accountKey.Dispose();
    }

    /// <summary>A labeler's document: its account key, and its label key when it has one.</summary>
    internal static DidDocument LabelerDocument(string did, AtProtoKey? labelKey, AtProtoKey? accountKey = null)
    {
        var methods = new List<VerificationMethod>();
        if (accountKey is not null)
            methods.Add(Multikey(did, "#atproto", accountKey));
        if (labelKey is not null)
            methods.Add(Multikey(did, "#atproto_label", labelKey));

        return new DidDocument { Id = Did.Parse(did), VerificationMethod = methods };

        static VerificationMethod Multikey(string did, string fragment, AtProtoKey key) => new()
        {
            Id = did + fragment,
            Type = "Multikey",
            Controller = did,
            PublicKeyMultibase = key.ToMultikey(),
        };
    }

    private Label Signed(AtProtoKey? key = null, string value = "spam") =>
        new LabelSigner(Did.Parse(Labeler), key ?? _labelKey).Sign(Post, value);

    [Fact]
    public async Task VerifyAsync_SignedWithThePublishedLabelKey_IsValid()
    {
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, _labelKey, _accountKey));
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(_labelKey.ToDidKey(), result.SigningKey);
        Assert.Null(result.Error);
        Assert.Equal(0, resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_BlueskyModerationLabel_VerifiesAgainstItsPublishedDocument()
    {
        var document = JsonSerializer.Deserialize<DidDocument>(
            LabelSigningInteropTests.BlueskyModerationDocument, AtProtoJsonDefaults.Options)!;
        var resolver = new FakeDidDocumentResolver().Publish(LabelSigningInteropTests.BlueskyModerationDid, document);
        var verifier = new LabelVerifier(resolver);

        var results = await verifier.VerifyAllAsync(
            [
                LabelSigningInteropTests.ParseLabel(LabelSigningInteropTests.BlueskyModerationLabel),
                LabelSigningInteropTests.ParseLabel(LabelSigningInteropTests.BlueskyModerationNegation),
            ],
            TestContext.Current.CancellationToken);

        Assert.All(results, result =>
        {
            Assert.Equal(LabelVerificationStatus.Valid, result.Status);
            Assert.Equal(LabelSigningInteropTests.BlueskyModerationLabelKey, result.SigningKey);
        });
    }

    [Fact]
    public async Task VerifyAsync_SignedWithTheAccountKey_IsNotValid()
    {
        // #atproto signs repos; a label must be signed with the key published for labels.
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, _labelKey, _accountKey));
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(_accountKey), TestContext.Current.CancellationToken);

        Assert.Equal(LabelVerificationStatus.InvalidSignature, result.Status);
        Assert.Equal(_labelKey.ToDidKey(), result.SigningKey);
    }

    [Fact]
    public async Task VerifyAsync_NoLabelKeyPublished_DoesNotFallBackToTheAccountKey()
    {
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, labelKey: null, _accountKey));
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(_accountKey), TestContext.Current.CancellationToken);

        Assert.Equal(LabelVerificationStatus.NoLabelKey, result.Status);
        Assert.Null(result.SigningKey);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_KeyRotatedSinceCached_RefetchesOnceAndVerifies()
    {
        using var rotated = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver()
            .Publish(Labeler, LabelerDocument(Labeler, _labelKey))
            .Rotate(Labeler, LabelerDocument(Labeler, rotated));
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(rotated), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(rotated.ToDidKey(), result.SigningKey);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_LabelKeyPublishedSinceCached_RefetchesAndVerifies()
    {
        var resolver = new FakeDidDocumentResolver()
            .Publish(Labeler, LabelerDocument(Labeler, labelKey: null, _accountKey))
            .Rotate(Labeler, LabelerDocument(Labeler, _labelKey, _accountKey));
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_SignedBeforeARotation_IsInvalidAfterOneRefetch()
    {
        using var rotated = AtProtoCrypto.GenerateP256Key();
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, rotated));
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(_labelKey), TestContext.Current.CancellationToken);

        Assert.Equal(LabelVerificationStatus.InvalidSignature, result.Status);
        Assert.Equal(rotated.ToDidKey(), result.SigningKey);
        Assert.Equal(1, resolver.RefreshCount);
    }

    [Fact]
    public async Task VerifyAsync_TamperedLabel_IsInvalid()
    {
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, _labelKey));
        var verifier = new LabelVerifier(resolver);
        var signed = Signed();
        var tampered = new Label
        {
            Version = signed.Version,
            Src = signed.Src,
            Uri = signed.Uri,
            Val = "!hide",
            Cts = signed.Cts,
            Sig = signed.Sig,
        };

        var result = await verifier.VerifyAsync(tampered, TestContext.Current.CancellationToken);

        Assert.Equal(LabelVerificationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_MalformedLabelKey_ReportsNoLabelKey()
    {
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, new DidDocument
        {
            Id = Did.Parse(Labeler),
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = Labeler + "#atproto_label",
                    Type = "Multikey",
                    Controller = Labeler,
                    PublicKeyMultibase = "zNotAKey",
                },
            ],
        });
        var verifier = new LabelVerifier(resolver);

        var result = await verifier.VerifyAsync(Signed(), TestContext.Current.CancellationToken);

        Assert.Equal(LabelVerificationStatus.NoLabelKey, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_IssuerDoesNotResolve_ReportsTheResolutionError()
    {
        var verifier = new LabelVerifier(new FakeDidDocumentResolver());

        var result = await verifier.VerifyAsync(Signed(), TestContext.Current.CancellationToken);

        Assert.Equal(LabelVerificationStatus.IssuerUnresolved, result.Status);
        Assert.Equal(DidResolutionErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task VerifyAsync_UnsignedOrUnversioned_ResolvesNothing()
    {
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, _labelKey));
        var verifier = new LabelVerifier(resolver);
        var signed = Signed();
        var unsigned = new Label { Version = 1, Src = signed.Src, Uri = signed.Uri, Val = signed.Val, Cts = signed.Cts };
        var unversioned = new Label { Src = signed.Src, Uri = signed.Uri, Val = signed.Val, Cts = signed.Cts, Sig = signed.Sig };

        var results = await verifier.VerifyAllAsync([unsigned, unversioned], TestContext.Current.CancellationToken);

        Assert.Equal(
            [LabelVerificationStatus.Unsigned, LabelVerificationStatus.UnsupportedVersion],
            results.Select(r => r.Status));
        Assert.Equal(0, resolver.ResolveCount);
    }

    [Fact]
    public async Task VerifyAllAsync_KeepsTheOrderAndEveryLabel()
    {
        var resolver = new FakeDidDocumentResolver().Publish(Labeler, LabelerDocument(Labeler, _labelKey));
        var verifier = new LabelVerifier(resolver);
        using var otherKey = AtProtoCrypto.GenerateP256Key();
        var labels = new[] { Signed(value: "a"), Signed(otherKey, "b"), Signed(value: "c") };

        var results = await verifier.VerifyAllAsync(labels, TestContext.Current.CancellationToken);

        Assert.Equal(labels, results.Select(r => r.Label));
        Assert.Equal([true, false, true], results.Select(r => r.IsValid));
    }

    [Fact]
    public async Task VerifyAsync_Cancelled_Throws()
    {
        var verifier = new LabelVerifier(new CancellingResolver());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(Signed(), new CancellationToken(canceled: true)));
    }

    private sealed class CancellingResolver : IDidResolver
    {
        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
            Task.FromCanceled<DidDocument>(cancellationToken);
    }
}
