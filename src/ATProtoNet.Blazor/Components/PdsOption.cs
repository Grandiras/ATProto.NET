namespace ATProtoNet.Blazor.Components;

/// <summary>
/// Represents a PDS option for the login form dropdown.
/// </summary>
public sealed class PdsOption
{
    /// <summary>The display label (e.g., "My PDS").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The PDS URL (e.g., "https://pds.example.com").</summary>
    public string Url { get; set; } = string.Empty;
}
