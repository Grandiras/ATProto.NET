using ATProtoNet.Identity;

namespace ATProtoNet.Auth;

// The value syntax of service auth tokens, shared by the generator and the server-side verifier.
internal static class ServiceAuthSyntax
{
    // Whether value is a service auth audience: a DID, optionally followed by a # fragment naming one of
    // the services in its DID document (did:web:feed.example.com#bsky_fg).
    //
    // The same check the reference implementation applies (isDidStringOrService): one non-empty fragment,
    // after a DID.
    public static bool IsAudience(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var hash = value.IndexOf('#');
        return hash < 0
            ? Did.TryParse(value, out _)
            : IsFragment(value.AsSpan(hash)) && Did.TryParse(value[..hash], out _);
    }

    // Whether value is a key identifier as the kid header carries one: a verification-method fragment with
    // its # and without the DID (#atproto).
    public static bool IsKeyId(string? value) => value is not null && IsFragment(value);

    private static bool IsFragment(ReadOnlySpan<char> value) =>
        value.Length > 1 &&
        value[0] == '#' &&
        !value[1..].ContainsAny('#', ' ') &&
        !value.ContainsAnyInRange('\0', '\x1f');
}
