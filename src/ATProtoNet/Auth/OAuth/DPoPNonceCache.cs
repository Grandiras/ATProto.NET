using System.Collections.Concurrent;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// The latest DPoP nonce each server handed out, by origin (<c>scheme://host[:port]</c>), shared
/// by everything in the process that sends DPoP proofs: every <see cref="Http.XrpcClient"/> (so
/// the per-request clients of a server application and space readers too) and every
/// <see cref="OAuthClient"/>.
/// </summary>
/// <remarks>
/// <para>A nonce is issued by a server, not bound to a key or a session (RFC 9449 section 8), so a
/// client created a moment ago can present the nonce another client just received instead of
/// paying a <c>use_dpop_nonce</c> round trip for its first request. The reference client keys its
/// cache the same way and shares it between the authorization server and the resource
/// server.</para>
/// <para>A stale nonce costs nothing but the round trip it was meant to save: the server answers
/// with a fresh one and the request is retried once. So the cache is small and forgetful: past
/// <see cref="Capacity"/> origins, arbitrary entries go, and a nonce longer than
/// <see cref="MaxNonceLength"/> characters is not kept, since the servers it comes from may be
/// anyone's.</para>
/// </remarks>
internal sealed class DPoPNonceCache
{
    /// <summary>The most origins remembered.</summary>
    internal const int Capacity = 1024;

    /// <summary>The longest nonce remembered. The reference servers issue nonces of about 40 characters.</summary>
    internal const int MaxNonceLength = 1024;

    private readonly ConcurrentDictionary<string, string> _nonces = new(StringComparer.Ordinal);

    /// <summary>The cache every client in the process shares unless given another.</summary>
    internal static DPoPNonceCache Shared { get; } = new();

    /// <summary>The number of origins remembered.</summary>
    internal int Count => _nonces.Count;

    /// <summary>The latest nonce the origin of <paramref name="url"/> handed out, if any.</summary>
    internal string? Get(Uri url) => _nonces.TryGetValue(Origin(url), out var nonce) ? nonce : null;

    /// <summary>
    /// Remembers the nonce in a response's <c>DPoP-Nonce</c> header for the origin of
    /// <paramref name="url"/>, and returns it; <see langword="null"/> when the response carries none
    /// worth keeping.
    /// </summary>
    internal string? Observe(Uri url, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("DPoP-Nonce", out var values) ||
            values.FirstOrDefault() is not { Length: > 0 and <= MaxNonceLength } nonce)
        {
            return null;
        }

        Set(url, nonce);
        return nonce;
    }

    /// <summary>Remembers <paramref name="nonce"/> for the origin of <paramref name="url"/>.</summary>
    internal void Set(Uri url, string nonce)
    {
        if (nonce.Length is 0 or > MaxNonceLength)
            return;

        _nonces[Origin(url)] = nonce;

        if (_nonces.Count > Capacity)
        {
            foreach (var key in _nonces.Keys)
            {
                if (_nonces.Count <= Capacity)
                    break;
                _nonces.TryRemove(key, out _);
            }
        }
    }

    /// <summary>The origin a URL's nonce is kept under: lower-cased scheme and host, and a non-default port.</summary>
    internal static string Origin(Uri url) => url.GetLeftPart(UriPartial.Authority);
}
