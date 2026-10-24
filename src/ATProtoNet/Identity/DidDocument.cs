using ATProtoNet.Crypto;

namespace ATProtoNet.Identity;

/// <summary>A DID document, as a <c>did:plc</c> directory or a <c>did:web</c> host serves it.</summary>
/// <remarks>
/// <para>Immutable: resolvers cache and share documents. AT Protocol reads the claimed handle (<see cref="GetHandle"/>), the
/// signing key (<see cref="GetSigningKey"/>), the PDS (<see cref="GetPdsEndpoint"/>) and, for Spaces, the KEM key
/// (<see cref="TryGetKemKey"/>); a space authority's host and credential key are its PDS and signing key.</para>
/// <para>Lookups by id take the <em>first</em> entry with that id, matching <c>#frag</c> and <c>{did}#frag</c>. <c>Get*</c>
/// lookups read an unusable entry as absent; <c>TryGet*</c> lookups tell it apart, so a security decision can refuse a
/// malformed entry rather than fall back. See <see href="https://atproto.com/specs/did"/>.</para>
/// </remarks>
public sealed partial class DidDocument
{
    /// <summary>The verification method of the account's signing key.</summary>
    public const string SigningKeyId = "#atproto";

    /// <summary>The verification method of a labeler's label signing key.</summary>
    public const string LabelKeyId = "#atproto_label";

    /// <summary>The verification method of the Spaces commitment scheme's KEM key (proposal 0016, alpha 2026-10-22).</summary>
    public const string KemKeyId = "#atproto_kem";

    /// <summary>The service of the account's PDS.</summary>
    public const string PdsServiceId = "#atproto_pds";

    /// <summary>The service type of a PDS.</summary>
    public const string PdsServiceType = "AtprotoPersonalDataServer";

    /// <summary>The service of a labeler.</summary>
    public const string LabelerServiceId = "#atproto_labeler";

    /// <summary>The service type of a labeler.</summary>
    public const string LabelerServiceType = "AtprotoLabeler";

    private DidDocument(Did id, IReadOnlyList<string> alsoKnownAs, IReadOnlyList<VerificationMethod> methods, IReadOnlyList<DidService> services, ReadOnlyMemory<byte> json) =>
        (Id, AlsoKnownAs, VerificationMethods, Services, Json) = (id, alsoKnownAs, methods, services, json);

    /// <summary>The DID the document describes.</summary>
    public Did Id { get; }

    /// <summary>Alternate identifiers; the handle is claimed as <c>at://handle</c>.</summary>
    public IReadOnlyList<string> AlsoKnownAs { get; }

    /// <summary>The published verification methods.</summary>
    public IReadOnlyList<VerificationMethod> VerificationMethods { get; }

    /// <summary>The published services.</summary>
    public IReadOnlyList<DidService> Services { get; }

    /// <summary>The document's JSON, as parsed.</summary>
    public ReadOnlyMemory<byte> Json { get; }

    /// <summary>The handle the document claims: its first <c>at://</c> entry, when that is a valid handle.</summary>
    /// <returns>The claimed handle, or <see langword="null"/>. A claim is not verified: only the handle resolving back makes it so.</returns>
    public Handle? GetHandle()
    {
        for (var i = 0; i < AlsoKnownAs.Count; i++)
        {
            var entry = AlsoKnownAs[i];
            if (!entry.StartsWith("at://", StringComparison.Ordinal)) continue;
            // An @ would be stripped by Handle parsing, but is not part of a handle.
            return !entry.StartsWith("at://@", StringComparison.Ordinal) && Handle.TryParse(entry.AsSpan(5), out var handle) ? handle : null;
        }
        return null;
    }

    /// <summary>The account's signing key (<c>#atproto</c>).</summary>
    /// <returns>The key, or <see langword="null"/> when none is published or it is unusable.</returns>
    public DidKey? GetSigningKey() => TryGetVerificationKey(SigningKeyId).Value;

    /// <summary>The PDS endpoint (<c>#atproto_pds</c> of type <c>AtprotoPersonalDataServer</c>).</summary>
    /// <returns>The endpoint, or <see langword="null"/> when none is published or it is unusable.</returns>
    public Uri? GetPdsEndpoint() => TryGetServiceEndpoint(PdsServiceId, PdsServiceType).Value;

    /// <summary>Looks a verification method's key up strictly.</summary>
    /// <param name="fragment">The fragment, with or without its <c>#</c>.</param>
    /// <returns>The key, or <see cref="DidDocumentEntryStatus.Absent"/> / <see cref="DidDocumentEntryStatus.Malformed"/> when the first entry with that id has an unknown type or unusable key material.</returns>
    public DidDocumentEntry<DidKey> TryGetVerificationKey(string fragment)
    {
        if (FindMethod(fragment) is not { } method) return default;
        try
        {
            return method.ToDidKey() is { } key ? new(DidDocumentEntryStatus.Found, key) : new(DidDocumentEntryStatus.Malformed, null);
        }
        catch (FormatException)
        {
            return new(DidDocumentEntryStatus.Malformed, null);
        }
    }

    /// <summary>Looks a service endpoint up strictly.</summary>
    /// <param name="fragment">The fragment, with or without its <c>#</c>.</param>
    /// <param name="type">The type the entry must have, or <see langword="null"/> for any.</param>
    /// <returns>The endpoint, or <see cref="DidDocumentEntryStatus.Malformed"/> when the first entry with that id has another type or an endpoint that is not an absolute <c>http</c>/<c>https</c> URL.</returns>
    public DidDocumentEntry<Uri> TryGetServiceEndpoint(string fragment, string? type = null)
    {
        fragment = Fragment(fragment);
        for (var i = 0; i < Services.Count; i++)
        {
            var service = Services[i];
            if (!Matches(service.Id, fragment)) continue;
            if (type is not null && service.Type != type) return new(DidDocumentEntryStatus.Malformed, null);
            return service.HttpEndpoint is { } endpoint ? new(DidDocumentEntryStatus.Found, endpoint) : new(DidDocumentEntryStatus.Malformed, null);
        }
        return default;
    }

    /// <summary>The Spaces KEM key (<c>#atproto_kem</c>), never a signing key in its place.</summary>
    /// <returns>
    /// The key, <see cref="DidDocumentEntryStatus.Absent"/> when none is published (the account's space repos cannot be synced with
    /// credentials until it is), or <see cref="DidDocumentEntryStatus.Malformed"/> unless the entry has id <c>{did}#atproto_kem</c>,
    /// controller the document's DID, type <c>Multikey</c> and a P-256 key.
    /// </returns>
    public DidDocumentEntry<KemPublicKey> TryGetKemKey()
    {
        if (FindMethod(KemKeyId) is not { } method) return default;
        if (method.Id != Id.Value + KemKeyId || method.Controller != Id.Value || method.Type != "Multikey" || method.PublicKeyMultibase is not { } multikey)
            return new(DidDocumentEntryStatus.Malformed, null);
        try
        {
            return new(DidDocumentEntryStatus.Found, KemPublicKey.FromMultikey(multikey));
        }
        catch (FormatException)
        {
            return new(DidDocumentEntryStatus.Malformed, null);
        }
    }

    private VerificationMethod? FindMethod(string fragment)
    {
        fragment = Fragment(fragment);
        for (var i = 0; i < VerificationMethods.Count; i++)
            if (Matches(VerificationMethods[i].Id, fragment)) return VerificationMethods[i];
        return null;
    }

    private static string Fragment(string fragment)
    {
        ArgumentException.ThrowIfNullOrEmpty(fragment);
        return fragment.StartsWith('#') ? fragment : "#" + fragment;
    }

    private bool Matches(string id, string fragment) =>
        id == fragment || (id.Length == Id.Value.Length + fragment.Length && id.StartsWith(Id.Value, StringComparison.Ordinal) && id.EndsWith(fragment, StringComparison.Ordinal));
}

/// <summary>How a strict DID-document lookup went.</summary>
public enum DidDocumentEntryStatus
{
    /// <summary>No entry with that id is published.</summary>
    Absent,

    /// <summary>The entry is published and usable.</summary>
    Found,

    /// <summary>The entry is published but cannot be used.</summary>
    Malformed,
}

/// <summary>The outcome of a strict DID-document lookup.</summary>
/// <typeparam name="T">The value looked up.</typeparam>
/// <param name="Status">Whether the entry is absent, usable or malformed.</param>
/// <param name="Value">The value, when <see cref="Status"/> is <see cref="DidDocumentEntryStatus.Found"/>.</param>
public readonly record struct DidDocumentEntry<T>(DidDocumentEntryStatus Status, T? Value) where T : class;

/// <summary>A verification method of a DID document.</summary>
/// <param name="Id">The id, such as <c>did:plc:…#atproto</c> or <c>#atproto</c>.</param>
/// <param name="Type">The type, such as <c>Multikey</c>.</param>
/// <param name="Controller">The controlling DID, if given.</param>
/// <param name="PublicKeyMultibase">The key material, if given.</param>
public sealed class VerificationMethod(string Id, string Type, string? Controller, string? PublicKeyMultibase)
{
    private DidKey? _key;

    /// <summary>The id, such as <c>did:plc:…#atproto</c> or <c>#atproto</c>.</summary>
    public string Id { get; } = Id;

    /// <summary>The type, such as <c>Multikey</c>.</summary>
    public string Type { get; } = Type;

    /// <summary>The controlling DID, if given.</summary>
    public string? Controller { get; } = Controller;

    /// <summary>The key material, if given.</summary>
    public string? PublicKeyMultibase { get; } = PublicKeyMultibase;

    /// <summary>The method's key as a <c>did:key</c>.</summary>
    /// <returns>The key, or <see langword="null"/> when the type is not one AT Protocol uses or there is no key material.</returns>
    /// <exception cref="FormatException">The key material is malformed for its type.</exception>
    /// <remarks>
    /// <c>Multikey</c> carries the multikey itself. The legacy <c>EcdsaSecp256r1VerificationKey2019</c> and
    /// <c>EcdsaSecp256k1VerificationKey2019</c> carry a bare SEC1 point, which is validated, compressed and tagged with the curve
    /// the type names.
    /// </remarks>
    public DidKey? ToDidKey()
    {
        if (_key is not null || string.IsNullOrEmpty(PublicKeyMultibase)) return _key;
        return _key = Type switch
        {
            "Multikey" => DidKey.FromMultikey(PublicKeyMultibase),
            "EcdsaSecp256r1VerificationKey2019" => Legacy(KeyCurve.P256),
            "EcdsaSecp256k1VerificationKey2019" => Legacy(KeyCurve.K256),
            _ => null,
        };
    }

    private DidKey Legacy(KeyCurve curve)
    {
        if (PublicKeyMultibase is not ['z', .. var base58]) throw new FormatException("Legacy key material must be base58btc multibase.");
        var point = Data.Base58Btc.Decode(base58);
        return (point.Length == VerificationKey.CompressedPointLength ? VerificationKey.FromCompressedPoint(curve, point) : VerificationKey.FromUncompressedPoint(curve, point)).DidKey;
    }
}

/// <summary>A service of a DID document.</summary>
/// <param name="Id">The id, such as <c>#atproto_pds</c>.</param>
/// <param name="Type">The type, such as <c>AtprotoPersonalDataServer</c>.</param>
/// <param name="Endpoint">The endpoint as published; <see langword="null"/> for a structured (map or list) endpoint, which AT Protocol does not use.</param>
public sealed class DidService(string Id, string Type, string? Endpoint)
{
    /// <summary>The id, such as <c>#atproto_pds</c>.</summary>
    public string Id { get; } = Id;

    /// <summary>The type, such as <c>AtprotoPersonalDataServer</c>.</summary>
    public string Type { get; } = Type;

    /// <summary>The endpoint as published; <see langword="null"/> for a structured endpoint.</summary>
    public string? Endpoint { get; } = Endpoint;

    internal Uri? HttpEndpoint { get; } =
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
}
