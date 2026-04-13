using System.Text.Json;
using ATProtoNet.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<PdsSessionService>();
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
        services.AddSingleton<PdsSessionService>();
        services.AddSingleton<PdsService>();

        services.AddSingleton<IAccountStore, TAccountStore>();
        services.AddSingleton<IRepoStore, TRepoStore>();

        return services;
    }

    /// <summary>
    /// Map the core AT Protocol XRPC endpoints for a PDS.
    /// This maps the com.atproto.server.*, com.atproto.repo.*, and com.atproto.sync.* endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static IEndpointRouteBuilder MapAtProtoPds(this IEndpointRouteBuilder endpoints)
    {
        var jsonOptions = AtProtoJsonDefaults.Options;

        // ── Server endpoints ──

        endpoints.MapPost("/xrpc/com.atproto.server.createAccount", async (HttpContext ctx, PdsService pds) =>
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
        });

        endpoints.MapPost("/xrpc/com.atproto.server.createSession", async (HttpContext ctx, PdsService pds) =>
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
        });

        endpoints.MapGet("/xrpc/com.atproto.server.getSession", async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
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
        });

        endpoints.MapPost("/xrpc/com.atproto.server.refreshSession", async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
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
        });

        endpoints.MapGet("/xrpc/com.atproto.server.describeServer", (PdsService pds) =>
        {
            var description = pds.DescribeServer();
            return Results.Json(description, jsonOptions);
        });

        // ── Repo endpoints ──

        endpoints.MapPost("/xrpc/com.atproto.repo.createRecord", async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
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
        });

        endpoints.MapGet("/xrpc/com.atproto.repo.getRecord", async (HttpContext ctx, PdsService pds) =>
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
        });

        endpoints.MapPost("/xrpc/com.atproto.repo.putRecord", async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
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
        });

        endpoints.MapPost("/xrpc/com.atproto.repo.deleteRecord", async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            var body = await ctx.Request.ReadFromJsonAsync<DeleteRecordInput>(jsonOptions, ctx.RequestAborted);
            if (body is null) return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing request body."));

            if (body.Repo != did)
                return Results.Json(new PdsErrorResponse("AuthorizationError", "Cannot write to another user's repo."), statusCode: 403);

            await pds.DeleteRecordAsync(did, body.Collection, body.Rkey, ctx.RequestAborted);
            return Results.Ok();
        });

        endpoints.MapGet("/xrpc/com.atproto.repo.listRecords", async (HttpContext ctx, PdsService pds) =>
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
        });

        // ── Blob endpoints ──

        endpoints.MapPost("/xrpc/com.atproto.repo.uploadBlob", async (HttpContext ctx, PdsService pds, PdsSessionService sessions) =>
        {
            var did = await ExtractDidFromTokenAsync(ctx, sessions);
            if (did is null) return Results.Json(new PdsErrorResponse("AuthenticationRequired", "Invalid or missing token."), statusCode: 401);

            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
            var data = ms.ToArray();

            try
            {
                var result = await pds.UploadBlobAsync(did, data, contentType, ctx.RequestAborted);
                return Results.Json(new { blob = result }, jsonOptions);
            }
            catch (PdsException ex)
            {
                return Results.Json(new PdsErrorResponse(ex.ErrorCode, ex.Message), statusCode: 400);
            }
        });

        endpoints.MapGet("/xrpc/com.atproto.sync.getBlob", async (HttpContext ctx, PdsService pds) =>
        {
            var repoDid = ctx.Request.Query["did"].ToString();
            var cid = ctx.Request.Query["cid"].ToString();

            if (string.IsNullOrEmpty(repoDid) || string.IsNullOrEmpty(cid))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing required parameters."));

            var blob = await pds.GetBlobAsync(repoDid, cid, ctx.RequestAborted);
            if (blob is null)
                return Results.Json(new PdsErrorResponse("BlobNotFound", "Blob not found."), statusCode: 400);

            return Results.File(blob.Data, blob.MimeType);
        });

        return endpoints;
    }

    private static Task<string?> ExtractDidFromTokenAsync(HttpContext ctx, PdsSessionService sessions)
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<string?>(null);

        var token = authHeader["Bearer ".Length..];
        var result = sessions.ValidateToken(token);
        if (result is null || !result.IsValid) return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(result.Did);
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
