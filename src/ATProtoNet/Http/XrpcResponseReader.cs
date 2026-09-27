using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Serialization;

namespace ATProtoNet.Http;

// The parts of an XRPC error body clients act on.
//
// Error: The error name, if the body was an XRPC error envelope.
//
// Message: The message, if present.
//
// Body: The raw body text, if it could be read.
internal readonly record struct XrpcErrorBody(string? Error, string? Message, string? Body);

// Reads what XRPC responses say about failures and limits: the error envelope, the RateLimit-* headers,
// and how long a 429 asks the client to wait.
internal static class XrpcResponseReader
{
    // The most of an error body that is read. An XRPC envelope is a few hundred bytes; a body past this
    // is no envelope, and the service chose its size.
    internal const int MaxErrorBodyBytes = 64 * 1024;

    // Reads the {"error", "message"} envelope out of a failed response. A body that is not one — a
    // proxy's HTML error page, or anything over MaxErrorBodyBytes — yields nulls rather than throwing,
    // since the status alone still says what happened.
    internal static async Task<XrpcErrorBody> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte>? bytes;
        try
        {
            bytes = await response.Content.ReadBoundedAsync(MaxErrorBodyBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return default;
        }

        return bytes is { } body ? ParseError(Encoding.UTF8.GetString(body.Span)) : default;
    }

    // Extracts the error envelope from a body already read.
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

    // Builds the exception for a failed XRPC response: XrpcRateLimitException for a 429,
    // XrpcAuthenticationException for rejected credentials, and XrpcException otherwise.
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

    // Whether a failure rejects the credentials rather than the request. A PDS answers an expired or
    // invalid access token with a 400 and a token error name, not a 401.
    private static bool IsAuthenticationFailure(HttpStatusCode status, string error) =>
        status == HttpStatusCode.Unauthorized ||
        error is XrpcErrors.ExpiredToken or XrpcErrors.InvalidToken;

    // Parses the RateLimit-Limit, -Remaining and -Reset headers, or returns null when the response
    // carries none of them.
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

    // How long a response asks the client to wait before retrying: Retry-After, in seconds or as an HTTP
    // date, otherwise the time until RateLimit-Reset. Never negative; null when the response names no
    // wait.
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
