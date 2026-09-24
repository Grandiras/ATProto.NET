using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server.Xrpc;

/// <summary>The XRPC endpoints registered on a service collection, in registration order.</summary>
internal sealed class XrpcEndpointRegistry
{
    private readonly List<XrpcEndpointRegistration> _registrations = [];

    public IReadOnlyList<XrpcEndpointRegistration> Registrations => _registrations;

    /// <summary>
    /// Adds <paramref name="registration"/>. Registering a handler again is a no-op; a second
    /// handler for an NSID that already has one is a configuration error.
    /// </summary>
    /// <remarks>
    /// NSIDs are compared case-insensitively, because that is how routing matches the paths
    /// they become: two NSIDs differing only in case would be one route.
    /// </remarks>
    public void Add(XrpcEndpointRegistration registration)
    {
        foreach (var existing in _registrations)
        {
            if (existing.HandlerType == registration.HandlerType)
                return;

            if (string.Equals(existing.Nsid.Value, registration.Nsid.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Both '{existing.HandlerType}' ({existing.Nsid}) and '{registration.HandlerType}' ({registration.Nsid}) " +
                    "serve the same route. An XRPC method has exactly one handler, and routes match NSIDs case-insensitively.");
            }
        }

        _registrations.Add(registration);
    }
}

/// <summary>
/// One handler, resolved at registration into everything mapping it needs: its route, its HTTP
/// method, the request delegate for its shape, and the metadata its class carries.
/// </summary>
internal sealed class XrpcEndpointRegistration
{
    // One entry per endpoint interface. Each factory is generic over the handler and the
    // interface's own type arguments, so a request runs without reflection; the one
    // MakeGenericMethod call happens here, at registration.
    private static readonly Dictionary<Type, (string HttpMethod, MethodInfo Factory)> Shapes = new()
    {
        [typeof(IXrpcQuery<,>)] = (HttpMethods.Get, Factory(nameof(Query))),
        [typeof(IXrpcQuery<>)] = (HttpMethods.Get, Factory(nameof(QueryWithoutParameters))),
        [typeof(IXrpcBlobQuery<>)] = (HttpMethods.Get, Factory(nameof(BlobQuery))),
        [typeof(IXrpcProcedure<,>)] = (HttpMethods.Post, Factory(nameof(Procedure))),
        [typeof(IXrpcProcedure<>)] = (HttpMethods.Post, Factory(nameof(ProcedureWithoutInput))),
        [typeof(IXrpcProcedureVoid<>)] = (HttpMethods.Post, Factory(nameof(ProcedureVoid))),
        [typeof(IXrpcProcedureVoid)] = (HttpMethods.Post, Factory(nameof(ProcedureVoidWithoutInput))),
        [typeof(IXrpcBlobProcedure<>)] = (HttpMethods.Post, Factory(nameof(BlobProcedure))),
    };

    private XrpcEndpointRegistration(
        Nsid nsid, Type handlerType, string httpMethod, RequestDelegate invoke, object[] metadata)
    {
        Nsid = nsid;
        HandlerType = handlerType;
        HttpMethod = httpMethod;
        Invoke = invoke;
        Metadata = metadata;
    }

    /// <summary>The method served, and the route segment after <c>/xrpc/</c>.</summary>
    public Nsid Nsid { get; }

    /// <summary>The handler class, resolved from the request's services.</summary>
    public Type HandlerType { get; }

    /// <summary><c>GET</c> for a query, <c>POST</c> for a procedure.</summary>
    public string HttpMethod { get; }

    /// <summary>Binds the request, runs the handler, and writes its output. Errors propagate.</summary>
    public RequestDelegate Invoke { get; }

    /// <summary>
    /// The handler class's attributes — <c>[Authorize]</c>, <c>[AllowAnonymous]</c>,
    /// <c>[EnableRateLimiting]</c>, <c>[RequestSizeLimit]</c> — as endpoint metadata.
    /// </summary>
    public object[] Metadata { get; }

    /// <summary>
    /// Reads <typeparamref name="THandler"/>'s NSID and endpoint interface.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The handler implements no endpoint interface, or more than one, or its NSID is null.
    /// </exception>
    public static XrpcEndpointRegistration Create<THandler>()
        where THandler : class, IXrpcEndpoint
    {
        var handlerType = typeof(THandler);

        var nsid = THandler.Nsid ?? throw new InvalidOperationException(
            $"XRPC endpoint handler '{handlerType}' declares a null Nsid.");

        Type? shapeInterface = null;
        foreach (var candidate in handlerType.GetInterfaces())
        {
            if (!Shapes.ContainsKey(Definition(candidate)))
                continue;

            if (shapeInterface is not null)
            {
                throw new InvalidOperationException(
                    $"XRPC endpoint handler '{handlerType}' implements both '{shapeInterface}' and '{candidate}'. " +
                    "A handler serves one method, so it implements one endpoint interface.");
            }

            shapeInterface = candidate;
        }

        if (shapeInterface is null)
        {
            throw new InvalidOperationException(
                $"'{handlerType}' implements none of the XRPC endpoint interfaces " +
                "(IXrpcQuery, IXrpcBlobQuery, IXrpcProcedure, IXrpcProcedureVoid, IXrpcBlobProcedure).");
        }

        var (httpMethod, factory) = Shapes[Definition(shapeInterface)];
        Type[] typeArguments = [handlerType, .. shapeInterface.GenericTypeArguments];
        var invoke = (RequestDelegate)factory.MakeGenericMethod(typeArguments).Invoke(null, null)!;

        return new XrpcEndpointRegistration(
            nsid, handlerType, httpMethod, invoke, handlerType.GetCustomAttributes(inherit: true));
    }

    private static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    private static MethodInfo Factory(string name) =>
        typeof(XrpcEndpointRegistration).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    // ── One request delegate per shape ────────────────────────

    private static RequestDelegate Query<THandler, TParams, TOutput>()
        where THandler : class, IXrpcQuery<TParams, TOutput>
        where TParams : class
        where TOutput : class =>
        async context =>
        {
            var parameters = XrpcQueryBinder<TParams>.Bind(context.Request.Query);
            var output = await Handler<THandler>(context).HandleAsync(parameters, context, context.RequestAborted);
            await WriteJsonAsync(context, output);
        };

    private static RequestDelegate QueryWithoutParameters<THandler, TOutput>()
        where THandler : class, IXrpcQuery<TOutput>
        where TOutput : class =>
        async context =>
        {
            var output = await Handler<THandler>(context).HandleAsync(context, context.RequestAborted);
            await WriteJsonAsync(context, output);
        };

    private static RequestDelegate BlobQuery<THandler, TParams>()
        where THandler : class, IXrpcBlobQuery<TParams>
        where TParams : class =>
        async context =>
        {
            var parameters = XrpcQueryBinder<TParams>.Bind(context.Request.Query);
            var blob = await Handler<THandler>(context).HandleAsync(parameters, context, context.RequestAborted);

            // Streamed rather than buffered: a repo CAR is arbitrarily large.
            await using var content = blob.Content;

            var response = context.Response;
            response.ContentType = blob.ContentType;
            if (blob.ContentLength is { } length)
                response.ContentLength = length;

            await content.CopyToAsync(response.Body, context.RequestAborted);
        };

    private static RequestDelegate Procedure<THandler, TInput, TOutput>()
        where THandler : class, IXrpcProcedure<TInput, TOutput>
        where TInput : class
        where TOutput : class =>
        async context =>
        {
            var input = await ReadJsonBodyAsync<TInput>(context);
            var output = await Handler<THandler>(context).HandleAsync(input, context, context.RequestAborted);
            await WriteJsonAsync(context, output);
        };

    private static RequestDelegate ProcedureWithoutInput<THandler, TOutput>()
        where THandler : class, IXrpcProcedure<TOutput>
        where TOutput : class =>
        async context =>
        {
            var output = await Handler<THandler>(context).HandleAsync(context, context.RequestAborted);
            await WriteJsonAsync(context, output);
        };

    private static RequestDelegate ProcedureVoid<THandler, TInput>()
        where THandler : class, IXrpcProcedureVoid<TInput>
        where TInput : class =>
        async context =>
        {
            var input = await ReadJsonBodyAsync<TInput>(context);
            await Handler<THandler>(context).HandleAsync(input, context, context.RequestAborted);
        };

    private static RequestDelegate ProcedureVoidWithoutInput<THandler>()
        where THandler : class, IXrpcProcedureVoid =>
        context => Handler<THandler>(context).HandleAsync(context, context.RequestAborted);

    private static RequestDelegate BlobProcedure<THandler, TOutput>()
        where THandler : class, IXrpcBlobProcedure<TOutput>
        where TOutput : class =>
        async context =>
        {
            var request = context.Request;
            if (string.IsNullOrEmpty(request.ContentType))
                throw new XrpcException(XrpcErrors.InvalidRequest, "Request encoding (Content-Type) required but not provided.");

            var input = new XrpcBlobInput(request.Body, request.ContentType, request.ContentLength);
            var output = await Handler<THandler>(context).HandleAsync(input, context, context.RequestAborted);
            await WriteJsonAsync(context, output);
        };

    // ── Shared steps ──────────────────────────────────────────

    private static THandler Handler<THandler>(HttpContext context) where THandler : class =>
        context.RequestServices.GetRequiredService<THandler>();

    private static async Task<TInput> ReadJsonBodyAsync<TInput>(HttpContext context)
        where TInput : class
    {
        var request = context.Request;
        if (!request.HasJsonContentType())
        {
            throw new XrpcException(
                XrpcErrors.InvalidRequest,
                request.ContentType is null
                    ? "Request encoding (Content-Type) required but not provided."
                    : $"Wrong request encoding (Content-Type): {request.ContentType}. Expected application/json.");
        }

        try
        {
            return await request.ReadFromJsonAsync(XrpcJson<TInput>.TypeInfo, context.RequestAborted)
                   ?? throw new XrpcException(XrpcErrors.InvalidRequest, "Request body is required.");
        }
        catch (JsonException ex)
        {
            throw new XrpcException(
                XrpcErrors.InvalidRequest,
                ex.Path is null or "$" ? "Invalid or missing request body." : $"Invalid request body at {ex.Path}.",
                ex);
        }
    }

    private static Task WriteJsonAsync<TOutput>(HttpContext context, TOutput? output)
        where TOutput : class
    {
        if (output is null)
            throw new InvalidOperationException($"The XRPC handler returned a null {typeof(TOutput).Name}.");

        // The runtime type, as ASP.NET Core's own JSON results serialize: a handler returning a
        // subclass of its declared output writes the subclass's members.
        return context.Response.WriteAsJsonAsync(
            output, output.GetType(), AtProtoJsonDefaults.Options, context.RequestAborted);
    }
}

/// <summary>The contract the routing reads or writes <typeparamref name="T"/> with.</summary>
/// <remarks>
/// Resolved on first use, not at registration: a contract snapshots the union variants
/// registered on <see cref="LexiconTypeRegistry"/>, and an application may register those after
/// its endpoints.
/// </remarks>
/// <typeparam name="T">The type read or written.</typeparam>
internal static class XrpcJson<T>
{
    /// <summary>The contract for <typeparamref name="T"/> under the SDK's serializer options.</summary>
    public static readonly JsonTypeInfo<T> TypeInfo = (JsonTypeInfo<T>)AtProtoJsonDefaults.Options.GetTypeInfo(typeof(T));
}
