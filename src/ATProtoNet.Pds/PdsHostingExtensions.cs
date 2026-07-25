using System.Text.Json;
using ATProtoNet.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Pds;

/// <summary>
/// Extension methods for adding PDS hosting to an ASP.NET Core application.
/// </summary>
public static class PdsHostingExtensions
{
    /// <summary>
    /// Register PDS hosting services with in-memory stores (for development/testing).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional PDS configuration.</param>
    public static IServiceCollection AddAtProtoPds(
        this IServiceCollection services,
        Action<PdsOptions>? configure = null)
    {
        var options = new PdsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(CreateSessionService);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PdsSessionKeyStartupCheck>());
        services.AddSingleton<PdsService>();

        // Default in-memory stores; can be replaced by calling
        // AddAtProtoPds<TAccountStore, TRepoStore>() instead
        services.AddSingleton<IAccountStore, InMemoryAccountStore>();
        services.AddSingleton<IRepoStore, InMemoryRepoStore>();

        return services;
    }

    /// <summary>
    /// Register PDS hosting services with custom store implementations.
    /// </summary>
    /// <typeparam name="TAccountStore">The account store implementation.</typeparam>
    /// <typeparam name="TRepoStore">The repository store implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional PDS configuration.</param>
    public static IServiceCollection AddAtProtoPds<TAccountStore, TRepoStore>(
        this IServiceCollection services,
        Action<PdsOptions>? configure = null)
        where TAccountStore : class, IAccountStore
        where TRepoStore : class, IRepoStore
    {
        var options = new PdsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(CreateSessionService);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PdsSessionKeyStartupCheck>());
        services.AddSingleton<PdsService>();

        services.AddSingleton<IAccountStore, TAccountStore>();
        services.AddSingleton<IRepoStore, TRepoStore>();

        return services;
    }

    /// <summary>
    /// Builds the singleton <see cref="PdsSessionService"/>. DI cannot supply the
    /// <c>byte[]</c> signing key itself, so the key comes from
    /// <see cref="PdsOptions.SessionSigningKey"/>; when it is unset the service falls back to
    /// a per-process random key and we warn, because that silently logs every client out on
    /// each restart.
    /// </summary>
    private static PdsSessionService CreateSessionService(IServiceProvider services)
    {
        var options = services.GetRequiredService<PdsOptions>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(PdsSessionService).FullName!);
        var key = PdsSessionService.ResolveSigningKey(options);

        if (key is null)
        {
            logger?.LogWarning(
                "PdsOptions.SessionSigningKey is not configured — session tokens are signed with a " +
                "random key that is discarded when this process exits, so every access and refresh " +
                "token is invalidated on restart. Set PdsOptions.SessionSigningKey to a persisted " +
                "base64 key (generate one with PdsSessionService.GenerateSigningKey()).");
        }
        else if (key.Length < PdsSessionService.SigningKeySize)
        {
            logger?.LogWarning(
                "PdsOptions.SessionSigningKey decodes to {KeyLength} bytes; HMAC-SHA256 session " +
                "signing keys should be at least {MinimumKeyLength} bytes.",
                key.Length, PdsSessionService.SigningKeySize);
        }

        return new PdsSessionService(options, key);
    }

    /// <summary>
    /// Map the core AT Protocol XRPC endpoints for a PDS.
    /// This maps the com.atproto.server.*, com.atproto.repo.*, and com.atproto.sync.* endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static IEndpointRouteBuilder MapAtProtoPds(this IEndpointRouteBuilder endpoints)
        => MapAtProtoPds(endpoints, configure: null);

    /// <summary>
    /// Map the core AT Protocol XRPC endpoints for a PDS, optionally excluding endpoints
    /// the host wants to implement itself or applying route conventions to the mapped ones.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configure">
    /// Configures which endpoints are mapped. Exclude an endpoint to map your own
    /// implementation on the same route without an ambiguous-match conflict:
    /// <c>app.MapAtProtoPds(o =&gt; o.Exclude(PdsEndpointNames.CreateAccount))</c>.
    /// </param>
    public static IEndpointRouteBuilder MapAtProtoPds(
        this IEndpointRouteBuilder endpoints,
        Action<PdsEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var jsonOptions = AtProtoJsonDefaults.Options;
        var options = new PdsEndpointOptions();
        configure?.Invoke(options);

        // Maps a route only when the host hasn't excluded it, then runs any configured
        // route conventions (auth policies, filters, metadata) against the result.
        void Map(string nsid, Func<string, IEndpointConventionBuilder> map)
        {
            if (!options.IsMapped(nsid)) return;

            var builder = map($"/xrpc/{nsid}");
            builder.WithDisplayName(nsid);
            options.ApplyConventions(nsid, builder);
        }

        // ── Server endpoints ──

        Map(PdsEndpointNames.CreateAccount, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<CreateAccountInput>(jsonOptions, ctx.RequestAborted);
            if (body is null) return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing request body."));

            try
            {
                var result = await pds.CreateAccountAsync(body.Handle, body.Email, body.Password,
                    body.Did, body.InviteCode, ctx.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 400);
            }
        }));

        Map(PdsEndpointNames.CreateSession, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<CreateSessionInput>(jsonOptions, ctx.RequestAborted);
            if (body is null) return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing request body."));

            try
            {
                var result = await pds.CreateSessionAsync(body.Identifier, body.Password, ctx.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 401);
            }
        }));

        Map(PdsEndpointNames.GetSession, route => endpoints.MapGet(route, async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            try
            {
                var info = await pds.GetSessionAsync(did, ctx.RequestAborted);
                return Results.Json(info, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 401);
            }
        }));

        Map(PdsEndpointNames.RefreshSession, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions, requiredScope: "com.atproto.refresh");
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            try
            {
                var result = await pds.RefreshSessionAsync(did, ctx.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 401);
            }
        }));

        Map(PdsEndpointNames.DescribeServer, route => endpoints.MapGet(route, (PdsService pds) =>
        {
            var description = pds.DescribeServer();
            return Results.Json(description, jsonOptions);
        }));

        // ── Repo endpoints ──

        Map(PdsEndpointNames.CreateRecord, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            var body = await ctx.Request.ReadFromJsonAsync<CreateRecordInput>(jsonOptions, ctx.RequestAborted);
            if (body is null) return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing request body."));

            // Verify the requester owns the repo
            if (body.Repo != did)
                return Results.Json(new PdsErrorResponse("AuthorizationError", "Cannot write to another user's repo."), statusCode: 403);

            try
            {
                var result = await pds.CreateRecordAsync(did, body.Collection, body.Record,
                    body.Rkey, ctx.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 400);
            }
        }));

        Map(PdsEndpointNames.GetRecord, route => endpoints.MapGet(route, async (HttpContext ctx, PdsService pds) =>
        {
            var repo = ctx.Request.Query["repo"].ToString();
            var collection = ctx.Request.Query["collection"].ToString();
            var rkey = ctx.Request.Query["rkey"].ToString();

            if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(rkey))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing required parameters."));

            var result = await pds.GetRecordAsync(repo, collection, rkey, ctx.RequestAborted);
            if (result is null)
                return Results.Json(new PdsErrorResponse("RecordNotFound", "Record not found."), statusCode: 400);

            return Results.Json(result, jsonOptions);
        }));

        Map(PdsEndpointNames.PutRecord, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            var body = await ctx.Request.ReadFromJsonAsync<PutRecordInput>(jsonOptions, ctx.RequestAborted);
            if (body is null) return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing request body."));

            if (body.Repo != did)
                return Results.Json(new PdsErrorResponse("AuthorizationError", "Cannot write to another user's repo."), statusCode: 403);

            try
            {
                var result = await pds.PutRecordAsync(did, body.Collection, body.Rkey, body.Record, ctx.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 400);
            }
        }));

        Map(PdsEndpointNames.DeleteRecord, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            var body = await ctx.Request.ReadFromJsonAsync<DeleteRecordInput>(jsonOptions, ctx.RequestAborted);
            if (body is null) return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing request body."));

            if (body.Repo != did)
                return Results.Json(new PdsErrorResponse("AuthorizationError", "Cannot write to another user's repo."), statusCode: 403);

            await pds.DeleteRecordAsync(did, body.Collection, body.Rkey, ctx.RequestAborted);
            return Results.Ok();
        }));

        Map(PdsEndpointNames.ListRecords, route => endpoints.MapGet(route, async (HttpContext ctx, PdsService pds) =>
        {
            var repo = ctx.Request.Query["repo"].ToString();
            var collection = ctx.Request.Query["collection"].ToString();

            if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(collection))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing required parameters."));

            int.TryParse(ctx.Request.Query["limit"], out var limit);
            if (limit <= 0) limit = 50;
            if (limit > 100) limit = 100;

            var cursor = ctx.Request.Query["cursor"].ToString();
            bool.TryParse(ctx.Request.Query["reverse"], out var reverse);

            var page = await pds.ListRecordsAsync(repo, collection, limit,
                string.IsNullOrEmpty(cursor) ? null : cursor, reverse, ctx.RequestAborted);

            var response = new
            {
                records = page.Records.Select(r => new { uri = $"at://{r.Did}/{r.Collection}/{r.Rkey}", cid = r.Cid, value = r.Value }),
                cursor = page.Cursor,
            };

            return Results.Json(response, jsonOptions);
        }));

        // ── Blob endpoints ──

        Map(PdsEndpointNames.UploadBlob, route => endpoints.MapPost(route, async (HttpContext ctx, PdsService pds, PdsSessionService sessions, PdsOptions pdsOptions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            var contentType = ctx.Request.ContentType ?? "application/octet-stream";

            // Reject oversized uploads before allocating: prefer Content-Length when present
            // and otherwise stream-copy with a hard ceiling so a hostile client can't OOM us.
            var maxSize = pdsOptions.MaxBlobSize;
            if (ctx.Request.ContentLength is { } declared && declared > maxSize)
            {
                return Results.Json(
                    new PdsErrorResponse("BlobTooLarge", $"Blob size {declared} exceeds maximum of {maxSize} bytes."),
                    statusCode: 413);
            }

            var data = await ReadBoundedBodyAsync(ctx.Request.Body, maxSize, ctx.RequestAborted);
            if (data is null)
            {
                return Results.Json(
                    new PdsErrorResponse("BlobTooLarge", $"Blob size exceeds maximum of {maxSize} bytes."),
                    statusCode: 413);
            }

            try
            {
                var result = await pds.UploadBlobAsync(did, data, contentType, ctx.RequestAborted);
                return Results.Json(new { blob = result }, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 400);
            }
        }));

        Map(PdsEndpointNames.GetBlob, route => endpoints.MapGet(route, async (HttpContext ctx, PdsService pds) =>
        {
            var repoDid = ctx.Request.Query["did"].ToString();
            var cid = ctx.Request.Query["cid"].ToString();

            if (string.IsNullOrEmpty(repoDid) || string.IsNullOrEmpty(cid))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing required parameters."));

            var blob = await pds.GetBlobAsync(repoDid, cid, ctx.RequestAborted);
            if (blob is null)
                return Results.Json(new PdsErrorResponse("BlobNotFound", "Blob not found."), statusCode: 400);

            return Results.File(blob.Data, blob.MimeType);
        }));

        return endpoints;
    }

    /// <summary>
    /// Stream-copies a request body into a byte array, aborting once <paramref name="maxBytes"/>
    /// is exceeded. Returns <c>null</c> if the body is too large.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedBodyAsync(Stream body, long maxBytes, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var read = await body.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;

                if (ms.Length + read > maxBytes)
                    return null;

                ms.Write(buffer, 0, read);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Extracts the DID from a Bearer token, enforcing that the token carries the
    /// expected scope. Defaults to the access-token scope ("atproto"); pass
    /// "com.atproto.refresh" to gate the refresh endpoint so refresh tokens cannot
    /// be replayed as access tokens (or vice versa).
    /// </summary>
    private static Task<string?> ExtractDidFromTokenAsync(
        HttpContext ctx,
        PdsSessionService sessions,
        string requiredScope = "atproto")
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<string?>(null);

        var token = authHeader["Bearer ".Length..];
        var result = sessions.ValidateToken(token);
        if (result is null || !result.IsValid) return Task.FromResult<string?>(null);

        // OAuth scope is a space-separated set (RFC 6749 §3.3); the required scope
        // must be one of its members, not the full string.
        if (!HasScope(result.Scope, requiredScope))
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(result.Did);
    }

    private static bool HasScope(string? tokenScope, string requiredScope)
    {
        if (string.IsNullOrEmpty(tokenScope)) return false;
        foreach (var part in tokenScope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, requiredScope, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ── Input DTOs for endpoint deserialization ──

    internal sealed class CreateAccountInput
    {
        public string Handle { get; set; } = "";
        public string? Email { get; set; }
        public string Password { get; set; } = "";
        public string? Did { get; set; }
        public string? InviteCode { get; set; }
    }

    internal sealed class CreateSessionInput
    {
        public string Identifier { get; set; } = "";
        public string Password { get; set; } = "";
    }

    internal sealed class CreateRecordInput
    {
        public string Repo { get; set; } = "";
        public string Collection { get; set; } = "";
        public JsonElement Record { get; set; }
        public string? Rkey { get; set; }
    }

    internal sealed class PutRecordInput
    {
        public string Repo { get; set; } = "";
        public string Collection { get; set; } = "";
        public string Rkey { get; set; } = "";
        public JsonElement Record { get; set; }
    }

    internal sealed class DeleteRecordInput
    {
        public string Repo { get; set; } = "";
        public string Collection { get; set; } = "";
        public string Rkey { get; set; } = "";
    }

    internal sealed class PdsErrorResponse
    {
        public string Error { get; }
        public string Message { get; }

        public PdsErrorResponse(string error, string message)
        {
            Error = error;
            Message = message;
        }
    }
}
