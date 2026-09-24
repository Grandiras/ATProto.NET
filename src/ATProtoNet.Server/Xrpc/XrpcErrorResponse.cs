using System.Net;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// The one place a failed XRPC request becomes a response: every exception an endpoint raises is
/// answered with the <c>{"error", "message"}</c> envelope XRPC clients branch on.
/// </summary>
internal static partial class XrpcErrorResponse
{
    /// <summary>The log category unexpected handler failures are written under.</summary>
    public const string LoggerCategory = "ATProtoNet.Server.Xrpc";

    /// <summary>
    /// Wraps an endpoint's delegate — a handler's, or the group's fallback — so that nothing it
    /// throws reaches the host's own error page.
    /// </summary>
    /// <remarks>
    /// Once the response has started — a blob that failed halfway — no envelope can be written,
    /// so the exception propagates and the server aborts the response rather than let a truncated
    /// body pass for a complete one.
    /// </remarks>
    /// <param name="invoke">The endpoint's delegate.</param>
    /// <param name="logger">Where unexpected failures are logged.</param>
    public static RequestDelegate Handle(RequestDelegate invoke, ILogger logger) =>
        async context =>
        {
            try
            {
                await invoke(context);
            }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                await WriteAsync(context, exception, logger);
            }
        };

    /// <summary>
    /// Answers a request for <c>/xrpc/{nsid}</c> that no endpoint matched, as the reference
    /// <c>xrpc-server</c> does: <c>InvalidRequest</c> for a segment that is not an NSID,
    /// <c>405</c> naming the right method for a registered NSID called with the wrong one, and
    /// <c>501 MethodNotImplemented</c> for any other NSID.
    /// </summary>
    /// <remarks>
    /// A registered NSID never reaches this with its own method: the fallback is ordered after
    /// every other endpoint. It does catch the wrong method, because routing prefers an endpoint
    /// accepting any method over producing its own 405, so that answer is written here instead
    /// — with the <c>Allow</c> header, and an XRPC body the client can read.
    /// </remarks>
    /// <param name="registrations">The endpoints mapped in the same group.</param>
    public static RequestDelegate Unmatched(IEnumerable<XrpcEndpointRegistration> registrations)
    {
        // Case-insensitive, as route matching of the registered paths is.
        var methods = registrations.ToDictionary(r => r.Nsid.Value, r => r.HttpMethod, StringComparer.OrdinalIgnoreCase);

        return context =>
        {
            var segment = context.Request.RouteValues["nsid"] as string;

            if (!Nsid.TryParse(segment, out _))
            {
                return WriteAsync(
                    context, HttpStatusCode.BadRequest, XrpcErrors.InvalidRequest, $"Invalid XRPC path: '{segment}' is not an NSID.");
            }

            if (methods.TryGetValue(segment, out var method))
            {
                context.Response.Headers.Allow = method;
                return WriteAsync(
                    context,
                    HttpStatusCode.MethodNotAllowed,
                    XrpcErrors.InvalidRequest,
                    $"Incorrect HTTP method ({context.Request.Method}), expected {method}.");
            }

            return WriteAsync(context, HttpStatusCode.NotImplemented, XrpcErrors.MethodNotImplemented, "Method Not Implemented");
        };
    }

    private static Task WriteAsync(HttpContext context, Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case XrpcException xrpc:
                foreach (var (name, value) in xrpc.Headers)
                    context.Response.Headers[name] = value;

                return WriteAsync(context, xrpc.StatusCode, xrpc.Error, xrpc.ErrorMessage ?? xrpc.Error);

            // The server's own refusal of the request: a body over the size limit, a malformed
            // body framing, a read timeout.
            case BadHttpRequestException badRequest:
                return WriteAsync(
                    context,
                    (HttpStatusCode)badRequest.StatusCode,
                    badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                        ? XrpcErrors.PayloadTooLarge
                        : XrpcErrors.InvalidRequest,
                    badRequest.Message);

            // The client went away; there is no one to answer.
            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return Task.CompletedTask;

            default:
                // What went wrong is for the operator, not the caller: the message of an arbitrary
                // exception can carry anything from a connection string to another user's data.
                LogUnhandled(logger, exception, context.Request.Method, context.Request.Path.Value);
                return WriteAsync(context, HttpStatusCode.InternalServerError, XrpcErrors.InternalServerError, "Internal Server Error");
        }
    }

    private static Task WriteAsync(HttpContext context, HttpStatusCode status, string error, string message)
    {
        var response = context.Response;
        response.StatusCode = (int)status;

        // A blob handler may already have declared the length of the body it failed to write.
        response.ContentLength = null;

        return response.WriteAsJsonAsync(new XrpcErrorBody { Error = error, Message = message }, XrpcJson<XrpcErrorBody>.TypeInfo);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "XRPC request {Method} {Path} failed with an unhandled exception.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string? path);
}

/// <summary>The XRPC error wire body: a name clients branch on, and a description for humans.</summary>
internal sealed class XrpcErrorBody
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
