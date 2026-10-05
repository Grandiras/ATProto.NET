using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

namespace ATProtoNet.Auth;

// The JSON form the SDK's own session stores persist, and the reader that also accepts what their
// predecessors wrote.
internal static class AtProtoSessionJson
{
    // A hand-edited or re-serialized document may not put "$kind" first.
    private static readonly JsonSerializerOptions Options = new() { AllowOutOfOrderMetadataProperties = true };

    // Serializes a session with its $kind discriminator.
    public static string Serialize(AtProtoSession session) =>
        JsonSerializer.Serialize(session, Options);

    // Reads a stored session: the current form, or the OAuth token data the 0.6 token stores wrote
    // (camel-cased AtProtoTokenData), so sessions persisted before the upgrade stay signed in.
    //
    // Throws JsonException: The JSON is neither form, or holds a value no session can have (an unknown
    // $kind, a relative endpoint, a DPoP key that is not base64). A store treats it as corrupt.
    public static AtProtoSession Deserialize(string json)
    {
        AtProtoSession session;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("A stored session must be a JSON object.");

            session = root.TryGetProperty("$kind", out _)
                ? root.Deserialize<AtProtoSession>(Options) ?? throw new JsonException("The stored session is null.")
                : ReadLegacyTokenData(root);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException or InvalidOperationException)
        {
            // An unknown discriminator, bad base64 and the like surface as these; to a store they
            // all mean the same thing as malformed JSON.
            throw new JsonException($"The stored session is not valid: {ex.Message}", ex);
        }

        if (!session.ServiceEndpoint.IsAbsoluteUri ||
            session is OAuthSession { TokenEndpoint.IsAbsoluteUri: false } ||
            session is OAuthSession { RevocationEndpoint.IsAbsoluteUri: false })
            throw new JsonException("The stored session has an endpoint that is not an absolute URL.");

        return session;
    }

    private static OAuthSession ReadLegacyTokenData(JsonElement root)
    {
        var did = Did.TryParse(root.GetStringOrNull("did"), out var parsedDid)
            ? parsedDid
            : throw new JsonException("The stored token data has no valid 'did'.");

        // The old format kept the DID in place of an unverified handle.
        var handle = root.GetBooleanOrNull("isHandleVerified") == true && Handle.TryParse(root.GetStringOrNull("handle"), out var verified)
            ? verified
            : Handle.Invalid;

        var key = root.GetStringOrNull("dPoPPrivateKey") ?? root.GetStringOrNull("dpopPrivateKey")
            ?? throw new JsonException("The stored token data has no DPoP key.");

        DateTimeOffset? expiresAt = null;
        if (root.GetInt32OrNull("expiresIn") is { } expiresIn &&
            root.TryGetProperty("tokenObtainedAt", out var obtained) &&
            obtained.TryGetDateTimeOffset(out var obtainedAt))
            expiresAt = obtainedAt.AddSeconds(expiresIn);

        return new OAuthSession
        {
            Did = did,
            Handle = handle,
            ServiceEndpoint = RequireUri(root, "pdsUrl"),
            AccessToken = root.GetStringOrNull("accessToken") ?? throw new JsonException("The stored token data has no access token."),
            RefreshToken = root.GetStringOrNull("refreshToken"),
            DPoPKey = Convert.FromBase64String(key),
            Issuer = root.GetStringOrNull("issuer") ?? throw new JsonException("The stored token data has no issuer."),
            TokenEndpoint = RequireUri(root, "tokenEndpoint"),
            ExpiresAt = expiresAt,
            Scope = root.GetStringOrNull("scope"),
        };
    }

    private static Uri RequireUri(JsonElement root, string name) =>
        Uri.TryCreate(root.GetStringOrNull(name), UriKind.Absolute, out var uri)
            ? uri
            : throw new JsonException($"The stored token data has no valid '{name}'.");
}
