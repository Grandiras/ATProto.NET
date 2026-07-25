using System.Net.WebSockets;
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
        services.AddSingleton(CreatePdsService);

        // Default in-memory stores; can be replaced by calling
        // AddAtProtoPds<TAccountStore, TRepoStore>() instead, or by registering
        // a store beforehand (e.g. AddAtProtoPdsEfCoreStores<TContext>()) — TryAdd
        // leaves an existing registration alone rather than shadowing it.
        services.TryAddSingleton<IAccountStore, InMemoryAccountStore>();
        services.TryAddSingleton<IRepoStore, InMemoryRepoStore>();

        AddFederationServices(services, options);
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
        services.AddSingleton(CreatePdsService);

        services.AddSingleton<IAccountStore, TAccountStore>();
        services.AddSingleton<IRepoStore, TRepoStore>();

        AddFederationServices(services, options);
        return services;
    }

    /// <summary>
    /// Register PDS hosting services with custom store implementations, including a durable
    /// store for repository heads.
    /// </summary>
    /// <typeparam name="TAccountStore">The account store implementation.</typeparam>
    /// <typeparam name="TRepoStore">The record and blob store implementation.</typeparam>
    /// <typeparam name="TCommitStore">The repository head store implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional PDS configuration.</param>
    /// <remarks>
    /// A federating PDS should persist repository heads: if the head store is lost, the next
    /// commit starts a fresh revision sequence and relays see the repository rewind.
    /// </remarks>
    public static IServiceCollection AddAtProtoPds<TAccountStore, TRepoStore, TCommitStore>(
        this IServiceCollection services,
        Action<PdsOptions>? configure = null)
        where TAccountStore : class, IAccountStore
        where TRepoStore : class, IRepoStore
        where TCommitStore : class, IRepoCommitStore
    {
        services.AddSingleton<IRepoCommitStore, TCommitStore>();
        return services.AddAtProtoPds<TAccountStore, TRepoStore>(configure);
    }

    /// <summary>
    /// Registers the services that make a PDS federate: the firehose sequencer, the commit
    /// engine, identity minting, and the relay crawl notifier.
    /// </summary>
    private static void AddFederationServices(IServiceCollection services, PdsOptions options)
    {
        // TryAdd throughout, so a host that registered its own head store — or called the
        // three-generic overload — keeps it.
        services.TryAddSingleton<IRepoCommitStore, InMemoryRepoCommitStore>();
        services.TryAddSingleton(_ => new PdsSequencer(options.FirehoseBacklogCapacity));
        services.TryAddSingleton(sp => new PdsRepoManager(
            sp.GetRequiredService<IAccountStore>(),
            sp.GetRequiredService<IRepoStore>(),
            sp.GetRequiredService<IRepoCommitStore>(),
            sp.GetRequiredService<PdsSequencer>(),
            options,
            sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(PdsRepoManager).FullName!)));
        services.TryAddSingleton(sp => new PdsIdentityService(
            options, sp.GetRequiredService<IAccountStore>()));
        services.TryAddSingleton(_ => new PdsCrawlNotifier(options));
    }

    /// <summary>
    /// Builds the singleton <see cref="PdsService"/>.
    /// <para>
    /// Registered as an explicit factory rather than by type: <see cref="PdsService"/> has both a
    /// federating and a non-federating constructor, and leaving the choice to the container's
    /// "greediest constructor that resolves" heuristic would make which one runs depend on what
    /// else happens to be registered. Federation services are always registered, so this always
    /// picks the federating constructor — but it says so, instead of implying it.
    /// </para>
    /// </summary>
    private static PdsService CreatePdsService(IServiceProvider services)
        => new(
            services.GetRequiredService<IAccountStore>(),
            services.GetRequiredService<IRepoStore>(),
            services.GetRequiredService<PdsSessionService>(),
            services.GetRequiredService<PdsOptions>(),
            services.GetRequiredService<PdsRepoManager>(),
            services.GetRequiredService<PdsIdentityService>());

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

        // ── Identity endpoints ──

        Map(PdsEndpointNames.ResolveHandle, route => endpoints.MapGet(route, async (HttpContext ctx, PdsIdentityService identity) =>
        {
            var handle = ctx.Request.Query["handle"].ToString();
            if (string.IsNullOrEmpty(handle))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing 'handle' parameter."));

            var did = await identity.ResolveHandleAsync(handle, ctx.RequestAborted);
            if (did is null)
                return Results.Json(new PdsErrorResponse("HandleNotFound", "Handle not found."), statusCode: 400);

            return Results.Json(new { did }, jsonOptions);
        }));

        // ── Sync endpoints ──

        Map(PdsEndpointNames.GetRepo, route => endpoints.MapGet(route, async (HttpContext ctx, PdsRepoManager repos) =>
        {
            var did = ctx.Request.Query["did"].ToString();
            if (string.IsNullOrEmpty(did))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing 'did' parameter."));

            var car = await repos.ExportRepoAsync(did, ctx.RequestAborted);
            if (car is null)
                return Results.Json(new PdsErrorResponse("RepoNotFound", "Repository not found."), statusCode: 400);

            return Results.Bytes(car, CarContentType);
        }));

        Map(PdsEndpointNames.GetLatestCommit, route => endpoints.MapGet(route, async (HttpContext ctx, PdsRepoManager repos) =>
        {
            var did = ctx.Request.Query["did"].ToString();
            if (string.IsNullOrEmpty(did))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing 'did' parameter."));

            var head = await repos.GetHeadAsync(did, ctx.RequestAborted);
            if (head is null)
                return Results.Json(new PdsErrorResponse("RepoNotFound", "Repository not found."), statusCode: 400);

            return Results.Json(new { cid = head.CommitCid, rev = head.Rev }, jsonOptions);
        }));

        Map(PdsEndpointNames.GetRepoStatus, route => endpoints.MapGet(route, async (HttpContext ctx, PdsService pds, PdsRepoManager repos) =>
        {
            var did = ctx.Request.Query["did"].ToString();
            if (string.IsNullOrEmpty(did))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing 'did' parameter."));

            PdsSessionInfo session;
            try
            {
                session = await pds.GetSessionAsync(did, ctx.RequestAborted);
            }
            catch (PdsException)
            {
                return Results.Json(new PdsErrorResponse("RepoNotFound", "Repository not found."), statusCode: 400);
            }

            var head = await repos.GetHeadAsync(did, ctx.RequestAborted);
            return Results.Json(new
            {
                did,
                active = session.Active,
                status = session.Active ? null : "deactivated",
                rev = head?.Rev,
            }, jsonOptions);
        }));

        Map(PdsEndpointNames.ListRepos, route => endpoints.MapGet(route, async (HttpContext ctx, PdsRepoManager repos) =>
        {
            int.TryParse(ctx.Request.Query["limit"], out var limit);
            if (limit <= 0) limit = 500;
            if (limit > 1000) limit = 1000;

            var cursor = ctx.Request.Query["cursor"].ToString();
            var page = await repos.ListRepoListingsAsync(limit,
                string.IsNullOrEmpty(cursor) ? null : cursor, ctx.RequestAborted);

            return Results.Json(new
            {
                repos = page.Select(r => new
                {
                    did = r.Head.Did,
                    head = r.Head.CommitCid,
                    rev = r.Head.Rev,
                    active = r.Active,
                    status = r.Status,
                }),
                cursor = page.Count == limit ? page[^1].Head.Did : null,
            }, jsonOptions);
        }));

        Map(PdsEndpointNames.SyncGetRecord, route => endpoints.MapGet(route, async (HttpContext ctx, PdsRepoManager repos) =>
        {
            var did = ctx.Request.Query["did"].ToString();
            var collection = ctx.Request.Query["collection"].ToString();
            var rkey = ctx.Request.Query["rkey"].ToString();

            if (string.IsNullOrEmpty(did) || string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(rkey))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing required parameters."));

            var car = await repos.ExportRecordProofAsync(did, collection, rkey, ctx.RequestAborted);
            if (car is null)
                return Results.Json(new PdsErrorResponse("RepoNotFound", "Repository not found."), statusCode: 400);

            return Results.Bytes(car, CarContentType);
        }));

        Map(PdsEndpointNames.GetBlocks, route => endpoints.MapGet(route, async (HttpContext ctx, PdsRepoManager repos) =>
        {
            var did = ctx.Request.Query["did"].ToString();
            var cids = ctx.Request.Query["cids"].Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).ToList();

            if (string.IsNullOrEmpty(did) || cids.Count == 0)
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing 'did' or 'cids' parameters."));

            var car = await repos.ExportBlocksAsync(did, cids, ctx.RequestAborted);
            if (car is null)
                return Results.Json(new PdsErrorResponse("RepoNotFound", "Repository not found."), statusCode: 400);

            return Results.Bytes(car, CarContentType);
        }));

        Map(PdsEndpointNames.ListBlobs, route => endpoints.MapGet(route, async (HttpContext ctx, IRepoStore store) =>
        {
            var did = ctx.Request.Query["did"].ToString();
            if (string.IsNullOrEmpty(did))
                return Results.BadRequest(new PdsErrorResponse("InvalidRequest", "Missing 'did' parameter."));

            int.TryParse(ctx.Request.Query["limit"], out var limit);
            if (limit <= 0) limit = 500;
            if (limit > 1000) limit = 1000;

            var cursor = ctx.Request.Query["cursor"].ToString();

            var all = await store.ListBlobCidsAsync(did, ctx.RequestAborted);
            IEnumerable<string> filtered = all;
            if (!string.IsNullOrEmpty(cursor))
                filtered = filtered.Where(c => string.CompareOrdinal(c, cursor) > 0);

            var page = filtered.Take(limit).ToList();
            return Results.Json(new
            {
                cids = page,
                cursor = page.Count == limit ? page[^1] : null,
            }, jsonOptions);
        }));

        // ── Firehose ──

        Map(PdsEndpointNames.SubscribeRepos, route => endpoints.MapGet(route, async (HttpContext ctx, PdsSequencer sequencer) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                return Results.Json(
                    new PdsErrorResponse("InvalidRequest", "com.atproto.sync.subscribeRepos requires a WebSocket connection."),
                    statusCode: 426);
            }

            long? cursor = null;
            var cursorText = ctx.Request.Query["cursor"].ToString();
            if (!string.IsNullOrEmpty(cursorText))
            {
                if (!long.TryParse(cursorText, out var parsed) || parsed < 0)
                {
                    return Results.Json(
                        new PdsErrorResponse("InvalidRequest", "Cursor must be a non-negative integer."),
                        statusCode: 400);
                }
                cursor = parsed;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await StreamFirehoseAsync(socket, sequencer, cursor, ctx.RequestAborted);
            return Results.Empty;
        }));

        // ── Well-known identity documents ──

        var pdsOptions = endpoints.ServiceProvider.GetService<PdsOptions>();

        if (pdsOptions?.ServeWellKnownHandle != false)
        {
            endpoints.MapGet("/.well-known/atproto-did", async (HttpContext ctx, PdsIdentityService identity) =>
            {
                // The handle being resolved is the host the request arrived on — that is what
                // makes this endpoint authoritative for handle→DID resolution.
                var did = await identity.ResolveHandleAsync(ctx.Request.Host.Host, ctx.RequestAborted);
                return did is null ? Results.NotFound() : Results.Text(did, "text/plain");
            }).WithDisplayName("atproto-did");
        }

        if (pdsOptions?.ServeWellKnownDidDocument != false)
        {
            endpoints.MapGet("/.well-known/did.json", async (HttpContext ctx, PdsIdentityService identity) =>
            {
                var document = await identity.GetWebDidDocumentAsync(ctx.Request.Host.Host, ctx.RequestAborted);
                return document is null
                    ? Results.NotFound()
                    : Results.Json(document, jsonOptions, contentType: "application/did+ld+json");
            }).WithDisplayName("did.json");
        }

        return endpoints;
    }

    /// <summary>The IANA media type for CAR files.</summary>
    private const string CarContentType = "application/vnd.ipld.car";

    /// <summary>
    /// Streams sequenced firehose frames to a subscribed relay until it disconnects.
    /// </summary>
    private static async Task StreamFirehoseAsync(
        WebSocket socket, PdsSequencer sequencer, long? cursor, CancellationToken cancellationToken)
    {
        if (cursor is { } requested)
        {
            // A cursor past our own sequence means the consumer is talking to a different (or
            // reset) instance; per the lexicon this is a terminal FutureCursor error, not
            // something to silently start streaming over.
            if (requested > sequencer.CurrentSeq)
            {
                await SendFrameAsync(socket,
                    PdsFirehoseFrame.Error("FutureCursor", "Cursor is ahead of the server's sequence."),
                    cancellationToken);
                await CloseAsync(socket, cancellationToken);
                return;
            }

            var oldest = sequencer.OldestAvailableSeq;
            if (oldest > 0 && requested < oldest - 1)
            {
                await SendFrameAsync(socket,
                    PdsFirehoseFrame.Info("OutdatedCursor", "Requested cursor is older than the retained backlog."),
                    cancellationToken);
            }
        }

        try
        {
            await foreach (var evt in sequencer.SubscribeAsync(cursor, cancellationToken).ConfigureAwait(false))
            {
                if (socket.State != WebSocketState.Open) break;
                await SendFrameAsync(socket, evt.Frame, cancellationToken);
            }

            await CloseAsync(socket, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The client went away; nothing to report.
        }
        catch (WebSocketException)
        {
            // Abrupt disconnects are routine on a long-lived stream.
        }
    }

    private static Task SendFrameAsync(WebSocket socket, byte[] frame, CancellationToken cancellationToken)
        => socket.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

    private static async Task CloseAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
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
