using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Server;

/// <summary>
/// Ozone server configuration.
/// </summary>
public sealed class OzoneServerConfig
{
    [JsonPropertyName("appview")]
    public ServiceConfig? Appview { get; init; }

    [JsonPropertyName("pds")]
    public ServiceConfig? Pds { get; init; }

    [JsonPropertyName("blobDivert")]
    public ServiceConfig? BlobDivert { get; init; }

    [JsonPropertyName("chat")]
    public ServiceConfig? Chat { get; init; }

    [JsonPropertyName("viewer")]
    public OzoneViewerConfig? Viewer { get; init; }
}

/// <summary>
/// A service endpoint configuration.
/// </summary>
public sealed class ServiceConfig
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>
/// Viewer-specific config (current user's role).
/// </summary>
public sealed class OzoneViewerConfig
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }
}
