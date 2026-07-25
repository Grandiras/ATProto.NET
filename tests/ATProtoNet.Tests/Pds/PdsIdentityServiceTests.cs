using ATProtoNet.Crypto;
using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public sealed class PdsIdentityServiceTests
{
    private readonly InMemoryAccountStore _accounts = new();

    private PdsOptions Options(PdsDidMethod method = PdsDidMethod.Plc) => new()
    {
        Hostname = "pds.test.local",
        PublicUrl = "https://pds.test.local",
        DidMethod = method,
        AvailableUserDomains = ["test.local"],
    };

    private async Task<PdsAccount> AddAccountAsync(PdsIdentityService identity, string handle)
    {
        var minted = await identity.CreateIdentityAsync(handle);
        var account = new PdsAccount
        {
            Did = minted.Did,
            Handle = handle,
            PasswordHash = "x",
            SigningKey = minted.SigningKey,
            RotationKey = minted.RotationKey,
        };
        await _accounts.CreateAsync(account);
        return account;
    }

    // ── did:plc ──────────────────────────────────────────────

    [Fact]
    public async Task CreateIdentityAsync_Plc_DerivesADidAndBothKeys()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var minted = await identity.CreateIdentityAsync("alice.test.local");

        Assert.StartsWith("did:plc:", minted.Did);
        Assert.NotEmpty(minted.SigningKey);
        Assert.NotNull(minted.RotationKey);
        Assert.False(minted.Published);
    }

    [Fact]
    public async Task CreateIdentityAsync_Plc_DoesNotContactTheDirectoryByDefault()
    {
        // RegisterDidsWithPlc defaults to false precisely so that creating an account never
        // writes to a public append-only directory as a side effect.
        var options = Options();
        options.PlcDirectoryUrl = "http://127.0.0.1:1/unreachable";

        using var identity = new PdsIdentityService(options, _accounts);
        var minted = await identity.CreateIdentityAsync("alice.test.local");

        Assert.False(minted.Published);
        Assert.StartsWith("did:plc:", minted.Did);
    }

    [Fact]
    public async Task CreateIdentityAsync_SameHandleTwice_YieldsDifferentDids()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);

        var first = await identity.CreateIdentityAsync("alice.test.local");
        var second = await identity.CreateIdentityAsync("alice.test.local");

        Assert.NotEqual(first.Did, second.Did);
    }

    [Fact]
    public async Task CreateIdentityAsync_ExplicitDid_IsUsedVerbatimWithNoRotationKey()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var minted = await identity.CreateIdentityAsync("alice.test.local", "did:web:alice.example.com");

        Assert.Equal("did:web:alice.example.com", minted.Did);
        Assert.Null(minted.RotationKey);
        Assert.NotEmpty(minted.SigningKey);
    }

    // ── did:web ──────────────────────────────────────────────

    [Fact]
    public async Task CreateIdentityAsync_Web_DerivesTheDidFromTheHandle()
    {
        using var identity = new PdsIdentityService(Options(PdsDidMethod.Web), _accounts);
        var minted = await identity.CreateIdentityAsync("alice.test.local");

        Assert.Equal("did:web:alice.test.local", minted.Did);
        Assert.Null(minted.RotationKey);
    }

    [Theory]
    [InlineData("alice.example.com", "did:web:alice.example.com")]
    [InlineData("ALICE.Example.COM", "did:web:alice.example.com")]
    [InlineData("localhost:3000", "did:web:localhost%3A3000")]
    public void BuildWebDid_NormalizesHostAndEncodesPort(string host, string expected)
    {
        Assert.Equal(expected, PdsIdentityService.BuildWebDid(host));
    }

    [Fact]
    public async Task GetWebDidDocumentAsync_KnownHost_ReturnsTheDocument()
    {
        using var identity = new PdsIdentityService(Options(PdsDidMethod.Web), _accounts);
        await AddAccountAsync(identity, "alice.test.local");

        var document = await identity.GetWebDidDocumentAsync("alice.test.local");

        Assert.NotNull(document);
        Assert.Equal("did:web:alice.test.local", document!.Id);
    }

    [Fact]
    public async Task GetWebDidDocumentAsync_UnknownHost_ReturnsNull()
    {
        using var identity = new PdsIdentityService(Options(PdsDidMethod.Web), _accounts);
        Assert.Null(await identity.GetWebDidDocumentAsync("nobody.test.local"));
    }

    // ── DID documents ────────────────────────────────────────

    [Fact]
    public async Task BuildDidDocument_HasTheFieldsAtProtoResolutionNeeds()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var account = await AddAccountAsync(identity, "alice.test.local");

        var document = identity.BuildDidDocument(account);

        Assert.Equal(account.Did, document.Id);
        Assert.Equal("alice.test.local", document.GetHandle());
        Assert.Equal("https://pds.test.local", document.GetPdsEndpoint());
        Assert.Contains("https://www.w3.org/ns/did/v1", document.Context!);
    }

    [Fact]
    public async Task BuildDidDocument_PublishesTheRepoSigningKeyAsAMultikey()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var account = await AddAccountAsync(identity, "alice.test.local");

        var method = Assert.Single(identity.BuildDidDocument(account).VerificationMethod);

        Assert.Equal($"{account.Did}#atproto", method.Id);
        Assert.Equal("Multikey", method.Type);
        Assert.Equal(account.Did, method.Controller);

        // The published key must be the one that actually signs this repo's commits, or nothing
        // on the network can verify them.
        using var signingKey = PdsRepoManager.ImportSigningKey(account.SigningKey);
        Assert.Equal(signingKey.ToMultikey(), method.PublicKeyMultibase);
    }

    [Fact]
    public async Task BuildDidDocument_PublishedKeyVerifiesACommitSignature()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var account = await AddAccountAsync(identity, "alice.test.local");

        var didKey = "did:key:" + identity.BuildDidDocument(account).VerificationMethod[0].PublicKeyMultibase;

        using var signingKey = PdsRepoManager.ImportSigningKey(account.SigningKey);
        var message = "commit bytes"u8.ToArray();
        var signature = signingKey.Sign(message);

        Assert.True(AtProtoCrypto.VerifySignature(didKey, message, signature));
    }

    // ── Handle resolution ────────────────────────────────────

    [Fact]
    public async Task ResolveHandleAsync_KnownHandle_ReturnsTheDid()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var account = await AddAccountAsync(identity, "alice.test.local");

        Assert.Equal(account.Did, await identity.ResolveHandleAsync("alice.test.local"));
    }

    [Fact]
    public async Task ResolveHandleAsync_IsCaseInsensitive()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        var account = await AddAccountAsync(identity, "alice.test.local");

        Assert.Equal(account.Did, await identity.ResolveHandleAsync("ALICE.Test.Local"));
    }

    [Fact]
    public async Task ResolveHandleAsync_UnknownHandle_ReturnsNull()
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        Assert.Null(await identity.ResolveHandleAsync("nobody.test.local"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveHandleAsync_BlankHandle_ReturnsNull(string handle)
    {
        using var identity = new PdsIdentityService(Options(), _accounts);
        Assert.Null(await identity.ResolveHandleAsync(handle));
    }
}
