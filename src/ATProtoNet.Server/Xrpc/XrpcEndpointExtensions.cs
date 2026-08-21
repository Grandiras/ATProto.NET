using System.Reflection;
using System.Text.Json;
using ATProtoNet.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Extension methods for registering and mapping XRPC endpoint handlers.
/// </summary>
public static class XrpcEndpointExtensions
{
    /// <summary>
    /// Registers a single XRPC endpoint handler in the DI container.
    /// </summary>
    /// <typeparam name="THandler">The endpoint handler type implementing one of the XRPC interfaces.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXrpcEndpoint<THandler>(this IServiceCollection services)
        where THandler : class, IXrpcEndpoint
    {
        EnsureRegistry(services);
        services.AddScoped<THandler>();
        var registry = GetOrCreateRegistry(services);
        RegisterEndpointType(registry, typeof(THandler));
        return services;
    }

    /// <summary>
    /// Scans the specified assembly for classes decorated with <see cref="XrpcEndpointAttribute"/>
    /// and registers them as XRPC endpoint handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXrpcEndpointsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        EnsureRegistry(services);

        var registry = GetOrCreateRegistry(services);
        var endpointTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && t.GetCustomAttribute<XrpcEndpointAttribute>() is not null
                        && t.IsAssignableTo(typeof(IXrpcEndpoint)));

        foreach (var type in endpointTypes)
        {
            services.AddScoped(type);
            RegisterEndpointType(registry, type);
        }

        return services;
    }

    /// <summary>
    /// Maps all registered XRPC endpoints as ASP.NET Core minimal API routes
    /// at <c>/xrpc/{nsid}</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapXrpcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<XrpcEndpointRegistry>();

        foreach (var registration in registry.Registrations)
        {
            registration.MapDelegate(endpoints, registration);
        }

        return endpoints;
    }

    private static void EnsureRegistry(IServiceCollection services)
    {
        services.TryAddSingleton<XrpcEndpointRegistry>();
    }

    private static XrpcEndpointRegistry GetOrCreateRegistry(IServiceCollection services)
    {
        // Look for existing registry in singleton descriptors
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(XrpcEndpointRegistry) &&
            d.ImplementationInstance is not null);

        if (descriptor?.ImplementationInstance is XrpcEndpointRegistry existing)
            return existing;

        // Create and register as instance
        var registry = new XrpcEndpointRegistry();
        services.Replace(ServiceDescriptor.Singleton(registry));
        return registry;
    }

    private static void RegisterEndpointType(XrpcEndpointRegistry registry, Type handlerType)
    {
        var interfaces = handlerType.GetInterfaces();

        foreach (var iface in interfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var genericDef = iface.GetGenericTypeDefinition();

            if (genericDef == typeof(IXrpcQuery<,>))
            {
                var typeArgs = iface.GetGenericArguments();
                registry.Registrations.Add(new XrpcEndpointRegistration
                {
                    HandlerType = handlerType,
                    ParamsType = typeArgs[0],
                    OutputType = typeArgs[1],
                    Kind = XrpcEndpointKind.QueryWithParams,
                    MapDelegate = MapQueryWithParams,
                });
            }
            else if (genericDef == typeof(IXrpcQuery<>))
            {
                var typeArgs = iface.GetGenericArguments();
                registry.Registrations.Add(new XrpcEndpointRegistration
                {
                    HandlerType = handlerType,
                    OutputType = typeArgs[0],
                    Kind = XrpcEndpointKind.Query,
                    MapDelegate = MapQuery,
                });
            }
            else if (genericDef == typeof(IXrpcBlobQuery<>))
            {
                var typeArgs = iface.GetGenericArguments();
                registry.Registrations.Add(new XrpcEndpointRegistration
                {
                    HandlerType = handlerType,
                    ParamsType = typeArgs[0],
                    Kind = XrpcEndpointKind.BlobQuery,
                    MapDelegate = MapBlobQuery,
                });
            }
            else if (genericDef == typeof(IXrpcProcedure<,>))
            {
                var typeArgs = iface.GetGenericArguments();
                registry.Registrations.Add(new XrpcEndpointRegistration
                {
                    HandlerType = handlerType,
                    InputType = typeArgs[0],
                    OutputType = typeArgs[1],
                    Kind = XrpcEndpointKind.ProcedureWithOutput,
                    MapDelegate = MapProcedureWithOutput,
                });
            }
            else if (genericDef == typeof(IXrpcProcedureVoid<>))
            {
                var typeArgs = iface.GetGenericArguments();
                registry.Registrations.Add(new XrpcEndpointRegistration
                {
                    HandlerType = handlerType,
                    InputType = typeArgs[0],
                    Kind = XrpcEndpointKind.ProcedureVoid,
                    MapDelegate = MapProcedureVoid,
                });
            }
        }
    }

    private static void MapQueryWithParams(IEndpointRouteBuilder endpoints, XrpcEndpointRegistration reg)
    {
        var method = typeof(XrpcEndpointExtensions)
            .GetMethod(nameof(MapQueryWithParamsGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(reg.HandlerType, reg.ParamsType!, reg.OutputType!);

        method.Invoke(null, [endpoints]);
    }

    private static void MapQueryWithParamsGeneric<THandler, TParams, TOutput>(IEndpointRouteBuilder endpoints)
        where THandler : class, IXrpcQuery<TParams, TOutput>
        where TParams : class
        where TOutput : class
    {
        var nsid = GetNsidFromType<THandler>();

        endpoints.MapGet($"/xrpc/{nsid}", async (HttpContext context, THandler handler, CancellationToken ct) =>
        {
            try
            {
                var parameters = BindQueryParameters<TParams>(context.Request.Query);
                var result = await handler.HandleAsync(parameters, context, ct);
                return Results.Json(result, AtProtoJsonDefaults.Options);
            }
            catch (XrpcException ex)
            {
                return WriteError(context, ex);
            }
        });
    }

    private static void MapQuery(IEndpointRouteBuilder endpoints, XrpcEndpointRegistration reg)
    {
        var method = typeof(XrpcEndpointExtensions)
            .GetMethod(nameof(MapQueryGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(reg.HandlerType, reg.OutputType!);

        method.Invoke(null, [endpoints]);
    }

    private static void MapQueryGeneric<THandler, TOutput>(IEndpointRouteBuilder endpoints)
        where THandler : class, IXrpcQuery<TOutput>
        where TOutput : class
    {
        var nsid = GetNsidFromType<THandler>();

        endpoints.MapGet($"/xrpc/{nsid}", async (HttpContext context, THandler handler, CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(context, ct);
                return Results.Json(result, AtProtoJsonDefaults.Options);
            }
            catch (XrpcException ex)
            {
                return WriteError(context, ex);
            }
        });
    }

    private static void MapProcedureWithOutput(IEndpointRouteBuilder endpoints, XrpcEndpointRegistration reg)
    {
        var method = typeof(XrpcEndpointExtensions)
            .GetMethod(nameof(MapProcedureWithOutputGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(reg.HandlerType, reg.InputType!, reg.OutputType!);

        method.Invoke(null, [endpoints]);
    }

    private static void MapProcedureWithOutputGeneric<THandler, TInput, TOutput>(IEndpointRouteBuilder endpoints)
        where THandler : class, IXrpcProcedure<TInput, TOutput>
        where TInput : class
        where TOutput : class
    {
        var nsid = GetNsidFromType<THandler>();

        endpoints.MapPost($"/xrpc/{nsid}", async (HttpContext context, THandler handler, CancellationToken ct) =>
        {
            TInput? input;
            try
            {
                input = await context.Request.ReadFromJsonAsync<TInput>(AtProtoJsonDefaults.Options, ct);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "Invalid or missing request body" });
            }

            if (input is null)
                return Results.BadRequest(new { error = "InvalidRequest", message = "Request body is required" });

            try
            {
                var result = await handler.HandleAsync(input, context, ct);
                return Results.Json(result, AtProtoJsonDefaults.Options);
            }
            catch (XrpcException ex)
            {
                return WriteError(context, ex);
            }
        });
    }

    private static void MapProcedureVoid(IEndpointRouteBuilder endpoints, XrpcEndpointRegistration reg)
    {
        var method = typeof(XrpcEndpointExtensions)
            .GetMethod(nameof(MapProcedureVoidGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(reg.HandlerType, reg.InputType!);

        method.Invoke(null, [endpoints]);
    }

    private static void MapProcedureVoidGeneric<THandler, TInput>(IEndpointRouteBuilder endpoints)
        where THandler : class, IXrpcProcedureVoid<TInput>
        where TInput : class
    {
        var nsid = GetNsidFromType<THandler>();

        endpoints.MapPost($"/xrpc/{nsid}", async (HttpContext context, THandler handler, CancellationToken ct) =>
        {
            TInput? input;
            try
            {
                input = await context.Request.ReadFromJsonAsync<TInput>(AtProtoJsonDefaults.Options, ct);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "Invalid or missing request body" });
            }

            if (input is null)
                return Results.BadRequest(new { error = "InvalidRequest", message = "Request body is required" });

            try
            {
                await handler.HandleAsync(input, context, ct);
                return Results.Ok();
            }
            catch (XrpcException ex)
            {
                return WriteError(context, ex);
            }
        });
    }

    private static void MapBlobQuery(IEndpointRouteBuilder endpoints, XrpcEndpointRegistration reg)
    {
        var method = typeof(XrpcEndpointExtensions)
            .GetMethod(nameof(MapBlobQueryGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(reg.HandlerType, reg.ParamsType!);

        method.Invoke(null, [endpoints]);
    }

    private static void MapBlobQueryGeneric<THandler, TParams>(IEndpointRouteBuilder endpoints)
        where THandler : class, IXrpcBlobQuery<TParams>
        where TParams : class
    {
        var nsid = GetNsidFromType<THandler>();

        endpoints.MapGet($"/xrpc/{nsid}", async (HttpContext context, THandler handler, CancellationToken ct) =>
        {
            XrpcBlobResult blob;
            try
            {
                var parameters = BindQueryParameters<TParams>(context.Request.Query);
                blob = await handler.HandleAsync(parameters, context, ct);
            }
            catch (XrpcException ex)
            {
                return WriteError(context, ex);
            }

            // The body is streamed rather than buffered — a repo CAR is arbitrarily large — so
            // the handler's stream is handed to Results.Stream, which disposes it after writing.
            if (blob.ContentLength is { } length)
                context.Response.ContentLength = length;

            return Results.Stream(blob.Content, blob.ContentType);
        });
    }

    /// <summary>
    /// Writes an <see cref="XrpcException"/> as the <c>{"error", "message"}</c> body XRPC
    /// clients branch on, plus any headers the error carries.
    /// </summary>
    private static IResult WriteError(HttpContext context, XrpcException exception)
    {
        foreach (var (name, value) in exception.Headers)
            context.Response.Headers[name] = value;

        return Results.Json(
            new XrpcErrorBody { Error = exception.Error, Message = exception.Message },
            AtProtoJsonDefaults.Options,
            statusCode: exception.StatusCode);
    }

    private static string GetNsidFromType<THandler>() where THandler : IXrpcEndpoint
    {
        // Try to get NSID from attribute first
        var attr = typeof(THandler).GetCustomAttribute<XrpcEndpointAttribute>();
        if (!string.IsNullOrEmpty(attr?.Nsid))
            return attr.Nsid;

        // Use RuntimeHelpers to get the NSID from an uninitialized instance
        var uninitObj = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(THandler));
        if (uninitObj is IXrpcEndpoint endpoint)
        {
            var nsid = endpoint.Nsid;
            if (!string.IsNullOrEmpty(nsid))
                return nsid;
        }

        throw new InvalidOperationException(
            $"Could not determine NSID for XRPC endpoint handler '{typeof(THandler).Name}'. " +
            $"Set the Nsid property on the [XrpcEndpoint] attribute or implement {nameof(IXrpcEndpoint)}.{nameof(IXrpcEndpoint.Nsid)}.");
    }

    /// <summary>
    /// Binds query string parameters to a typed object using System.Text.Json conventions.
    /// </summary>
    private static TParams BindQueryParameters<TParams>(IQueryCollection query) where TParams : class
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kvp in query)
        {
            if (kvp.Value.Count > 1)
                dict[kvp.Key] = kvp.Value.ToArray();
            else
                dict[kvp.Key] = kvp.Value.ToString();
        }

        var json = JsonSerializer.Serialize(dict);
        try
        {
            return JsonSerializer.Deserialize<TParams>(json, AtProtoJsonDefaults.Options)
                   ?? throw new XrpcException("InvalidRequest", "Could not bind query parameters.");
        }
        catch (JsonException ex)
        {
            throw new XrpcException("InvalidRequest", $"Could not bind query parameters: {ex.Message}");
        }
    }
}

/// <summary>
/// Internal registry for XRPC endpoint registrations.
/// </summary>
internal sealed class XrpcEndpointRegistry
{
    public List<XrpcEndpointRegistration> Registrations { get; } = [];
}

internal sealed class XrpcEndpointRegistration
{
    public required Type HandlerType { get; init; }
    public Type? ParamsType { get; init; }
    public Type? InputType { get; init; }
    public Type? OutputType { get; init; }
    public required XrpcEndpointKind Kind { get; init; }
    public required Action<IEndpointRouteBuilder, XrpcEndpointRegistration> MapDelegate { get; init; }
}

internal enum XrpcEndpointKind
{
    Query,
    QueryWithParams,
    BlobQuery,
    ProcedureWithOutput,
    ProcedureVoid,
}

/// <summary>The XRPC error wire body: a name clients branch on, and a description for humans.</summary>
internal sealed class XrpcErrorBody
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public required string Error { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public required string Message { get; init; }
}
