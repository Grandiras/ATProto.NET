using System.Net;

namespace ATProtoNet.Http;

/// <summary>
/// Represents an error response from an XRPC endpoint.
/// </summary>
public sealed class AtProtoHttpException : HttpRequestException
{
    /// <summary>
    /// The error type name from the response (e.g., "InvalidRequest", "AuthenticationRequired").
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// The human-readable error message from the response.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// The HTTP status code of the response.
    /// </summary>
    public new HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// The raw response body, if available.
    /// </summary>
    public string? ResponseBody { get; }

    public AtProtoHttpException(string? errorType, string? errorMessage, HttpStatusCode statusCode, string? responseBody = null)
        : base($"XRPC Error [{statusCode}] {errorType}: {errorMessage}", null, statusCode)
    {
        ErrorType = errorType;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public AtProtoHttpException(string message, HttpStatusCode statusCode)
        : base(message, null, statusCode)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// The error response body from an XRPC endpoint.
/// </summary>
internal sealed class XrpcErrorResponse
{
    public string? Error { get; set; }
    public string? Message { get; set; }
}
