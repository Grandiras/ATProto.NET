using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ATProtoNet.Identity;

/// <summary>
/// Resolves <c>did:web</c> identifiers to DID Documents by fetching <c>https://&lt;domain&gt;/.well-known/did.json</c>.
/// <para>
/// Only hostname-level <c>did:web</c> DIDs are supported in the AT Protocol.
/// Path-based DIDs (e.g., <c>did:web:example.com:path:to:resource</c>) are rejected.
/// </para>
/// </summary>
/// <remarks>
/// See: <see href="https://atproto.com/specs/did">AT Protocol DID specification</see>
/// </remarks>
public sealed partial class DidWebResolver : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    // Matches a simple domain name (with optional port for localhost only)
    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9\-.]*[a-zA-Z0-9])?(%3[Aa]\d+)?$", RegexOptions.Compiled)]
    private static partial Regex DomainPattern();

    /// <summary>
    /// Creates a new <c>did:web</c> resolver with its own <see cref="HttpClient"/>.
    /// </summary>
    public DidWebResolver()
    {
        _httpClient = new HttpClient();
        _ownsHttpClient = true;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>
    /// Creates a new <c>did:web</c> resolver using the provided <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Pre-configured HttpClient (caller owns lifecycle).</param>
    public DidWebResolver(HttpClient httpClient)
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
    /// Resolves a <c>did:web</c> identifier to a DID Document.
    /// </summary>
    /// <param name="did">The DID to resolve (e.g., <c>did:web:example.com</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved DID document.</returns>
    /// <exception cref="DidWebException">Thrown when resolution fails.</exception>
    public async Task<DidDocument> ResolveDidAsync(string did, CancellationToken cancellationToken = default)
    {
        var url = BuildResolutionUrl(did);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DidWebException($"Failed to connect to {url}: {ex.Message}", DidWebErrorKind.NetworkError, ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DidWebException($"DID document not found at {url}", DidWebErrorKind.NotFound);

            if (!response.IsSuccessStatusCode)
                throw new DidWebException(
                    $"HTTP {(int)response.StatusCode} from {url}",
                    DidWebErrorKind.HttpError);

            DidDocument doc;
            try
            {
                doc = await response.Content.ReadFromJsonAsync<DidDocument>(_jsonOptions, cancellationToken)
                    ?? throw new DidWebException("Empty DID document response.", DidWebErrorKind.ParseError);
            }
            catch (JsonException ex)
            {
                throw new DidWebException("Failed to parse DID document JSON.", DidWebErrorKind.ParseError, ex);
            }

            // Validate that the document ID matches the expected DID
            if (!string.Equals(doc.Id, did, StringComparison.Ordinal))
                throw new DidWebException(
                    $"DID document ID mismatch: expected '{did}', got '{doc.Id}'",
                    DidWebErrorKind.ValidationError);

            return doc;
        }
    }

    /// <summary>
    /// Builds the HTTPS URL for fetching a <c>did:web</c> DID document.
    /// </summary>
    internal static string BuildResolutionUrl(string did)
    {
        if (string.IsNullOrWhiteSpace(did))
            throw new ArgumentException("DID cannot be null or empty.", nameof(did));
        if (!did.StartsWith("did:web:", StringComparison.Ordinal))
            throw new ArgumentException("Only did:web identifiers are supported.", nameof(did));

        var methodSpecificId = did["did:web:".Length..];

        if (string.IsNullOrEmpty(methodSpecificId))
            throw new DidWebException("did:web identifier has empty domain.", DidWebErrorKind.InvalidDid);

        // AT Protocol only supports hostname-level did:web (no path segments)
        if (methodSpecificId.Contains(':'))
            throw new DidWebException(
                "Path-based did:web identifiers are not supported in AT Protocol. Only hostname-level DIDs are allowed.",
                DidWebErrorKind.InvalidDid);

        // Decode percent-encoded port separator (%3A -> :)
        var domain = Uri.UnescapeDataString(methodSpecificId);

        // Validate domain format
        if (!DomainPattern().IsMatch(methodSpecificId))
            throw new DidWebException($"Invalid domain in did:web: '{domain}'", DidWebErrorKind.InvalidDid);

        // Block IP addresses (SSRF prevention)
        if (IPAddress.TryParse(domain.Split(':')[0], out _))
            throw new DidWebException(
                "IP addresses are not allowed in did:web identifiers. Use a domain name.",
                DidWebErrorKind.InvalidDid);

        // Block IPv6 bracketed addresses
        if (domain.StartsWith('['))
            throw new DidWebException(
                "IPv6 addresses are not allowed in did:web identifiers.",
                DidWebErrorKind.InvalidDid);

        // Allow localhost only for development, but require HTTPS for everything else
        var host = domain.Split(':')[0];
        var scheme = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "http" : "https";

        return $"{scheme}://{domain}/.well-known/did.json";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}

/// <summary>
/// Unified DID resolver that dispatches to the correct resolver based on DID method.
/// Supports <c>did:plc</c> and <c>did:web</c>.
/// </summary>
public sealed class DidResolver : IDisposable
{
    private readonly PlcClient _plcClient;
    private readonly DidWebResolver _webResolver;
    private readonly bool _ownsClients;

    /// <summary>
    /// Creates a new unified DID resolver with default settings.
    /// </summary>
    public DidResolver()
    {
        _plcClient = new PlcClient();
        _webResolver = new DidWebResolver();
        _ownsClients = true;
    }

    /// <summary>
    /// Creates a new unified DID resolver with pre-configured resolvers.
    /// </summary>
    /// <param name="plcClient">The PLC client for <c>did:plc</c> resolution.</param>
    /// <param name="webResolver">The <c>did:web</c> resolver.</param>
    public DidResolver(PlcClient plcClient, DidWebResolver webResolver)
    {
        _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
        _webResolver = webResolver ?? throw new ArgumentNullException(nameof(webResolver));
        _ownsClients = false;
    }

    /// <summary>
    /// Resolves any supported DID to a DID Document, dispatching to the correct resolver.
    /// </summary>
    /// <param name="did">The DID to resolve (e.g., <c>did:plc:...</c> or <c>did:web:...</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved DID document.</returns>
    /// <exception cref="ArgumentException">Thrown for unsupported DID methods.</exception>
    public Task<DidDocument> ResolveDidAsync(string did, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did))
            throw new ArgumentException("DID cannot be null or empty.", nameof(did));

        if (did.StartsWith("did:plc:", StringComparison.Ordinal))
            return _plcClient.ResolveDidAsync(did, cancellationToken);

        if (did.StartsWith("did:web:", StringComparison.Ordinal))
            return _webResolver.ResolveDidAsync(did, cancellationToken);

        throw new ArgumentException($"Unsupported DID method: '{did}'. Only did:plc and did:web are supported.", nameof(did));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClients)
        {
            _plcClient.Dispose();
            _webResolver.Dispose();
        }
    }
}

// ── Exceptions ───────────────────────────────────────────────

/// <summary>Categorizes did:web resolution errors.</summary>
public enum DidWebErrorKind
{
    /// <summary>The DID format is invalid.</summary>
    InvalidDid,

    /// <summary>The DID document was not found (HTTP 404).</summary>
    NotFound,

    /// <summary>An HTTP error occurred during resolution.</summary>
    HttpError,

    /// <summary>A network error occurred during resolution.</summary>
    NetworkError,

    /// <summary>The DID document could not be parsed.</summary>
    ParseError,

    /// <summary>The DID document failed validation.</summary>
    ValidationError,
}

/// <summary>Exception thrown by <see cref="DidWebResolver"/> operations.</summary>
public sealed class DidWebException : Exception
{
    /// <summary>The kind of error.</summary>
    public DidWebErrorKind Kind { get; }

    /// <summary>Creates a new did:web exception.</summary>
    public DidWebException(string message, DidWebErrorKind kind) : base(message)
    {
        Kind = kind;
    }

    /// <summary>Creates a new did:web exception with an inner exception.</summary>
    public DidWebException(string message, DidWebErrorKind kind, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
