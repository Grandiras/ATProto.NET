using ATProtoNet.Identity;

namespace ATProtoNet.Auth;

/// <summary>
/// The value syntax of service auth tokens, shared by the generator and the server-side verifier.
/// </summary>
internal static class ServiceAuthSyntax
{
    /// <summary>
    /// Whether <paramref name="value"/> is a service auth audience: a DID, optionally followed by
    /// a <c>#</c> fragment naming one of the services in its DID document
    /// (<c>did:web:feed.example.com#bsky_fg</c>).
    /// </summary>
    /// <remarks>
    /// The same check the reference implementation applies (<c>isDidStringOrService</c>): one
    /// non-empty fragment, after a DID.
    /// </remarks>
    public static bool IsAudience(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var hash = value.IndexOf('#');
        return hash < 0
            ? Did.TryParse(value, out _)
            : IsFragment(value.AsSpan(hash)) && Did.TryParse(value[..hash], out _);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a key identifier as the <c>kid</c> header carries one:
    /// a verification-method fragment with its <c>#</c> and without the DID (<c>#atproto</c>).
    /// </summary>
    public static bool IsKeyId(string? value) => value is not null && IsFragment(value);

    private static bool IsFragment(ReadOnlySpan<char> value) =>
        value.Length > 1 &&
        value[0] == '#' &&
        !value[1..].ContainsAny('#', ' ') &&
        !value.ContainsAnyInRange('\0', '\x1f');
}
