using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Authentication;

internal static class AuthorizationHeader
{
    /// <summary>Splits the request's <c>Authorization</c> header at its first space; false when it has none.</summary>
    public static bool TryRead(HttpRequest request, out string scheme, out string token)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            scheme = token = string.Empty;
            return false;
        }

        var space = header.IndexOf(' ', StringComparison.Ordinal);
        (scheme, token) = space < 0 ? (header, string.Empty) : (header[..space], header[(space + 1)..].Trim());
        return true;
    }

    /// <summary>The non-empty token of an <c>Authorization: Bearer</c> header, or <see langword="null"/>.</summary>
    public static string? Bearer(HttpRequest request) =>
        TryRead(request, out var scheme, out var token)
        && string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
        && token.Length > 0
            ? token
            : null;
}
