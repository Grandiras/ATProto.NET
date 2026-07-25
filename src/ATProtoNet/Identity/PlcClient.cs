using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// Client for interacting with a PLC directory server (https://plc.directory).
/// PLC (Public Ledger of Credentials) is the primary DID method for AT Protocol.
/// <para>
/// All methods are read-only and do not require authentication. The PLC directory
/// is a permissionless public service.
/// </para>
/// </summary>
/// <remarks>
/// See: https://web.plc.directory/spec/v0.1/did-plc
/// </remarks>
public sealed class PlcClient : IDisposable
{
    /// <summary>Default PLC directory URL.</summary>
    public const string DefaultDirectoryUrl = "https://plc.directory";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new PLC client targeting the specified directory server.
    /// </summary>
    /// <param name="directoryUrl">The PLC directory base URL (defaults to <c>https://plc.directory</c>).</param>
    public PlcClient(string directoryUrl = DefaultDirectoryUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(directoryUrl.TrimEnd('/') + "/") };
        _ownsHttpClient = true;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>
    /// Creates a new PLC client using the provided <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Pre-configured HttpClient (caller owns lifecycle).</param>
    public PlcClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>
    /// Resolves a <c>did:plc</c> identifier to a W3C DID Document.
    /// </summary>
    /// <param name="did">The DID to resolve (e.g., <c>did:plc:ewvi7nxzyoun6zhxrhs64oiz</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved DID document.</returns>
    /// <exception cref="PlcException">Thrown when the DID is not found or tombstoned.</exception>
    public async Task<DidDocument> ResolveDidAsync(string did, CancellationToken cancellationToken = default)
    {
        ValidateDid(did);

        using var response = await _httpClient.GetAsync("./" + did, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new PlcException($"DID not found: {did}", PlcErrorKind.NotFound);
        if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            throw new PlcException($"DID has been tombstoned: {did}", PlcErrorKind.Tombstoned);

        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<DidDocument>(_jsonOptions, cancellationToken)
            ?? throw new PlcException("Failed to parse DID document.", PlcErrorKind.ParseError);

        // Defend against a compromised or misbehaving directory returning a forged
        // document for a different DID. The `id` field must echo the requested DID.
        if (!string.Equals(doc.Id, did, StringComparison.Ordinal))
            throw new PlcException(
                $"DID document id '{doc.Id}' does not match requested DID '{did}'.",
                PlcErrorKind.ParseError);

        return doc;
    }

    /// <summary>
    /// Gets the full PLC operation log (chain of signed operations) for a DID.
    /// </summary>
    /// <param name="did">The DID to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered list of PLC operations.</returns>
    public async Task<IReadOnlyList<PlcOperation>> GetOperationLogAsync(
        string did, CancellationToken cancellationToken = default)
    {
        ValidateDid(did);

        using var response = await _httpClient.GetAsync($"./{did}/log", cancellationToken);
        response.EnsureSuccessStatusCode();

        var operations = await response.Content.ReadFromJsonAsync<List<PlcOperation>>(
            _jsonOptions, cancellationToken) ?? [];

        return operations;
    }

    /// <summary>
    /// Gets the PLC audit log for a DID, including CIDs, timestamps, and nullification status.
    /// </summary>
    /// <param name="did">The DID to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit log entries.</returns>
    public async Task<IReadOnlyList<PlcAuditEntry>> GetAuditLogAsync(
        string did, CancellationToken cancellationToken = default)
    {
        ValidateDid(did);

        using var response = await _httpClient.GetAsync($"./{did}/log/audit", cancellationToken);
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<PlcAuditEntry>>(
            _jsonOptions, cancellationToken) ?? [];

        return entries;
    }

    /// <summary>
    /// Gets the latest PLC operation for a DID.
    /// </summary>
    /// <param name="did">The DID to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent PLC operation.</returns>
    public async Task<PlcOperation> GetLastOperationAsync(
        string did, CancellationToken cancellationToken = default)
    {
        ValidateDid(did);

        using var response = await _httpClient.GetAsync($"./{did}/log/last", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new PlcException($"DID not found: {did}", PlcErrorKind.NotFound);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PlcOperation>(
            _jsonOptions, cancellationToken)
            ?? throw new PlcException("Failed to parse PLC operation.", PlcErrorKind.ParseError);
    }

    /// <summary>
    /// Gets the current PLC data for a DID (the data from the latest valid operation).
    /// </summary>
    /// <param name="did">The DID to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current PLC data.</returns>
    public async Task<PlcData> GetPlcDataAsync(
        string did, CancellationToken cancellationToken = default)
    {
        ValidateDid(did);

        using var response = await _httpClient.GetAsync($"./{did}/data", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new PlcException($"DID not found: {did}", PlcErrorKind.NotFound);
        if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            throw new PlcException($"DID has been tombstoned: {did}", PlcErrorKind.Tombstoned);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PlcData>(_jsonOptions, cancellationToken)
            ?? throw new PlcException("Failed to parse PLC data.", PlcErrorKind.ParseError);
    }

    /// <summary>
    /// Checks whether the PLC directory server is reachable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the server responds successfully.</returns>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("_health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateDid(string did)
    {
        if (string.IsNullOrWhiteSpace(did))
            throw new ArgumentException("DID cannot be null or empty.", nameof(did));
        if (!did.StartsWith("did:plc:", StringComparison.Ordinal))
            throw new ArgumentException("Only did:plc identifiers are supported.", nameof(did));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

// ── Models ───────────────────────────────────────────────────

/// <summary>A W3C DID Document as returned by PLC directory resolution.</summary>
public sealed class DidDocument
{
    /// <summary>The DID identifier (e.g., <c>did:plc:ewvi7nxzyoun6zhxrhs64oiz</c>).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Alternate identifiers, typically including the AT Protocol handle as <c>at://handle</c>.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string> AlsoKnownAs { get; set; } = [];

    /// <summary>Verification methods (public keys) associated with this DID.</summary>
    [JsonPropertyName("verificationMethod")]
    public List<VerificationMethod> VerificationMethod { get; set; } = [];

    /// <summary>Service endpoints associated with this DID.</summary>
    [JsonPropertyName("service")]
    public List<ServiceEndpoint> Service { get; set; } = [];

    /// <summary>Extracts the AT Protocol handle from <see cref="AlsoKnownAs"/> entries.</summary>
    /// <returns>The handle, or <c>null</c> if not found.</returns>
    public string? GetHandle()
    {
        var atUri = AlsoKnownAs.FirstOrDefault(a =>
            a.StartsWith("at://", StringComparison.OrdinalIgnoreCase));
        return atUri?["at://".Length..];
    }

    /// <summary>Gets the PDS service endpoint URL.</summary>
    /// <returns>The PDS URL, or <c>null</c> if not found.</returns>
    public string? GetPdsEndpoint()
    {
        return Service.FirstOrDefault(s =>
            (s.Id == "#atproto_pds" || s.Id == $"{Id}#atproto_pds") &&
            s.Type == "AtprotoPersonalDataServer")?.Endpoint;
    }
}

/// <summary>A verification method entry in a DID Document.</summary>
public sealed class VerificationMethod
{
    /// <summary>The verification method identifier (e.g., <c>did:plc:...#atproto</c>).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The type (e.g., <c>Multikey</c>).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>The controller DID.</summary>
    [JsonPropertyName("controller")]
    public string Controller { get; set; } = "";

    /// <summary>The public key in multibase encoding (e.g., <c>z...</c> for base58btc).</summary>
    [JsonPropertyName("publicKeyMultibase")]
    public string? PublicKeyMultibase { get; set; }
}

/// <summary>A service endpoint entry in a DID Document.</summary>
public sealed class ServiceEndpoint
{
    /// <summary>The service identifier (e.g., <c>#atproto_pds</c>).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The service type (e.g., <c>AtprotoPersonalDataServer</c>).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>The endpoint URL.</summary>
    [JsonPropertyName("serviceEndpoint")]
    public string Endpoint { get; set; } = "";
}

/// <summary>A PLC operation — a signed state transition for a DID.</summary>
public sealed class PlcOperation
{
    /// <summary>Operation type (e.g., <c>plc_operation</c> or <c>plc_tombstone</c>).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>Service endpoints map.</summary>
    [JsonPropertyName("services")]
    public Dictionary<string, PlcOperationService>? Services { get; set; }

    /// <summary>Alternate identifiers.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; }

    /// <summary>Rotation keys in <c>did:key</c> format.</summary>
    [JsonPropertyName("rotationKeys")]
    public List<string>? RotationKeys { get; set; }

    /// <summary>Verification methods map.</summary>
    [JsonPropertyName("verificationMethods")]
    public Dictionary<string, string>? VerificationMethods { get; set; }

    /// <summary>CID reference to the previous operation, or <c>null</c> for genesis.</summary>
    [JsonPropertyName("prev")]
    public string? Prev { get; set; }

    /// <summary>Cryptographic signature (base64).</summary>
    [JsonPropertyName("sig")]
    public string? Sig { get; set; }
}

/// <summary>A service entry within a PLC operation.</summary>
public sealed class PlcOperationService
{
    /// <summary>The service type.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>The service endpoint URL.</summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "";
}

/// <summary>An audit log entry from the PLC directory.</summary>
public sealed class PlcAuditEntry
{
    /// <summary>The DID this entry belongs to.</summary>
    [JsonPropertyName("did")]
    public string Did { get; set; } = "";

    /// <summary>The PLC operation.</summary>
    [JsonPropertyName("operation")]
    public PlcOperation Operation { get; set; } = new();

    /// <summary>The CID (content identifier) of this operation.</summary>
    [JsonPropertyName("cid")]
    public string Cid { get; set; } = "";

    /// <summary>Whether this operation has been nullified (superseded by a later operation).</summary>
    [JsonPropertyName("nullified")]
    public bool Nullified { get; set; }

    /// <summary>When this operation was indexed by the directory.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Current PLC data for a DID (latest resolved state).</summary>
public sealed class PlcData
{
    /// <summary>Service endpoints map.</summary>
    [JsonPropertyName("services")]
    public Dictionary<string, PlcOperationService>? Services { get; set; }

    /// <summary>Alternate identifiers.</summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; }

    /// <summary>Rotation keys in <c>did:key</c> format.</summary>
    [JsonPropertyName("rotationKeys")]
    public List<string>? RotationKeys { get; set; }

    /// <summary>Verification methods map.</summary>
    [JsonPropertyName("verificationMethods")]
    public Dictionary<string, string>? VerificationMethods { get; set; }
}

// ── Exceptions ───────────────────────────────────────────────

/// <summary>Categorizes PLC directory errors.</summary>
public enum PlcErrorKind
{
    /// <summary>The DID was not found.</summary>
    NotFound,

    /// <summary>The DID has been tombstoned (deleted).</summary>
    Tombstoned,

    /// <summary>The response could not be parsed.</summary>
    ParseError,
}

/// <summary>Exception thrown by <see cref="PlcClient"/> operations.</summary>
public sealed class PlcException : Exception
{
    /// <summary>The kind of error.</summary>
    public PlcErrorKind Kind { get; }

    /// <summary>Creates a new PLC exception.</summary>
    public PlcException(string message, PlcErrorKind kind) : base(message)
    {
        Kind = kind;
    }
}
