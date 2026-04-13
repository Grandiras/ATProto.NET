namespace ATProtoNet.Pds;

/// <summary>
/// Configuration options for the PDS host.
/// </summary>
public sealed class PdsOptions
{
    /// <summary>
    /// The public hostname of this PDS (e.g. "pds.example.com").
    /// Used to form AT URIs and in DID documents.
    /// </summary>
    public string Hostname { get; set; } = "localhost";

    /// <summary>
    /// The public URL of this PDS (e.g. "https://pds.example.com").
    /// Defaults to https://{Hostname}.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Whether to allow open registration (any user can create an account).
    /// When false, invite codes are required.
    /// </summary>
    public bool OpenRegistration { get; set; }

    /// <summary>
    /// Maximum blob size in bytes. Default: 5 MB.
    /// </summary>
    public long MaxBlobSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Supported DID methods for account creation. Default: ["plc"].
    /// </summary>
    public List<string> AvailableUserDomains { get; set; } = [];

    /// <summary>
    /// Contact email for the PDS operator.
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Gets the resolved public URL. Falls back to https://{Hostname}.
    /// </summary>
    internal string ResolvedPublicUrl => PublicUrl ?? $"https://{Hostname}";
}
