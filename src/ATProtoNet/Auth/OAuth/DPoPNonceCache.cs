using ATProtoNet.Caching;

namespace ATProtoNet.Auth.OAuth;

// The latest DPoP nonce each server handed out, by origin (scheme://host[:port]), shared by everything
// in the process that sends DPoP proofs: every Http.XrpcClient (so the per-request clients of a server
// application and space readers too) and every OAuthClient.
//
// A nonce is issued by a server, not bound to a key or a session (RFC 9449 section 8), so a client
// created a moment ago can present the nonce another client just received instead of paying a
// use_dpop_nonce round trip for its first request. The reference client keys its cache the same way and
// shares it between the authorization server and the resource server.
//
// A stale nonce costs nothing but the round trip it was meant to save: the server answers with a fresh
// one and the request is retried once. So the cache is small and forgetful: past Capacity origins, the
// least recently used go, and a nonce longer than MaxNonceLength characters is not kept, since the
// servers it comes from may be anyone's.
internal sealed class DPoPNonceCache
{
    // The most origins remembered.
    internal const int Capacity = 1024;

    // The longest nonce remembered. The reference servers issue nonces of about 40 characters.
    internal const int MaxNonceLength = 1024;

    private readonly LruCache<string, string> _nonces = new(Capacity, StringComparer.Ordinal);

    // The cache every client in the process shares unless given another.
    internal static DPoPNonceCache Shared { get; } = new();

    // The number of origins remembered.
    internal int Count => _nonces.Count;

    // The latest nonce the origin of url handed out, if any.
    internal string? Get(Uri url) => _nonces.TryGetValue(Origin(url), out var nonce) ? nonce : null;

    // Remembers the nonce in a response's DPoP-Nonce header for the origin of url, and returns it; null
    // when the response carries none worth keeping.
    internal string? Observe(Uri url, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("DPoP-Nonce", out var values) ||
            values.FirstOrDefault() is not { Length: > 0 and <= MaxNonceLength } nonce)
            return null;

        Set(url, nonce);
        return nonce;
    }

    // Remembers nonce for the origin of url.
    internal void Set(Uri url, string nonce)
    {
        if (nonce.Length is 0 or > MaxNonceLength)
            return;

        _nonces.Set(Origin(url), nonce);
    }

    // The origin a URL's nonce is kept under: lower-cased scheme and host, and a non-default port.
    internal static string Origin(Uri url) => url.GetLeftPart(UriPartial.Authority);
}
