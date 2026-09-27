using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Tap;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Tap;

/// <summary>Configures a Tap webhook endpoint.</summary>
public sealed class TapWebhookOptions
{
    /// <summary>
    /// The admin password the Tap instance was started with (<c>TAP_ADMIN_PASSWORD</c>): Tap sends
    /// it on every webhook as HTTP Basic auth, user <c>admin</c>. Required unless
    /// <see cref="AllowUnauthenticated"/> is set.
    /// </summary>
    public string? AdminPassword { get; set; }

    /// <summary>
    /// Accept webhooks without checking who sent them, for a Tap instance with no admin password.
    /// Anyone who can reach the endpoint can then feed it events. Default: false.
    /// </summary>
    public bool AllowUnauthenticated { get; set; }

    /// <summary>
    /// The largest request body accepted, in bytes; a larger one is refused with a 413. An event
    /// carries one record of at most a million bytes of CBOR, which its JSON form can exceed.
    /// Default: 4 MiB.
    /// </summary>
    public long MaxBodyBytes { get; set; } = 4L * 1024 * 1024;
}

/// <summary>Maps an ASP.NET Core endpoint that receives Tap's webhook delivery mode (<c>TAP_WEBHOOK_URL</c>).</summary>
/// <remarks>
/// <para>Tap POSTs each event as JSON, with the admin password as HTTP Basic auth, and counts it
/// delivered once the endpoint answers with a 2xx; any other answer is retried with backoff. The
/// endpoint checks the password in constant time and answers 401 without it, bounds the body
/// (<see cref="TapWebhookOptions.MaxBodyBytes"/>, 413 beyond it), and answers 200 once the handler
/// returns. A body that is not a Tap event (malformed, or of a type this SDK does not model) is
/// acknowledged too, with a warning logged: refused, Tap would resend it forever and hold back its
/// repository's later events. This is the receiver <c>@atproto/tap</c>
/// documents: <c>assureAdminAuth</c> and <c>parseTapEvent</c>.</para>
/// <para>Tap redelivers an event whose webhook failed or timed out, so handlers must be idempotent.</para>
/// </remarks>
/// <example>
/// <code>
/// app.MapTapWebhook("/tap/webhook", async (evt, ct) =>
/// {
///     if (evt is TapRecordEvent record)
///         await index.ApplyAsync(record, ct);
/// }, options => options.AdminPassword = builder.Configuration["Tap:AdminPassword"]);
/// </code>
/// </example>
public static class TapWebhookExtensions
{
    /// <summary>
    /// Maps a POST endpoint at <paramref name="pattern"/> that receives Tap webhooks and passes each
    /// event to <paramref name="handler"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route, e.g. <c>/tap/webhook</c>: the path of <c>TAP_WEBHOOK_URL</c>.</param>
    /// <param name="handler">
    /// Processes each event: returning acknowledges it, throwing answers 500 and Tap sends it
    /// again. One that needs scoped services creates a scope from the application's services.
    /// </param>
    /// <param name="configure">Sets the admin password and limits.</param>
    /// <returns>The endpoint's builder, for further conventions.</returns>
    /// <exception cref="InvalidOperationException">No admin password is set and unauthenticated webhooks are not allowed.</exception>
    public static RouteHandlerBuilder MapTapWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TapEvent, CancellationToken, Task> handler,
        Action<TapWebhookOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        var options = Configure(configure);
        Func<HttpContext, Task<IResult>> receive = context => ReceiveAsync(context, options, handler);
        return endpoints.MapPost(pattern, receive);
    }

    // Whether an Authorization header carries Tap's admin credentials: HTTP Basic, user admin, and
    // password, compared in constant time.
    internal static bool IsAuthorized(string? header, string password)
    {
        const string scheme = "Basic ";
        if (header is null || !header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(header[scheme.Length..].Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        // user:password, split at the first colon (RFC 7617): the password may hold colons.
        var colon = Array.IndexOf(decoded, (byte)':');
        if (colon < 0 || !decoded.AsSpan(0, colon).SequenceEqual("admin"u8))
            return false;

        // Comparing digests keeps the time independent of where the passwords differ and of how
        // long either is.
        Span<byte> given = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(decoded.AsSpan(colon + 1), given);
        SHA256.HashData(Encoding.UTF8.GetBytes(password), expected);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    private static TapWebhookOptions Configure(Action<TapWebhookOptions>? configure)
    {
        var options = new TapWebhookOptions();
        configure?.Invoke(options);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBodyBytes, nameof(TapWebhookOptions.MaxBodyBytes));
        if (string.IsNullOrEmpty(options.AdminPassword) && !options.AllowUnauthenticated)
        {
            throw new InvalidOperationException(
                "A Tap webhook needs the instance's admin password to authenticate deliveries. Set AdminPassword, " +
                "or AllowUnauthenticated for an instance that has none.");
        }

        return options;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context, TapWebhookOptions options, Func<TapEvent, CancellationToken, Task> handler)
    {
        if (options.AdminPassword is { Length: > 0 } password
            && !IsAuthorized(context.Request.Headers.Authorization.ToString(), password))
        {
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"admin\", charset=\"UTF-8\"";
            return Results.Json(new { error = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (context.Request.ContentLength > options.MaxBodyBytes)
            return TooLarge();

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = options.MaxBodyBytes;

        byte[]? body;
        try
        {
            body = await ReadBoundedAsync(context.Request.Body, options.MaxBodyBytes, context.RequestAborted).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }

        if (body is null)
            return TooLarge();

        TapEvent evt;
        try
        {
            evt = TapEvent.Parse(body);
        }
        catch (FormatException ex)
        {
            // Acknowledged: Tap retries anything else indefinitely, in order per repository, so
            // refusing one event it cannot deliver differently would stall that repository.
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(TapWebhookExtensions))
                .LogWarning(ex, "Acknowledged a Tap webhook delivery that is not a readable event");
            return Results.Ok();
        }

        try
        {
            await handler(evt, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(TapWebhookExtensions))
                .LogError(ex, "Handling Tap event {Id} failed; Tap will send it again", evt.Id);
            return Results.Json(new { error = "Failed to process event" }, statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    private static IResult TooLarge() =>
        Results.Json(new { error = "PayloadTooLarge" }, statusCode: StatusCodes.Status413PayloadTooLarge);

    // Reads the body, or returns null once it passes maxBytes.
    private static async Task<byte[]?> ReadBoundedAsync(Stream body, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
