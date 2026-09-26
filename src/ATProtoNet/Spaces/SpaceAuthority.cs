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
/// <para>Only an <em>absent</em> entry falls back. Proposal 0016 makes an entry that is published
/// but malformed an error: a <c>#atproto_space</c> key of a type this SDK does not read or with
/// no usable key material, or a <c>#atproto_space_host</c> service that is not of type
/// <c>AtprotoSpaceHost</c> or whose endpoint is not an absolute <c>https</c> URL. Quietly using
/// the <c>#atproto</c> entries instead would verify against, or deliver to, something the
/// authority never named.</para>
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
    public static string HostAudience(Did authorityDid)
    {
        ArgumentNullException.ThrowIfNull(authorityDid);
        return $"{authorityDid}{HostServiceId}";
    }

    /// <summary>
    /// Extracts the <c>did:key</c> a space's credentials are verified against, falling back to
    /// the account's <c>#atproto</c> signing key when no <c>#atproto_space</c> entry is published.
    /// </summary>
    /// <param name="didDocument">The authority's DID document.</param>
    /// <returns>The signing key as a <c>did:key</c> string, or <see langword="null"/> when neither entry exists.</returns>
    /// <exception cref="FormatException">
    /// Thrown when a <c>#atproto_space</c> entry is published but unusable: a type this SDK does
    /// not read, no key material, or key material that does not decode. The fallback applies only
    /// to an absent entry. Also thrown when the <c>#atproto</c> key material is malformed.
    /// </exception>
    /// <remarks>
    /// Both the <c>Multikey</c> and the legacy <c>Ecdsa...VerificationKey2019</c> verification
    /// method types are read — see <see cref="VerificationMethod.ToDidKey"/>.
    /// </remarks>
    public static string? GetSigningKey(DidDocument didDocument)
    {
        ArgumentNullException.ThrowIfNull(didDocument);

        return didDocument.TryGetVerificationKey(SigningKeyId, out var key) switch
        {
            DidDocumentEntryStatus.Found => key,
            DidDocumentEntryStatus.Absent => didDocument.GetSigningKey(),
            _ => throw new FormatException(
                $"'{didDocument.Id}' publishes a {SigningKeyId} verification method that is not a usable key."),
        };
    }

    /// <summary>
    /// Extracts the space host endpoint, falling back to the account's <c>#atproto_pds</c>
    /// service endpoint when no <c>#atproto_space_host</c> entry is published.
    /// </summary>
    /// <param name="didDocument">The authority's DID document.</param>
    /// <returns>
    /// The host URL, or <see langword="null"/> when no <c>#atproto_space_host</c> entry is
    /// published and the PDS entry is absent or has no absolute http(s) endpoint.
    /// </returns>
    /// <exception cref="FormatException">
    /// Thrown when a <c>#atproto_space_host</c> entry is published but is not of type
    /// <c>AtprotoSpaceHost</c> or its endpoint is not an absolute <c>https</c> URL. The fallback
    /// applies only to an absent entry.
    /// </exception>
    public static Uri? GetHostEndpoint(DidDocument didDocument)
    {
        ArgumentNullException.ThrowIfNull(didDocument);

        return didDocument.TryGetServiceEndpoint(HostServiceId, HostServiceType, out var endpoint) switch
        {
            DidDocumentEntryStatus.Found when endpoint!.Scheme == Uri.UriSchemeHttps => endpoint,
            DidDocumentEntryStatus.Absent => didDocument.GetPdsEndpoint(),
            _ => throw new FormatException(
                $"'{didDocument.Id}' publishes a {HostServiceId} service that is not of type " +
                $"{HostServiceType} with an absolute https endpoint."),
        };
    }

    /// <summary>
    /// Extracts the endpoint a service identifier names, for delivering write notifications.
    /// </summary>
    /// <param name="didDocument">The subscriber's DID document.</param>
    /// <param name="serviceId">
    /// The service fragment (e.g. <c>#atproto_space_syncer</c>). When omitted, the space host
    /// entry is used.
    /// </param>
    /// <returns>
    /// The endpoint URL, or <see langword="null"/> when the fragment is not published with an
    /// absolute http(s) endpoint.
    /// </returns>
    /// <exception cref="FormatException">
    /// Thrown when the space host is named and its <c>#atproto_space_host</c> entry is published
    /// but malformed; see <see cref="GetHostEndpoint"/>.
    /// </exception>
    /// <remarks>
    /// The space host is resolved the same way whether it is named or implied: through
    /// <see cref="GetHostEndpoint"/>, falling back to <c>#atproto_pds</c>. An authority on an
    /// ordinary PDS publishes no <c>#atproto_space_host</c> entry, and a notification addressed
    /// to <c>{authority}#atproto_space_host</c> must still reach it there.
    /// </remarks>
    public static Uri? GetServiceEndpoint(DidDocument didDocument, string? serviceId)
    {
        ArgumentNullException.ThrowIfNull(didDocument);

        if (string.IsNullOrEmpty(serviceId))
            return GetHostEndpoint(didDocument);

        if (!serviceId.StartsWith('#'))
            serviceId = "#" + serviceId;

        return serviceId == HostServiceId
            ? GetHostEndpoint(didDocument)
            : didDocument.GetServiceEndpoint(serviceId);
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
    public static (Did Did, string? Fragment) ParseServiceIdentifier(string serviceIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceIdentifier);

        var hash = serviceIdentifier.IndexOf('#');
        var fragment = hash < 0 ? null : serviceIdentifier[hash..];

        if (!Did.TryParse(hash < 0 ? serviceIdentifier : serviceIdentifier[..hash], out var did))
        {
            throw new ArgumentException(
                $"Service identifier must begin with a DID: '{serviceIdentifier}'.", nameof(serviceIdentifier));
        }

        return (did, fragment);
    }
}
