using System.Text.Json;
using ATProtoNet.Identity;

namespace ATProtoNet.Auth;

/// <summary>
/// The JSON form the SDK's own session stores persist, and the reader that also accepts what
/// their predecessors wrote.
/// </summary>
internal static class AtProtoSessionJson
{
    // A hand-edited or re-serialized document may not put "$kind" first.
    private static readonly JsonSerializerOptions Options = new() { AllowOutOfOrderMetadataProperties = true };

    /// <summary>Serializes a session with its <c>$kind</c> discriminator.</summary>
    public static string Serialize(AtProtoSession session) =>
        JsonSerializer.Serialize(session, Options);

    /// <summary>
    /// Reads a stored session: the current form, or the OAuth token data the 0.6 token stores
    /// wrote (camel-cased <c>AtProtoTokenData</c>), so sessions persisted before the upgrade stay
    /// signed in.
    /// </summary>
    /// <exception cref="JsonException">
    /// The JSON is neither form, or holds a value no session can have (an unknown <c>$kind</c>, a
    /// relative endpoint, a DPoP key that is not base64). A store treats it as corrupt.
    /// </exception>
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
        {
            throw new JsonException("The stored session has an endpoint that is not an absolute URL.");
        }

        return session;
    }

    private static OAuthSession ReadLegacyTokenData(JsonElement root)
    {
        var did = Did.TryParse(Get(root, "did"), out var parsedDid)
            ? parsedDid
            : throw new JsonException("The stored token data has no valid 'did'.");

        // The old format kept the DID in place of an unverified handle.
        var handle = GetBoolean(root, "isHandleVerified") == true && Handle.TryParse(Get(root, "handle"), out var verified)
            ? verified
            : Handle.Invalid;

        var key = Get(root, "dPoPPrivateKey") ?? Get(root, "dpopPrivateKey")
            ?? throw new JsonException("The stored token data has no DPoP key.");

        DateTimeOffset? expiresAt = null;
        if (GetInt32(root, "expiresIn") is { } expiresIn &&
            root.TryGetProperty("tokenObtainedAt", out var obtained) &&
            obtained.TryGetDateTimeOffset(out var obtainedAt))
        {
            expiresAt = obtainedAt.AddSeconds(expiresIn);
        }

        return new OAuthSession
        {
            Did = did,
            Handle = handle,
            ServiceEndpoint = RequireUri(root, "pdsUrl"),
            AccessToken = Get(root, "accessToken") ?? throw new JsonException("The stored token data has no access token."),
            RefreshToken = Get(root, "refreshToken"),
            DPoPKey = Convert.FromBase64String(key),
            Issuer = Get(root, "issuer") ?? throw new JsonException("The stored token data has no issuer."),
            TokenEndpoint = RequireUri(root, "tokenEndpoint"),
            ExpiresAt = expiresAt,
            Scope = Get(root, "scope"),
        };
    }

    private static string? Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? GetBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? GetInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static Uri RequireUri(JsonElement root, string name) =>
        Uri.TryCreate(Get(root, name), UriKind.Absolute, out var uri)
            ? uri
            : throw new JsonException($"The stored token data has no valid '{name}'.");
}
