using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Identity;

// TXT lookups over a DNS-over-HTTPS JSON API (?name=…&type=TXT, as Google and Cloudflare serve it): the
// _atproto record of a handle and the _lexicon record of an NSID authority.
//
// .NET has no TXT lookup of its own, so both go through the configured endpoint.
internal static class DnsTxtLookup
{
    private const int MaxDnsResponseBytes = 64 * 1024;

    // Queries the TXT records at name, each reassembled from its character-strings.
    //
    // client: The client to send with, under the identity fetch policy.
    //
    // endpoint: The DNS-over-HTTPS endpoint.
    //
    // name: The name to query. Built from an identifier whose syntax has already been validated, so it
    // is safe to put in the query string as is.
    //
    // timeout: The budget for the query, or Timeout.InfiniteTimeSpan.
    //
    // Returns: The records, or null when the endpoint answered with an HTTP error.
    //
    // Throws DidResolutionException: The query failed or the answer is malformed.
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

    // Reassembles a TXT record's text from the JSON API's presentation form, where each character-string
    // is quoted and a long record may be split into several.
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
