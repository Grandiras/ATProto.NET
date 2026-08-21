using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Parameter validation shared by the space endpoint handlers.
/// </summary>
/// <remarks>
/// Every failure here is an <c>InvalidRequest</c> — the request is malformed, and saying so
/// discloses nothing, because none of these checks consult any state. Anything that <em>would</em>
/// require a lookup answers <c>RepoNotFound</c> or <c>SpaceNotFound</c> instead, which the
/// protocol keeps deliberately uninformative.
/// </remarks>
internal static class SpaceRequestValidation
{
    /// <summary>The default page size when a request names none.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Parses a required space URI parameter.</summary>
    public static SpaceUri RequireSpace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new XrpcException("InvalidRequest", "The \"space\" parameter is required.");

        return SpaceUri.TryParse(value, out var space)
            ? space
            : throw new XrpcException("InvalidRequest", $"'{value}' is not a valid space URI.");
    }

    /// <summary>Parses a required DID parameter.</summary>
    public static string RequireDid(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter is required.");

        // A space's participants are keyed on DIDs, never handles — a handle can be reassigned
        // and would silently move a repo's contents to a different account.
        return Did.TryParse(value, out _)
            ? value
            : throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter must be a DID; got '{value}'.");
    }

    /// <summary>Parses a required NSID parameter.</summary>
    public static string RequireNsid(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter is required.");

        return Nsid.TryParse(value, out _)
            ? value
            : throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter must be an NSID; got '{value}'.");
    }

    /// <summary>Parses an optional NSID parameter.</summary>
    public static string? OptionalNsid(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireNsid(value, name);

    /// <summary>Parses a required record key parameter.</summary>
    public static string RequireRkey(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter is required.");

        return RecordKey.TryParse(value, out _)
            ? value
            : throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter must be a record key; got '{value}'.");
    }

    /// <summary>Requires a non-empty string parameter.</summary>
    public static string RequireString(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new XrpcException("InvalidRequest", $"The \"{name}\" parameter is required.")
            : value;

    /// <summary>
    /// Clamps a page size into the Lexicon's declared range, defaulting when unset.
    /// </summary>
    /// <param name="value">The requested limit.</param>
    /// <param name="defaultLimit">The default when none was requested.</param>
    /// <param name="maxLimit">The largest page this method serves.</param>
    public static int Limit(int? value, int defaultLimit = DefaultLimit, int maxLimit = 1000)
    {
        if (value is null)
            return defaultLimit;

        return value < 1
            ? throw new XrpcException("InvalidRequest", "The \"limit\" parameter must be at least 1.")
            : Math.Min(value.Value, maxLimit);
    }

    /// <summary>
    /// Parses a service identifier — a DID with an optional service fragment, as
    /// <c>registerNotify</c> carries.
    /// </summary>
    public static string RequireServiceIdentifier(string? value, string name)
    {
        var identifier = RequireString(value, name);

        try
        {
            SpaceAuthority.ParseServiceIdentifier(identifier);
            return identifier;
        }
        catch (ArgumentException ex)
        {
            throw new XrpcException("InvalidRequest", ex.Message, ex);
        }
    }
}
