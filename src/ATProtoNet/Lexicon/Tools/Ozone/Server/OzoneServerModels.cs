using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Server;

/// <summary>
/// Ozone server configuration.
/// </summary>
public sealed class OzoneServerConfig
{
    /// <summary>Configuration of the app view service Ozone talks to.</summary>
    [JsonPropertyName("appview")]
    public ServiceConfig? Appview { get; init; }

    /// <summary>Configuration of the PDS Ozone talks to.</summary>
    [JsonPropertyName("pds")]
    public ServiceConfig? Pds { get; init; }

    /// <summary>Configuration of the blob diversion service.</summary>
    [JsonPropertyName("blobDivert")]
    public ServiceConfig? BlobDivert { get; init; }

    /// <summary>Configuration of the chat service.</summary>
    [JsonPropertyName("chat")]
    public ServiceConfig? Chat { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public OzoneViewerConfig? Viewer { get; init; }
}

/// <summary>
/// A service endpoint configuration.
/// </summary>
public sealed class ServiceConfig
{
    /// <summary>The service URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>
/// Viewer-specific config (current user's role).
/// </summary>
public sealed class OzoneViewerConfig
{
    /// <summary>The role assigned to the member.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }
}
