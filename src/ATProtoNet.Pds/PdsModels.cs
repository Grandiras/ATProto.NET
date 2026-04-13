using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Pds;

/// <summary>
/// Result from creating a session or account.
/// </summary>
public sealed class PdsSessionResult
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmed")]
    public bool EmailConfirmed { get; init; }

    [JsonPropertyName("accessJwt")]
    public required string AccessJwt { get; init; }

    [JsonPropertyName("refreshJwt")]
    public required string RefreshJwt { get; init; }
}

/// <summary>
/// Session info returned by getSession.
/// </summary>
public sealed class PdsSessionInfo
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmed")]
    public bool EmailConfirmed { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }
}

/// <summary>
/// Reference to a created/updated record.
/// </summary>
public sealed class PdsRecordRef
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }
}

/// <summary>
/// A record result with its value.
/// </summary>
public sealed class PdsRecordResult
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }
}

/// <summary>
/// Server description for describeServer.
/// </summary>
public sealed class PdsDescription
{
    [JsonPropertyName("inviteCodeRequired")]
    public bool InviteCodeRequired { get; init; }

    [JsonPropertyName("availableUserDomains")]
    public required List<string> AvailableUserDomains { get; init; }

    [JsonPropertyName("links")]
    public PdsLinks? Links { get; init; }

    [JsonPropertyName("contact")]
    public PdsContact? Contact { get; init; }
}

/// <summary>
/// Links provided by the PDS.
/// </summary>
public sealed class PdsLinks
{
    [JsonPropertyName("privacyPolicy")]
    public string? PrivacyPolicy { get; init; }

    [JsonPropertyName("termsOfService")]
    public string? TermsOfService { get; init; }
}

/// <summary>
/// Contact information for the PDS operator.
/// </summary>
public sealed class PdsContact
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Reference to an uploaded blob.
/// </summary>
public sealed class PdsBlobRef
{
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }
}
