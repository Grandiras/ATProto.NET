using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceAuthorityTests
{
    private const string Did = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";

    private static DidDocument Document(params DidDocumentService[] services) => new()
    {
        Id = ATProtoNet.Identity.Did.Parse(Did),
        Service = [.. services],
    };

    private static DidDocument Document(params VerificationMethod[] methods) => new()
    {
        Id = ATProtoNet.Identity.Did.Parse(Did),
        VerificationMethod = [.. methods],
    };

    private static DidDocumentService Pds => new()
    {
        Id = "#atproto_pds",
        Type = "AtprotoPersonalDataServer",
        Endpoint = "https://pds.example.com",
    };

    private static DidDocumentService SpaceHost(string type = SpaceAuthority.HostServiceType, string? endpoint = "https://spaces.example.com") => new()
    {
        Id = SpaceAuthority.HostServiceId,
        Type = type,
        Endpoint = endpoint,
    };

    private static VerificationMethod Key(string fragment, string type, string? multibase) => new()
    {
        Id = $"{Did}{fragment}",
        Type = type,
        PublicKeyMultibase = multibase,
    };

    [Fact]
    public void GetServiceEndpoint_SpaceHostFragmentOnAnOrdinaryAccount_FallsBackToThePds()
    {
        // `{authority}#atproto_space_host` is what a repo host registers for a space's authority,
        // and an authority on an ordinary PDS publishes no such entry.
        Assert.Equal<Uri?>(
            new Uri("https://pds.example.com"),
            SpaceAuthority.GetServiceEndpoint(Document(Pds), SpaceAuthority.HostServiceId));
    }

    [Fact]
    public void GetServiceEndpoint_PublishedSpaceHost_IsPreferredOverThePds()
    {
        Assert.Equal<Uri?>(
            new Uri("https://spaces.example.com"),
            SpaceAuthority.GetServiceEndpoint(Document(Pds, SpaceHost()), "atproto_space_host"));
    }

    [Fact]
    public void GetServiceEndpoint_OtherFragment_DoesNotFallBack()
    {
        // A syncer that names an entry it does not publish is unreachable, not its PDS.
        Assert.Null(SpaceAuthority.GetServiceEndpoint(Document(Pds), "#atproto_space_syncer"));
    }

    // ── Proposal 0016: a present-but-malformed entry is an error, not a fallback ──

    [Theory]
    [InlineData("AtprotoPersonalDataServer", "https://spaces.example.com")] // wrong type
    [InlineData(SpaceAuthority.HostServiceType, "http://spaces.example.com")] // not https
    [InlineData(SpaceAuthority.HostServiceType, "spaces.example.com")]         // not absolute
    [InlineData(SpaceAuthority.HostServiceType, null)]                          // structured endpoint
    public void GetHostEndpoint_MalformedSpaceHostEntry_ThrowsRatherThanFallingBackToThePds(string type, string? endpoint)
    {
        var document = Document(Pds, SpaceHost(type, endpoint));

        Assert.Throws<FormatException>(() => SpaceAuthority.GetHostEndpoint(document));
        Assert.Throws<FormatException>(() => SpaceAuthority.GetServiceEndpoint(document, SpaceAuthority.HostServiceId));
    }

    [Fact]
    public void GetHostEndpoint_NoEntryAtAll_IsNull()
    {
        Assert.Null(SpaceAuthority.GetHostEndpoint(Document(Array.Empty<DidDocumentService>())));
    }

    [Fact]
    public void GetSigningKey_NoSpaceEntry_FallsBackToTheAccountKey()
    {
        using var account = AtProtoCrypto.GenerateP256Key();

        var document = Document(Key("#atproto", "Multikey", account.ToMultikey()));

        Assert.Equal(account.ToDidKey(), SpaceAuthority.GetSigningKey(document));
    }

    [Theory]
    [InlineData("JsonWebKey2020", true)]  // a type this SDK does not read
    [InlineData("Multikey", false)]       // no key material
    public void GetSigningKey_MalformedSpaceEntry_ThrowsRatherThanFallingBackToTheAccountKey(string type, bool withMaterial)
    {
        using var account = AtProtoCrypto.GenerateP256Key();
        using var space = AtProtoCrypto.GenerateP256Key();

        var document = Document(
            Key("#atproto", "Multikey", account.ToMultikey()),
            Key("#atproto_space", type, withMaterial ? space.ToMultikey() : null));

        Assert.Throws<FormatException>(() => SpaceAuthority.GetSigningKey(document));
    }

    [Fact]
    public void GetSigningKey_SpaceEntryWhoseKeyDoesNotDecode_Throws()
    {
        using var account = AtProtoCrypto.GenerateP256Key();

        var document = Document(
            Key("#atproto", "Multikey", account.ToMultikey()),
            Key("#atproto_space", "Multikey", "z" + AtProtoCrypto.Base58Encode(new byte[35])));

        Assert.Throws<FormatException>(() => SpaceAuthority.GetSigningKey(document));
    }
}
