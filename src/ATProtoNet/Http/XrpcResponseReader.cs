using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Http;

/// <summary>
/// The parts of an XRPC error body clients act on.
/// </summary>
/// <param name="Error">The <c>error</c> name, if the body was an XRPC error envelope.</param>
/// <param name="Message">The <c>message</c>, if present.</param>
/// <param name="Body">The raw body text, if it could be read.</param>
internal readonly record struct XrpcErrorBody(string? Error, string? Message, string? Body);

/// <summary>
/// Reads what XRPC responses say about failures and limits: the error envelope, the
/// <c>RateLimit-*</c> headers, and how long a 429 asks the client to wait.
/// </summary>
internal static class XrpcResponseReader
{
    /// <summary>
    /// The most of an error body that is read. An XRPC envelope is a few hundred bytes; a body
    /// past this is no envelope, and the service chose its size.
    /// </summary>
    internal const int MaxErrorBodyBytes = 64 * 1024;

    /// <summary>
    /// Reads the <c>{"error", "message"}</c> envelope out of a failed response. A body that is
    /// not one — a proxy's HTML error page, or anything over <see cref="MaxErrorBodyBytes"/> —
    /// yields nulls rather than throwing, since the status alone still says what happened.
    /// </summary>
    internal static async Task<XrpcErrorBody> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte>? bytes;
        try
        {
            bytes = await response.Content.ReadBoundedAsync(MaxErrorBodyBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return default;
        }

        return bytes is { } body ? ParseError(Encoding.UTF8.GetString(body.Span)) : default;
    }

    /// <summary>Extracts the error envelope from a body already read.</summary>
    internal static XrpcErrorBody ParseError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new XrpcErrorBody(null, null, body);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return new XrpcErrorBody(root.GetStringOrNull("error"), root.GetStringOrNull("message"), body);
        }
        catch (JsonException)
        {
            return new XrpcErrorBody(null, null, body);
        }
    }

    /// <summary>
    /// Builds the exception for a failed XRPC response: <see cref="XrpcRateLimitException"/>
    /// for a 429, <see cref="XrpcAuthenticationException"/> for rejected credentials, and
    /// <see cref="XrpcException"/> otherwise.
    /// </summary>
    internal static XrpcException CreateException(
        HttpResponseMessage response, string nsid, XrpcErrorBody body, DateTimeOffset now)
    {
        var status = response.StatusCode;
        var error = string.IsNullOrWhiteSpace(body.Error) ? XrpcErrors.ForStatus(status) : body.Error;

        XrpcException exception = status switch
        {
            HttpStatusCode.TooManyRequests => new XrpcRateLimitException(
                error, body.Message, nsid, GetRequestedDelay(response, now), ParseRateLimit(response))
            {
                ResponseBody = body.Body,
            },
            _ when IsAuthenticationFailure(status, error) => new XrpcAuthenticationException(
                error, body.Message, status, nsid)
            {
                ResponseBody = body.Body,
            },
            _ => new XrpcException(error, body.Message, status, nsid) { ResponseBody = body.Body },
        };

        foreach (var (name, values) in response.Headers)
            exception.Headers[name] = string.Join(", ", values);

        return exception;
    }

    /// <summary>
    /// Whether a failure rejects the credentials rather than the request. A PDS answers an
    /// expired or invalid access token with a 400 and a token error name, not a 401.
    /// </summary>
    private static bool IsAuthenticationFailure(HttpStatusCode status, string error) =>
        status == HttpStatusCode.Unauthorized ||
        error is XrpcErrors.ExpiredToken or XrpcErrors.InvalidToken;

    /// <summary>
    /// Parses the <c>RateLimit-Limit</c>, <c>-Remaining</c> and <c>-Reset</c> headers, or
    /// returns <see langword="null"/> when the response carries none of them.
    /// </summary>
    internal static RateLimitInfo? ParseRateLimit(HttpResponseMessage response)
    {
        var limit = GetInt64(response, "RateLimit-Limit");
        var remaining = GetInt64(response, "RateLimit-Remaining");
        var reset = GetInt64(response, "RateLimit-Reset");

        if (limit is null && remaining is null && reset is null)
            return null;

        return new RateLimitInfo
        {
            Limit = (int?)limit,
            Remaining = (int?)remaining,
            Reset = reset is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
        };
    }

    /// <summary>
    /// How long a response asks the client to wait before retrying: <c>Retry-After</c>, in
    /// seconds or as an HTTP date, otherwise the time until <c>RateLimit-Reset</c>. Never
    /// negative; <see langword="null"/> when the response names no wait.
    /// </summary>
    internal static TimeSpan? GetRequestedDelay(HttpResponseMessage response, DateTimeOffset now)
    {
        TimeSpan? delay = null;

        if (response.Headers.RetryAfter is { } retryAfter)
        {
            delay = retryAfter.Delta ?? (retryAfter.Date is { } date ? date - now : null);
        }

        if (delay is null && GetInt64(response, "RateLimit-Reset") is { } reset)
        {
            delay = DateTimeOffset.FromUnixTimeSeconds(reset) - now;
        }

        return delay is { Ticks: < 0 } ? TimeSpan.Zero : delay;
    }

    private static long? GetInt64(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) &&
        long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
