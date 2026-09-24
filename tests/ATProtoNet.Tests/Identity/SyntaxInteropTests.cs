using ATProtoNet.Identity;
using ATProtoNet.Tests.Interop;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Runs every line of the vendored <c>atproto-interop-tests/syntax</c> fixtures through the
/// identifier parsers: each valid line must parse (and keep its text), each invalid line must
/// be rejected by both <c>TryParse</c> and <c>Parse</c>.
/// </summary>
public class SyntaxInteropTests
{
    public static TheoryData<string> Fixture(string fileName) => SyntaxFixtures.Lines(fileName);

    // ── TID ──

    [Theory]
    [MemberData(nameof(Fixture), "tid_syntax_valid.txt")]
    public void TidTryParse_ValidFixture_Accepts(string value)
    {
        Assert.True(Tid.TryParse(value, out var tid));
        Assert.Equal(value, tid.Value);
    }

    [Theory]
    [MemberData(nameof(Fixture), "tid_syntax_invalid.txt")]
    public void TidTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(Tid.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Tid.Parse(value));
    }

    // ── AT URI ──

    [Theory]
    [MemberData(nameof(Fixture), "aturi_syntax_valid.txt")]
    public void AtUriTryParse_ValidFixture_Accepts(string value)
    {
        Assert.True(AtUri.TryParse(value, out var uri));
        Assert.Equal(value, uri.Value);
    }

    [Theory]
    [MemberData(nameof(Fixture), "aturi_syntax_invalid.txt")]
    public void AtUriTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(AtUri.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => AtUri.Parse(value));
    }

    // ── DID ──

    [Theory]
    [MemberData(nameof(Fixture), "did_syntax_valid.txt")]
    public void DidTryParse_ValidFixture_Accepts(string value)
    {
        Assert.True(Did.TryParse(value, out var did));
        Assert.Equal(value, did.Value);
    }

    [Theory]
    [MemberData(nameof(Fixture), "did_syntax_invalid.txt")]
    public void DidTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(Did.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Did.Parse(value));
    }

    // ── NSID ──

    [Theory]
    [MemberData(nameof(Fixture), "nsid_syntax_valid.txt")]
    public void NsidTryParse_ValidFixture_Accepts(string value)
    {
        Assert.True(Nsid.TryParse(value, out var nsid));
        Assert.Equal(value, nsid.Value);
    }

    [Theory]
    [MemberData(nameof(Fixture), "nsid_syntax_invalid.txt")]
    public void NsidTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(Nsid.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Nsid.Parse(value));
    }

    // ── Handle ──

    [Theory]
    [MemberData(nameof(Fixture), "handle_syntax_valid.txt")]
    public void HandleTryParse_ValidFixture_AcceptsAndLowerCases(string value)
    {
        Assert.True(Handle.TryParse(value, out var handle));
        Assert.Equal(value.ToLowerInvariant(), handle.Value);
    }

    [Theory]
    [MemberData(nameof(Fixture), "handle_syntax_invalid.txt")]
    public void HandleTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(Handle.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Handle.Parse(value));
    }

    // ── Record key ──

    [Theory]
    [MemberData(nameof(Fixture), "recordkey_syntax_valid.txt")]
    public void RecordKeyTryParse_ValidFixture_Accepts(string value)
    {
        Assert.True(RecordKey.TryParse(value, out var rkey));
        Assert.Equal(value, rkey.Value);
    }

    [Theory]
    [MemberData(nameof(Fixture), "recordkey_syntax_invalid.txt")]
    public void RecordKeyTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(RecordKey.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => RecordKey.Parse(value));
    }

    // ── AT identifier ──

    [Theory]
    [MemberData(nameof(Fixture), "atidentifier_syntax_valid.txt")]
    public void AtIdentifierTryParse_ValidFixture_Accepts(string value)
    {
        Assert.True(AtIdentifier.TryParse(value, out var identifier));
        Assert.Equal(value, identifier.Value, ignoreCase: identifier.IsHandle);
    }

    [Theory]
    [MemberData(nameof(Fixture), "atidentifier_syntax_invalid.txt")]
    public void AtIdentifierTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(AtIdentifier.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => AtIdentifier.Parse(value));
    }

    // ── CID ──

    // Only the invalid file applies. cid_syntax_valid.txt checks the generic multiformats
    // syntax (base58, base16, dag-pb, CIDv0-era forms); Cid accepts only the atproto-blessed
    // CIDv1 subset, which none of those lines is. CidTests pins that subset with real CIDs.
    [Theory]
    [MemberData(nameof(Fixture), "cid_syntax_invalid.txt")]
    public void CidTryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(Cid.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => Cid.Parse(value));
    }
}
