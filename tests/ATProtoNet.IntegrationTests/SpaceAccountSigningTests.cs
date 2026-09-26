using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// An SDK repo host telling a reference space authority about a write, signed as the writer
/// through <see cref="ISpaceAccountSigner"/>.
/// </summary>
/// <remarks>
/// <para>The reference authority accepts <c>com.atproto.space.notifyWrite</c> only when the
/// service auth's <c>iss</c> is the writer itself (and its <c>aud</c> the authority's bare DID).
/// A repo host that signs as its own service DID is refused, so without per-account signing an
/// account hosted on the SDK never joins a reference authority's writer set.</para>
/// <para>The writer here is a DID the test creates in the network's own PLC, with a signing key
/// the test holds, standing in for an account whose repo an SDK-based PDS hosts.</para>
/// </remarks>
[Collection("Spaces")]
public class SpaceAccountSigningTests(SpaceNetworkFixture fixture)
{
    private static readonly Did SdkHostDid = Did.Parse("did:web:sdk-host.example.com");

    [RequiresSpacesFact]
    public async Task NotifyWrite_SignedAsTheWriter_JoinsTheReferenceAuthoritysWriterSet()
    {
        using var writerKey = AtProtoCrypto.GenerateK256Key();
        var writer = await CreateWriterAsync(writerKey);
        var space = await fixture.CreateSpaceAsync("signing-writer", writePolicy: new PublicPolicy());
        var signer = new SingleAccountSigner(writer, writerKey);
        var notifier = CreateNotifier(signer);

        await notifier.EnsureAuthoritySubscribedAsync(space, writer);
        var delivered = await notifier.NotifyWriteAsync(space, writer, Tid.Next(), new byte[32]);

        Assert.Equal(1, delivered);
        Assert.Contains(writer.Value, await ReadWriterSetAsync(space));
    }

    [RequiresSpacesFact]
    public async Task NotifyWrite_SignedAsTheHostService_IsRefusedByTheReferenceAuthority()
    {
        // The control: the same notification, signed as the host rather than the writer.
        using var writerKey = AtProtoCrypto.GenerateK256Key();
        var writer = await CreateWriterAsync(writerKey);
        var space = await fixture.CreateSpaceAsync("signing-service", writePolicy: new PublicPolicy());
        var notifier = CreateNotifier(accountSigner: null);

        await notifier.EnsureAuthoritySubscribedAsync(space, writer);
        var delivered = await notifier.NotifyWriteAsync(space, writer, Tid.Next(), new byte[32]);

        Assert.Equal(0, delivered);
        Assert.DoesNotContain(writer.Value, await ReadWriterSetAsync(space));
    }

    private SpaceWriteNotifier CreateNotifier(ISpaceAccountSigner? accountSigner) =>
        new(
            new InMemorySpaceAuthorityStore(),
            fixture.DidResolver,
            new ServiceAuthGenerator(SdkHostDid, AtProtoCrypto.GenerateP256Key()),
            new HttpClient(),
            accountSigner);

    /// <summary>Registers a fresh did:plc with the test network's PLC, signing with <paramref name="signingKey"/>.</summary>
    private async Task<Did> CreateWriterAsync(AtProtoKey signingKey)
    {
        using var rotationKey = AtProtoCrypto.GenerateK256Key();
        var genesis = PlcOperationBuilder.Sign(
            PlcOperationBuilder.CreateGenesisOperation(
                [rotationKey.ToDidKey()],
                signingKey.ToDidKey(),
                Handle.Parse($"w{Guid.NewGuid():N}"[..12] + ".test"),
                "https://sdk-host.example.com"),
            rotationKey);

        using var http = new HttpClient();
        var plc = new PlcClient(http, new Uri(TestConfig.PlcUrl), new IdentityResolverOptions { AllowPrivateNetworks = true });
        return await plc.SubmitOperationAsync(genesis);
    }

    private async Task<IReadOnlyList<string>> ReadWriterSetAsync(SpaceUri space)
    {
        await using var provider = fixture.CreateProvider(fixture.Authority);
        using var host = await provider.CreateReaderAsync(space, fixture.PdsUrl);

        var page = await host.Space.ListReposAsync(space);
        return page.Repos.Select(repo => repo.Did.Value).ToList();
    }

    private sealed class SingleAccountSigner(Did account, AtProtoKey key) : ISpaceAccountSigner
    {
        private readonly ServiceAuthGenerator _generator = new(account, key);

        public ValueTask<ServiceAuthGenerator?> GetSignerAsync(Did requested, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(requested == account ? _generator : null);
    }
}
