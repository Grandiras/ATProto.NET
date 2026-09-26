using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// TXT lookups over a DNS-over-HTTPS JSON API (<c>?name=…&amp;type=TXT</c>, as Google and
/// Cloudflare serve it): the <c>_atproto</c> record of a handle and the <c>_lexicon</c> record of
/// an NSID authority.
/// </summary>
/// <remarks>.NET has no TXT lookup of its own, so both go through the configured endpoint.</remarks>
internal static class DnsTxtLookup
{
    private const int MaxDnsResponseBytes = 64 * 1024;

    /// <summary>
    /// Queries the TXT records at <paramref name="name"/>, each reassembled from its
    /// character-strings.
    /// </summary>
    /// <param name="client">The client to send with, under the identity fetch policy.</param>
    /// <param name="endpoint">The DNS-over-HTTPS endpoint.</param>
    /// <param name="name">
    /// The name to query. Built from an identifier whose syntax has already been validated, so it
    /// is safe to put in the query string as is.
    /// </param>
    /// <param name="timeout">The budget for the query, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The records, or <see langword="null"/> when the endpoint answered with an HTTP error.</returns>
    /// <exception cref="DidResolutionException">The query failed or the answer is malformed.</exception>
    internal static async Task<IReadOnlyList<string>?> QueryAsync(
        HttpClient client, Uri endpoint, string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var url = new Uri($"{endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/')}?name={name}&type=TXT");

        var result = await IdentityFetch.GetAsync(
            client, url, "application/dns-json", MaxDnsResponseBytes, timeout, did: null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
            return null;

        DnsJsonResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<DnsJsonResponse>(result.Body.Span);
        }
        catch (JsonException ex)
        {
            throw new DidResolutionException(
                $"The DNS answer for {name} is malformed: {ex.Message}", DidResolutionErrorKind.InvalidDocument, innerException: ex);
        }

        var records = new List<string>();
        foreach (var answer in response?.Answer ?? [])
        {
            // TXT type is 16; a CNAME along the way is also listed.
            if (answer is null || answer.Type is not (null or 16) || answer.Data is null)
                continue;

            records.Add(JoinCharacterStrings(answer.Data));
        }

        return records;
    }

    /// <summary>
    /// Reassembles a TXT record's text from the JSON API's presentation form, where each
    /// character-string is quoted and a long record may be split into several.
    /// </summary>
    private static string JoinCharacterStrings(string data)
    {
        data = data.Trim();
        if (!data.StartsWith('"'))
            return data;

        var text = new StringBuilder(data.Length);
        var inQuotes = false;
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == '\\' && inQuotes && i + 1 < data.Length)
                text.Append(data[++i]);
            else if (inQuotes)
                text.Append(c);
        }

        return text.ToString();
    }

    private sealed class DnsJsonResponse
    {
        [JsonPropertyName("Answer")]
        public List<DnsJsonAnswer?>? Answer { get; set; }
    }

    private sealed class DnsJsonAnswer
    {
        [JsonPropertyName("type")]
        public int? Type { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }
}
