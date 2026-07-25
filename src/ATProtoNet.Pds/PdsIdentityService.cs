using ATProtoNet.Crypto;
using ATProtoNet.Identity;

namespace ATProtoNet.Pds;

/// <summary>
/// The identity a PDS mints for a new account: its DID plus the private keys backing it.
/// </summary>
/// <param name="Did">The account DID.</param>
/// <param name="SigningKey">Base64 PKCS#8 repo signing key.</param>
/// <param name="RotationKey">Base64 PKCS#8 PLC rotation key, or <c>null</c> for <c>did:web</c>.</param>
/// <param name="Published">Whether the identity was published to a PLC directory.</param>
public sealed record PdsIdentity(string Did, string SigningKey, string? RotationKey, bool Published);

/// <summary>
/// Mints and serves real, resolvable identities for PDS accounts.
/// <para>
/// For <see cref="PdsDidMethod.Plc"/> it generates a rotation key and a repo signing key, builds
/// a <c>plc_operation</c> genesis operation naming this PDS as the account's service endpoint,
/// signs it, and derives the DID from the hash of that signed operation — the same derivation
/// the PLC directory performs, so the DID is verifiable rather than a random placeholder.
/// Submission to the directory is gated behind <see cref="PdsOptions.RegisterDidsWithPlc"/>.
/// </para>
/// <para>
/// For <see cref="PdsDidMethod.Web"/> the DID is derived from the handle's domain and resolves
/// from the <c>/.well-known/did.json</c> this PDS serves, so no external directory is involved.
/// </para>
/// </summary>
public sealed class PdsIdentityService : IDisposable
{
    private readonly PdsOptions _options;
    private readonly IAccountStore _accounts;
    private readonly PlcClient? _plc;
    private readonly bool _ownsPlcClient;

    /// <summary>Creates an identity service.</summary>
    /// <param name="options">PDS configuration.</param>
    /// <param name="accounts">The account store, used for handle and DID lookups.</param>
    /// <param name="plcClient">
    /// An optional pre-configured PLC client. When <c>null</c> and PLC registration is enabled,
    /// one is created for <see cref="PdsOptions.PlcDirectoryUrl"/> and owned by this service.
    /// </param>
    public PdsIdentityService(PdsOptions options, IAccountStore accounts, PlcClient? plcClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));

        if (plcClient is not null)
        {
            _plc = plcClient;
            _ownsPlcClient = false;
        }
        else if (options.RegisterDidsWithPlc)
        {
            _plc = new PlcClient(options.PlcDirectoryUrl);
            _ownsPlcClient = true;
        }
    }

    /// <summary>
    /// Mints an identity for a new account.
    /// </summary>
    /// <param name="handle">The account's handle.</param>
    /// <param name="requestedDid">
    /// A DID supplied by the caller. When present it is used verbatim and no registration
    /// happens — the caller owns that identity and is responsible for its DID document.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PdsIdentity> CreateIdentityAsync(
        string handle, string? requestedDid = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        using var signingKey = GenerateKey();
        var signingKeyBase64 = Convert.ToBase64String(signingKey.ExportPrivateKey());

        if (!string.IsNullOrEmpty(requestedDid))
            return new PdsIdentity(requestedDid, signingKeyBase64, null, Published: false);

        if (_options.DidMethod == PdsDidMethod.Web)
            return new PdsIdentity(BuildWebDid(handle), signingKeyBase64, null, Published: false);

        using var rotationKey = GenerateKey();
        var rotationKeyBase64 = Convert.ToBase64String(rotationKey.ExportPrivateKey());

        var operation = PlcOperationBuilder.CreateGenesisOperation(
            [rotationKey.ToDidKey()],
            signingKey.ToDidKey(),
            handle,
            _options.ResolvedPublicUrl);

        var signed = PlcOperationBuilder.Sign(operation, rotationKey);

        var published = false;
        if (_options.RegisterDidsWithPlc && _plc is not null)
        {
            await _plc.SubmitOperationAsync(signed, cancellationToken).ConfigureAwait(false);
            published = true;
        }

        return new PdsIdentity(signed.Did, signingKeyBase64, rotationKeyBase64, published);
    }

    /// <summary>
    /// Builds the DID document this PDS publishes for an account. For <c>did:web</c> accounts
    /// this is the authoritative document served at <c>/.well-known/did.json</c>; for
    /// <c>did:plc</c> accounts it mirrors what the directory holds and is useful for diagnostics.
    /// </summary>
    /// <param name="account">The account.</param>
    public DidDocument BuildDidDocument(PdsAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var signingKey = PdsRepoManager.ImportSigningKey(account.SigningKey);

        return new DidDocument
        {
            Context =
            [
                "https://www.w3.org/ns/did/v1",
                "https://w3id.org/security/multikey/v1",
                "https://w3id.org/security/suites/secp256k1-2019/v1",
            ],
            Id = account.Did,
            AlsoKnownAs = [$"at://{account.Handle}"],
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = $"{account.Did}#atproto",
                    Type = "Multikey",
                    Controller = account.Did,
                    PublicKeyMultibase = signingKey.ToMultikey(),
                },
            ],
            Service =
            [
                new ServiceEndpoint
                {
                    Id = "#atproto_pds",
                    Type = PlcOperationBuilder.PdsServiceType,
                    Endpoint = _options.ResolvedPublicUrl,
                },
            ],
        };
    }

    /// <summary>
    /// Resolves a handle hosted by this PDS to its DID. Backs both
    /// <c>com.atproto.identity.resolveHandle</c> and <c>/.well-known/atproto-did</c>.
    /// </summary>
    /// <param name="handle">The handle to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID, or <c>null</c> if this PDS does not host the handle.</returns>
    /// <remarks>
    /// The handle is passed to the store as given. Handles are canonically lower-case, but
    /// whether lookup folds case is the store's decision — <see cref="InMemoryAccountStore"/>
    /// matches ordinal-ignore-case, and folding here as well would break a store that
    /// deliberately keeps mixed-case handles.
    /// </remarks>
    public async Task<string?> ResolveHandleAsync(
        string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle)) return null;

        var account = await _accounts.GetByHandleAsync(handle, cancellationToken).ConfigureAwait(false);
        return account?.Did;
    }

    /// <summary>
    /// Returns the DID document for the <c>did:web</c> identity matching <paramref name="host"/>,
    /// or <c>null</c> when this PDS hosts no such account.
    /// </summary>
    /// <param name="host">The request host, e.g. <c>alice.example.com</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DidDocument?> GetWebDidDocumentAsync(
        string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        var did = BuildWebDid(host);
        var account = await _accounts.GetByDidAsync(did, cancellationToken).ConfigureAwait(false);
        return account is null ? null : BuildDidDocument(account);
    }

    /// <summary>
    /// Builds the <c>did:web</c> identifier for a host. The host is lower-cased and its port,
    /// if any, percent-encoded as the did:web method requires.
    /// </summary>
    /// <param name="host">The host, optionally including a port.</param>
    public static string BuildWebDid(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return "did:web:" + host.ToLowerInvariant().Replace(":", "%3A", StringComparison.Ordinal);
    }

    private AtProtoKey GenerateKey()
        => _options.SigningKeyCurve == KeyCurve.K256
            ? AtProtoCrypto.GenerateK256Key()
            : AtProtoCrypto.GenerateP256Key();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsPlcClient)
            _plc?.Dispose();
    }
}
