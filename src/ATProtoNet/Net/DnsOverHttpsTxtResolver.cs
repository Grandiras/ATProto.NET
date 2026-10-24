using System.Text;
using System.Text.Json;

namespace ATProtoNet.Net;

/// <summary>Looks up DNS TXT records: a handle's <c>_atproto</c> record, an NSID authority's <c>_lexicon</c> record.</summary>
public interface IDnsTxtResolver
{
    /// <summary>The TXT records at <paramref name="name"/>, each reassembled from its character-strings.</summary>
    /// <param name="name">The DNS name.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The records (empty when the name has none), or <see langword="null"/> when the resolver gave no answer.</returns>
    Task<IReadOnlyList<string>?> QueryAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>TXT lookups over a DNS-over-HTTPS JSON API (<c>?name=…&amp;type=TXT</c>, as Google and Cloudflare serve it), through <see cref="UntrustedHttp"/>.</summary>
/// <remarks>The DoH provider sees every name looked up, and so every handle resolved; plug in another <see cref="IDnsTxtResolver"/> to avoid that.</remarks>
public sealed class DnsOverHttpsTxtResolver : IDnsTxtResolver
{
    /// <summary>The default endpoint.</summary>
    public static Uri DefaultEndpoint { get; } = new("https://dns.google/resolve");

    /// <summary>The longest answer accepted, in bytes.</summary>
    public const int MaxResponseBytes = 64 * 1024;

    private static readonly UntrustedRequest Request = new() { MaxBodyBytes = MaxResponseBytes, Accept = "application/dns-json" };

    private readonly UntrustedHttp _http;
    private readonly string _endpoint;

    /// <summary>Creates a resolver.</summary>
    /// <param name="http">The fetch layer, which the caller owns.</param>
    /// <param name="endpoint">The DoH endpoint, <c>https</c> only. Defaults to <see cref="DefaultEndpoint"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is not an absolute <c>https</c> URL without query or fragment, on the default port unless <see cref="UntrustedFetchPolicy.AllowPrivateNetworks"/> is set.</exception>
    public DnsOverHttpsTxtResolver(UntrustedHttp http, Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        endpoint ??= DefaultEndpoint;
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || (!endpoint.IsDefaultPort && !http.Policy.AllowPrivateNetworks) || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            throw new ArgumentException("The DNS-over-HTTPS endpoint must be an absolute https URL without query or fragment, on the default port unless private networks are allowed.", nameof(endpoint));
        _http = http;
        _endpoint = endpoint.AbsoluteUri;
    }

    /// <inheritdoc />
    /// <exception cref="UntrustedFetchException">The query failed, or the answer is not DNS JSON (<see cref="UntrustedFetchError.Malformed"/>).</exception>
    public async Task<IReadOnlyList<string>?> QueryAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var url = new Uri($"{_endpoint}?name={Uri.EscapeDataString(name)}&type=TXT");
        var response = await _http.GetAsync(url, Request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess) return null;
        try
        {
            using var answer = JsonDocument.Parse(response.Body);
            if (!answer.RootElement.TryGetProperty("Answer", out var records)) return [];
            // TXT is type 16; a CNAME along the way is listed too.
            return [.. records.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String
                    && (!r.TryGetProperty("type", out var t) || t.ValueKind == JsonValueKind.Number && t.GetInt32() == 16))
                .Select(r => Join(r.GetProperty("data").GetString()!))];
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new UntrustedFetchException(UntrustedFetchError.Malformed, url, $"The DNS answer for {name} is malformed: {ex.Message}", response.Status, ex);
        }
    }

    // The JSON API's presentation form quotes each character-string; a long record is split into several.
    private static string Join(string data)
    {
        data = data.Trim();
        if (!data.StartsWith('"')) return data;
        var text = new StringBuilder(data.Length);
        var quoted = false;
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c == '"') quoted = !quoted;
            else if (c == '\\' && quoted && i + 1 < data.Length) text.Append(data[++i]);
            else if (quoted) text.Append(c);
        }
        return text.ToString();
    }
}
