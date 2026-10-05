using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Crypto;

namespace ATProtoNet.Identity;

/// <summary>A W3C DID document, as a <c>did:plc</c> directory or a <c>did:web</c> host serves it.</summary>
/// <remarks>
/// <para>Immutable: resolvers cache and share documents, so no consumer may change one another
/// consumer is reading.</para>
/// <para>AT Protocol reads three things from a document: the handle it claims
/// (<see cref="GetHandle"/>), the repo signing key (<see cref="GetSigningKey"/>) and the PDS
/// endpoint (<see cref="GetPdsEndpoint"/>). Other entries are reached by fragment through
/// <see cref="GetVerificationKey"/> and <see cref="GetServiceEndpoint"/>.</para>
/// </remarks>
public sealed class DidDocument
{
    /// <summary>The service fragment of an account's PDS.</summary>
    public const string PdsServiceId = "#atproto_pds";

    /// <summary>The service type of an account's PDS.</summary>
    public const string PdsServiceType = "AtprotoPersonalDataServer";

    /// <summary>The verification method fragment of an account's repo signing key.</summary>
    public const string SigningKeyId = "#atproto";

    /// <summary>The verification method fragment of a labeler's label signing key.</summary>
    public const string LabelKeyId = "#atproto_label";

    /// <summary>The JSON-LD context. Omitted when serializing unless set — required when <em>publishing</em> a document (e.g. a <c>did:web</c> <c>/.well-known/did.json</c>), ignorable when consuming one. A single string is read as a one-entry list, and inline context objects are skipped.</summary>
    [JsonPropertyName("@context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(StringListConverter))]
    public IReadOnlyList<string>? Context { get; init; }

    /// <summary>The DID this document describes.</summary>
    [JsonPropertyName("id")]
    public required Did Id { get; init; }

    /// <summary>Alternate identifiers, including the account's handle as <c>at://handle</c>. A JSON <c>null</c> reads as an empty list, and a <c>null</c> entry fails deserialization.</summary>
    [JsonPropertyName("alsoKnownAs")]
    [JsonConverter(typeof(NonNullListConverter<string>))]
    public IReadOnlyList<string> AlsoKnownAs
    {
        get => _alsoKnownAs;
        init => _alsoKnownAs = value ?? [];
    }

    /// <summary>The public keys published for this DID. A JSON <c>null</c> reads as an empty list, and a <c>null</c> entry fails deserialization.</summary>
    [JsonPropertyName("verificationMethod")]
    [JsonConverter(typeof(NonNullListConverter<VerificationMethod>))]
    public IReadOnlyList<VerificationMethod> VerificationMethod
    {
        get => _verificationMethod;
        init => _verificationMethod = value ?? [];
    }

    /// <summary>The services published for this DID. A JSON <c>null</c> reads as an empty list, and a <c>null</c> entry fails deserialization.</summary>
    [JsonPropertyName("service")]
    [JsonConverter(typeof(NonNullListConverter<DidDocumentService>))]
    public IReadOnlyList<DidDocumentService> Service
    {
        get => _service;
        init => _service = value ?? [];
    }

    // A JSON null would otherwise overwrite an initializer and leave every lookup to throw.
    private readonly IReadOnlyList<string> _alsoKnownAs = [];
    private readonly IReadOnlyList<VerificationMethod> _verificationMethod = [];
    private readonly IReadOnlyList<DidDocumentService> _service = [];

    /// <summary>The handle this document claims: the first <c>at://</c> entry of <see cref="AlsoKnownAs"/>, when it is a syntactically valid handle.</summary>
    /// <returns>
    /// The claimed handle, or <see langword="null"/> when the document claims none or its first
    /// <c>at://</c> entry is not a valid handle.
    /// </returns>
    /// <remarks>
    /// <para>As in the reference implementation, only the first <c>at://</c> entry counts (the
    /// prefix compared case-sensitively): an invalid one is no handle, rather than a reason to
    /// look further down the list.</para>
    /// <para>A claim is not a verified handle: anyone controlling the document can claim any
    /// name. Only the handle resolving back to this DID makes it one;
    /// <see cref="IIdentityResolver"/> checks both directions.</para>
    /// </remarks>
    public Handle? GetHandle()
    {
        // Indexed: foreach over the interface would box the list's enumerator on every call.
        for (var i = 0; i < AlsoKnownAs.Count; i++)
        {
            if (AlsoKnownAs[i] is not { } entry || !entry.StartsWith("at://", StringComparison.Ordinal))
                continue;

            // An @ prefix, which Handle.TryCreate would strip, is not part of a handle.
            return !entry.StartsWith("at://@", StringComparison.Ordinal) && Handle.TryCreate(entry.AsSpan(5), null, out var handle)
                ? handle
                : null;
        }

        return null;
    }

    /// <summary>The account's repo signing key (<c>#atproto</c>) as a <c>did:key</c>.</summary>
    /// <returns>
    /// The signing key, or <see langword="null"/> when the document publishes none it can be read
    /// from, as <see cref="GetVerificationKey"/> reads it.
    /// </returns>
    public string? GetSigningKey() => GetVerificationKey(SigningKeyId);

    /// <summary>A verification method's public key as a <c>did:key</c>, whichever of the verification-method types AT Protocol uses the document publishes it under.</summary>
    /// <param name="fragment">
    /// The verification method fragment, with or without its leading <c>#</c> (e.g. <c>#atproto</c>).
    /// Both the bare fragment and the DID-qualified form are matched.
    /// </param>
    /// <returns>
    /// The key as a <c>did:key</c> string, or <see langword="null"/> when no entry with that id is
    /// published, its type is not one this SDK understands, or its key material does not decode.
    /// </returns>
    /// <remarks>
    /// A <c>Multikey</c> value is already a <c>did:key</c> and is passed through undecoded; a
    /// verification against it fails if it is not a key. <see cref="TryGetVerificationKey"/>
    /// decodes it, and tells an absent entry from an unusable one.
    /// </remarks>
    public string? GetVerificationKey(string fragment)
    {
        var method = FindVerificationMethod(fragment);
        try
        {
            return method?.ToDidKey();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Looks a verification method up strictly: tells an entry that is absent from one that is published but unusable.</summary>
    /// <param name="fragment">
    /// The verification method fragment, with or without its leading <c>#</c>. Both the bare
    /// fragment and the DID-qualified form are matched.
    /// </param>
    /// <param name="didKey">The key as a <c>did:key</c>, when the result is <see cref="DidDocumentEntryStatus.Found"/>.</param>
    /// <returns>
    /// <see cref="DidDocumentEntryStatus.Absent"/> when no entry has that id, and
    /// <see cref="DidDocumentEntryStatus.Malformed"/> when one does but its type is not one this SDK
    /// understands or its key material is missing or does not decode to a public key.
    /// </returns>
    /// <remarks>
    /// <see cref="GetVerificationKey"/> reads an unusable entry as absent, which suits a lookup
    /// with a fallback. Where a published entry must be used or refused — a space authority's
    /// <c>#atproto_space</c> key, say — this is the lookup that keeps a broken entry from quietly
    /// falling through to the fallback.
    /// </remarks>
    public DidDocumentEntryStatus TryGetVerificationKey(string fragment, out string? didKey)
    {
        didKey = null;
        if (FindVerificationMethod(fragment) is not { } method)
            return DidDocumentEntryStatus.Absent;

        try
        {
            if (method.ToDidKey() is not { } key)
                return DidDocumentEntryStatus.Malformed;

            if (!method.KeyChecked)
            {
                AtProtoCrypto.ParseDidKey(key, out _);
                method.KeyChecked = true;
            }

            didKey = key;
            return DidDocumentEntryStatus.Found;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return DidDocumentEntryStatus.Malformed;
        }
    }

    /// <summary>The endpoint of a service entry, found by fragment and optionally by type.</summary>
    /// <param name="fragment">
    /// The service fragment, with or without its leading <c>#</c> (e.g. <c>#atproto_pds</c>). Both
    /// the bare fragment and the DID-qualified form are matched.
    /// </param>
    /// <param name="type">The service type the entry must carry, or <see langword="null"/> for any.</param>
    /// <returns>
    /// The endpoint, or <see langword="null"/> when no such entry is published or its endpoint is
    /// not an absolute <c>http</c>/<c>https</c> URL.
    /// </returns>
    public Uri? GetServiceEndpoint(string fragment, string? type = null) =>
        TryGetServiceEndpoint(fragment, type, out var endpoint) == DidDocumentEntryStatus.Found ? endpoint : null;

    /// <summary>Looks a service up strictly: tells an entry that is absent from one that is published but unusable.</summary>
    /// <param name="fragment">
    /// The service fragment, with or without its leading <c>#</c>. Both the bare fragment and the
    /// DID-qualified form are matched.
    /// </param>
    /// <param name="type">The service type the entry must carry, or <see langword="null"/> for any.</param>
    /// <param name="endpoint">The endpoint, when the result is <see cref="DidDocumentEntryStatus.Found"/>.</param>
    /// <returns>
    /// <see cref="DidDocumentEntryStatus.Absent"/> when no entry has that id, and
    /// <see cref="DidDocumentEntryStatus.Malformed"/> when the first one that does carries another
    /// type, or an endpoint that is not an absolute <c>http</c>/<c>https</c> URL (a structured
    /// endpoint included).
    /// </returns>
    /// <remarks>
    /// <see cref="GetServiceEndpoint"/> reads an unusable entry as absent, which suits a lookup
    /// with a fallback. Where a published entry must be used or refused — a space authority's
    /// <c>#atproto_space_host</c>, say — this is the lookup that keeps a broken entry from quietly
    /// falling through to the fallback.
    /// </remarks>
    public DidDocumentEntryStatus TryGetServiceEndpoint(string fragment, string? type, out Uri? endpoint)
    {
        fragment = NormalizeFragment(fragment);
        endpoint = null;

        // The first entry by id decides, as in the reference implementation: a later entry with
        // the same id and the expected type does not stand in for a first one that is wrong.
        for (var i = 0; i < Service.Count; i++)
        {
            if (Service[i] is not { } service || !IsFragment(service.Id, fragment))
                continue;
            if (type is not null && !string.Equals(service.Type, type, StringComparison.Ordinal))
                return DidDocumentEntryStatus.Malformed;

            endpoint = service.HttpEndpoint;
            return endpoint is null ? DidDocumentEntryStatus.Malformed : DidDocumentEntryStatus.Found;
        }

        return DidDocumentEntryStatus.Absent;
    }

    /// <summary>The account's PDS endpoint: the <c>#atproto_pds</c> entry of type <c>AtprotoPersonalDataServer</c>.</summary>
    /// <returns>The PDS URL, or <see langword="null"/> when the document publishes none.</returns>
    public Uri? GetPdsEndpoint() => GetServiceEndpoint(PdsServiceId, PdsServiceType);

    private VerificationMethod? FindVerificationMethod(string fragment)
    {
        fragment = NormalizeFragment(fragment);

        for (var i = 0; i < VerificationMethod.Count; i++)
        {
            if (VerificationMethod[i] is { } method && IsFragment(method.Id, fragment))
                return method;
        }

        return null;
    }

    private static string NormalizeFragment(string fragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        return fragment.StartsWith('#') ? fragment : "#" + fragment;
    }

    private bool IsFragment(string? id, string fragment) =>
        id is not null &&
        (string.Equals(id, fragment, StringComparison.Ordinal) ||
         (id.Length == Id.Value.Length + fragment.Length &&
          id.StartsWith(Id.Value, StringComparison.Ordinal) &&
          id.EndsWith(fragment, StringComparison.Ordinal)));

    // Reads a list that DID Core requires to hold values: null reads as an empty list, and a null entry
    // is malformed.
    private sealed class NonNullListConverter<T> : JsonConverter<IReadOnlyList<T>>
    {
        public override bool HandleNull => true;

        public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return [];

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected an array, not {reader.TokenType}.");

            var values = new List<T>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    throw new JsonException("A DID document list may not contain null.");

                values.Add(JsonSerializer.Deserialize<T>(ref reader, options)!);
            }

            return values;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var entry in value)
                JsonSerializer.Serialize(writer, entry, options);
            writer.WriteEndArray();
        }
    }

    // Reads @context, which JSON-LD allows as a string, an array, or an array mixing strings with inline
    // context objects.
    private sealed class StringListConverter : JsonConverter<IReadOnlyList<string>?>
    {
        public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // A null never reaches the converter: the serializer reads and omits it itself.
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return [reader.GetString()!];
                case JsonTokenType.StartArray:
                    var values = new List<string>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                            values.Add(reader.GetString()!);
                        else
                            reader.Skip();
                    }
                    return values;
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string>? value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var entry in value!)
                writer.WriteStringValue(entry);
            writer.WriteEndArray();
        }
    }
}

/// <summary>The outcome of a strict DID document lookup.</summary>
public enum DidDocumentEntryStatus
{
    /// <summary>The entry is published and usable.</summary>
    Found,

    /// <summary>No entry with that id (and type) is published.</summary>
    Absent,

    /// <summary>The entry is published, but its value cannot be used.</summary>
    Malformed,
}

/// <summary>A verification method entry in a DID document.</summary>
public sealed class VerificationMethod
{
    /// <summary>The verification method identifier (e.g. <c>did:plc:…#atproto</c> or <c>#atproto</c>).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The type (e.g. <c>Multikey</c>).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The controller DID.</summary>
    [JsonPropertyName("controller")]
    public string? Controller { get; init; }

    /// <summary>The public key in multibase encoding (e.g. <c>z…</c> for base58btc).</summary>
    [JsonPropertyName("publicKeyMultibase")]
    public string? PublicKeyMultibase { get; init; }

    /// <summary>Converts this method's key material to a <c>did:key</c>.</summary>
    /// <returns>
    /// The key as a <c>did:key</c> string, or <see langword="null"/> when the entry carries no key
    /// material or its <see cref="Type"/> is not one this SDK understands.
    /// </returns>
    /// <exception cref="FormatException">Thrown when the key material is malformed for its type.</exception>
    /// <remarks>
    /// <para>Three types are accepted, matching the reference implementation. A
    /// <c>Multikey</c>'s <c>publicKeyMultibase</c> is already the did:key encoding —
    /// base58btc over multicodec-tagged compressed key bytes — so it passes through
    /// unchanged. The legacy <c>EcdsaSecp256k1VerificationKey2019</c> and
    /// <c>EcdsaSecp256r1VerificationKey2019</c> forms instead carry a bare uncompressed
    /// point (<c>0x04 || X || Y</c>) with no multicodec prefix, so the point is compressed
    /// and re-tagged with the curve the type names.</para>
    /// <para>plc.directory serves <c>Multikey</c> today, but older PLC releases and
    /// hand-written <c>did:web</c> documents may publish the legacy forms.</para>
    /// </remarks>
    public string? ToDidKey() => _didKey ??= string.IsNullOrEmpty(PublicKeyMultibase) ? null : Type switch
    {
        "Multikey" => $"did:key:{PublicKeyMultibase}",
        "EcdsaSecp256k1VerificationKey2019" => FormatLegacyDidKey(PublicKeyMultibase, KeyCurve.K256),
        "EcdsaSecp256r1VerificationKey2019" => FormatLegacyDidKey(PublicKeyMultibase, KeyCurve.P256),
        _ => null,
    };

    // Documents are shared and read on every verification, so the did:key is built once, and a key the
    // strict lookup has decoded is not decoded again.
    private string? _didKey;

    internal bool KeyChecked { get; set; }

    private static string FormatLegacyDidKey(string publicKeyMultibase, KeyCurve curve)
        => AtProtoCrypto.FormatDidKey(AtProtoCrypto.MultibaseToBytes(publicKeyMultibase), curve);
}

/// <summary>A service entry in a DID document.</summary>
public sealed class DidDocumentService
{
    /// <summary>The service identifier (e.g. <c>#atproto_pds</c>).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The service type (e.g. <c>AtprotoPersonalDataServer</c>).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The endpoint URL as published. <see langword="null"/> when the document carries a structured endpoint (a map or a list, which DID Core allows and AT Protocol does not use).</summary>
    /// <remarks>Read it through <see cref="DidDocument.GetServiceEndpoint"/>, which validates it.</remarks>
    [JsonPropertyName("serviceEndpoint")]
    [JsonConverter(typeof(StringOrNullConverter))]
    public string? Endpoint { get; init; }

    // The endpoint when it is an absolute http(s) URL, parsed once.
    internal Uri? HttpEndpoint => _endpoint ??=
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    private Uri? _endpoint;

    private sealed class StringOrNullConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString();

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
