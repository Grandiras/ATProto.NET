using ATProtoNet.Identity;

namespace ATProtoNet.Spaces;

/// <summary>
/// Resolves a space authority's key material and host endpoint from its DID document.
/// </summary>
/// <remarks>
/// <para>A space authority publishes two optional entries in its DID document: a verification
/// method with id <c>#atproto_space</c>, the public key its space credentials verify against,
/// and a service entry with id <c>#atproto_space_host</c>, the endpoint of the space host.</para>
/// <para>Both fall back when absent — the signing key to the account's <c>#atproto</c> key and
/// the host to its <c>#atproto_pds</c> service endpoint — so an ordinary account is a usable
/// space authority with no DID-document change at all. That is what makes personal-data spaces
/// (bookmarks, drafts, mutes) work on any PDS. An authority MAY publish the dedicated entries
/// to point at distinct key material or a distinct host.</para>
/// </remarks>
public static class SpaceAuthority
{
    /// <summary>The DID document verification method id for a space's credential signing key.</summary>
    public const string SigningKeyId = "#atproto_space";

    /// <summary>The DID document service id for a space host endpoint.</summary>
    public const string HostServiceId = "#atproto_space_host";

    /// <summary>The DID document service type published for a space host.</summary>
    public const string HostServiceType = "AtprotoSpaceHost";

    /// <summary>
    /// The service identifier used as the <c>aud</c> of a delegation token or client attestation
    /// addressed to a space authority acting as the space host.
    /// </summary>
    /// <param name="authorityDid">The space authority's DID.</param>
    /// <remarks>
    /// This is the audience, not necessarily the endpoint: an authority that publishes no
    /// <c>#atproto_space_host</c> entry is still addressed by this identifier while being
    /// reached at its <c>#atproto_pds</c> endpoint.
    /// </remarks>
    public static string HostAudience(string authorityDid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityDid);
        return $"{authorityDid}{HostServiceId}";
    }

    /// <summary>
    /// Extracts the <c>did:key</c> a space's credentials are verified against, falling back to
    /// the account's <c>#atproto</c> signing key when no <c>#atproto_space</c> entry is published.
    /// </summary>
    /// <param name="didDocument">The authority's DID document.</param>
    /// <returns>The signing key as a <c>did:key</c> string, or <see langword="null"/> when neither entry exists.</returns>
    public static string? GetSigningKey(DidDocument didDocument)
    {
        ArgumentNullException.ThrowIfNull(didDocument);

        return FindMultikey(didDocument, SigningKeyId)
            ?? FindMultikey(didDocument, "#atproto");
    }

    /// <summary>
    /// Extracts the space host endpoint, falling back to the account's <c>#atproto_pds</c>
    /// service endpoint when no <c>#atproto_space_host</c> entry is published.
    /// </summary>
    /// <param name="didDocument">The authority's DID document.</param>
    /// <returns>The host URL, or <see langword="null"/> when neither entry exists.</returns>
    public static string? GetHostEndpoint(DidDocument didDocument)
    {
        ArgumentNullException.ThrowIfNull(didDocument);

        return FindService(didDocument, HostServiceId)
            ?? didDocument.GetPdsEndpoint();
    }

    /// <summary>
    /// Extracts the endpoint a service identifier names, for delivering write notifications.
    /// </summary>
    /// <param name="didDocument">The subscriber's DID document.</param>
    /// <param name="serviceId">
    /// The service fragment (e.g. <c>#atproto_space_syncer</c>). When omitted, the space host
    /// entry is used.
    /// </param>
    /// <returns>The endpoint URL, or <see langword="null"/> when the fragment is not published.</returns>
    public static string? GetServiceEndpoint(DidDocument didDocument, string? serviceId)
    {
        ArgumentNullException.ThrowIfNull(didDocument);

        if (string.IsNullOrEmpty(serviceId))
            return GetHostEndpoint(didDocument);

        if (!serviceId.StartsWith('#'))
            serviceId = "#" + serviceId;

        return FindService(didDocument, serviceId);
    }

    /// <summary>
    /// Splits a service identifier — a DID with an optional service fragment, as
    /// <c>registerNotify</c> and <c>managingApp</c> carry — into its DID and fragment.
    /// </summary>
    /// <param name="serviceIdentifier">
    /// The identifier, e.g. <c>did:web:syncer.example.com#atproto_space_syncer</c>.
    /// </param>
    /// <returns>The DID and the fragment (including its leading <c>#</c>), or a null fragment when bare.</returns>
    /// <exception cref="ArgumentException">Thrown when the DID part is not a valid DID.</exception>
    public static (string Did, string? Fragment) ParseServiceIdentifier(string serviceIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceIdentifier);

        var hash = serviceIdentifier.IndexOf('#');
        var did = hash < 0 ? serviceIdentifier : serviceIdentifier[..hash];
        var fragment = hash < 0 ? null : serviceIdentifier[hash..];

        if (!Did.TryParse(did, out _))
        {
            throw new ArgumentException(
                $"Service identifier must begin with a DID: '{serviceIdentifier}'.", nameof(serviceIdentifier));
        }

        return (did, fragment);
    }

    private static string? FindMultikey(DidDocument didDocument, string fragment)
    {
        var method = didDocument.VerificationMethod.FirstOrDefault(vm =>
            (vm.Id == fragment || vm.Id == $"{didDocument.Id}{fragment}") &&
            vm.Type == "Multikey" &&
            !string.IsNullOrEmpty(vm.PublicKeyMultibase));

        return method?.PublicKeyMultibase is { } multikey ? $"did:key:{multikey}" : null;
    }

    private static string? FindService(DidDocument didDocument, string fragment)
    {
        var service = didDocument.Service.FirstOrDefault(s =>
            s.Id == fragment || s.Id == $"{didDocument.Id}{fragment}");

        return string.IsNullOrEmpty(service?.Endpoint) ? null : service.Endpoint;
    }
}
