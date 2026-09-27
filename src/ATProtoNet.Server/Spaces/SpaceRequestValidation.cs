using ATProtoNet.Http;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

// Parameter validation shared by the space endpoint handlers.
//
// Every failure here is an InvalidRequest — the request is malformed, and saying so discloses nothing,
// because none of these checks consult any state. Anything that would require a lookup answers
// RepoNotFound or SpaceNotFound instead, which the protocol keeps deliberately uninformative.
//
// Identifier syntax is already checked by then: parameters and bodies bind through the identifier types'
// own parsers, which refuse a malformed value with InvalidRequest. A participant is a Identity.Did
// rather than an Identity.AtIdentifier, so a handle — which can be reassigned, and would silently move a
// repo's contents to a different account — never binds in its place. What is left here is presence.
internal static class SpaceRequestValidation
{
    // The default page size when a request names none.
    public const int DefaultLimit = 50;

    // Requires the space parameter.
    public static SpaceUri RequireSpace(SpaceUri? value) => Require(value, "space");

    // Requires a parameter or body field. A body can carry an explicit JSON null for a field the Lexicon
    // requires, which deserializes without complaint.
    public static T Require<T>(T? value, string name)
        where T : class =>
        value ?? throw new XrpcException(XrpcErrors.InvalidRequest, $"The \"{name}\" parameter is required.");

    // Requires a non-empty string parameter.
    public static string RequireString(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new XrpcException(XrpcErrors.InvalidRequest, $"The \"{name}\" parameter is required.")
            : value;

    // Clamps a page size into the Lexicon's declared range, defaulting when unset.
    //
    // value: The requested limit.
    //
    // defaultLimit: The default when none was requested.
    //
    // maxLimit: The largest page this method serves.
    public static int Limit(int? value, int defaultLimit = DefaultLimit, int maxLimit = 1000)
    {
        if (value is null)
            return defaultLimit;

        return value < 1
            ? throw new XrpcException(XrpcErrors.InvalidRequest, "The \"limit\" parameter must be at least 1.")
            : Math.Min(value.Value, maxLimit);
    }

    // Parses a service identifier — a DID with an optional service fragment, as registerNotify carries.
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
