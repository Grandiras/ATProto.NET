using ATProtoNet.Http;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Parameter validation shared by the space endpoint handlers.
/// </summary>
/// <remarks>
/// <para>Every failure here is an <c>InvalidRequest</c> — the request is malformed, and saying so
/// discloses nothing, because none of these checks consult any state. Anything that <em>would</em>
/// require a lookup answers <c>RepoNotFound</c> or <c>SpaceNotFound</c> instead, which the
/// protocol keeps deliberately uninformative.</para>
/// <para>Identifier syntax is already checked by then: parameters and bodies bind through the
/// identifier types' own parsers, which refuse a malformed value with <c>InvalidRequest</c>. A
/// participant is a <see cref="Identity.Did"/> rather than an <see cref="Identity.AtIdentifier"/>,
/// so a handle — which can be reassigned, and would silently move a repo's contents to a
/// different account — never binds in its place. What is left here is presence.</para>
/// </remarks>
internal static class SpaceRequestValidation
{
    /// <summary>The default page size when a request names none.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Requires the space parameter.</summary>
    public static SpaceUri RequireSpace(SpaceUri? value) => Require(value, "space");

    /// <summary>
    /// Requires a parameter or body field. A body can carry an explicit JSON <c>null</c> for a
    /// field the Lexicon requires, which deserializes without complaint.
    /// </summary>
    public static T Require<T>(T? value, string name)
        where T : class =>
        value ?? throw new XrpcException(XrpcErrors.InvalidRequest, $"The \"{name}\" parameter is required.");

    /// <summary>Requires a non-empty string parameter.</summary>
    public static string RequireString(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new XrpcException(XrpcErrors.InvalidRequest, $"The \"{name}\" parameter is required.")
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
            ? throw new XrpcException(XrpcErrors.InvalidRequest, "The \"limit\" parameter must be at least 1.")
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
            throw new XrpcException(XrpcErrors.InvalidRequest, ex.Message, ex);
        }
    }
}
