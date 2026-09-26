using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Extension methods for registering and mapping XRPC endpoint handlers.
/// </summary>
public static class XrpcEndpointExtensions
{
    private static readonly MethodInfo AddEndpointMethod =
        typeof(XrpcEndpointExtensions).GetMethod(nameof(AddXrpcEndpoint))!;

    /// <summary>
    /// Registers a single XRPC endpoint handler in the DI container.
    /// </summary>
    /// <typeparam name="THandler">
    /// The handler: a class implementing exactly one of the XRPC endpoint interfaces
    /// (<see cref="IXrpcQuery{TParams, TOutput}"/>, <see cref="IXrpcProcedure{TInput, TOutput}"/>, …).
    /// It is registered as a scoped service and constructed per request.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The handler implements no endpoint interface or more than one, its
    /// <see cref="IXrpcEndpoint.Nsid"/> is null, or another handler already serves its NSID.
    /// Registering the same handler twice is not an error.
    /// </exception>
    public static IServiceCollection AddXrpcEndpoint<THandler>(this IServiceCollection services)
        where THandler : class, IXrpcEndpoint
    {
        ArgumentNullException.ThrowIfNull(services);

        GetOrCreateRegistry(services).Add(XrpcEndpointRegistration.Create<THandler>());
        services.TryAddScoped<THandler>();
        return services;
    }

    /// <summary>
    /// Registers every XRPC endpoint handler in <paramref name="assembly"/>: each non-abstract,
    /// non-generic class implementing <see cref="IXrpcEndpoint"/>, exactly as
    /// <see cref="AddXrpcEndpoint{THandler}"/> registers one.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A handler in the assembly is invalid; see <see cref="AddXrpcEndpoint{THandler}"/>.
    /// </exception>
    public static IServiceCollection AddXrpcEndpointsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                || !type.IsAssignableTo(typeof(IXrpcEndpoint)))
            {
                continue;
            }

            AddEndpointMethod.MakeGenericMethod(type)
                .Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, [services], culture: null);
        }

        return services;
    }

    /// <summary>
    /// Maps every registered XRPC endpoint at <c>/xrpc/{nsid}</c>: a query as <c>GET</c>, a
    /// procedure as <c>POST</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>
    /// The <c>/xrpc</c> route group, so conventions apply to every XRPC endpoint at once:
    /// <c>.RequireAuthorization()</c>, <c>.RequireRateLimiting(…)</c>, <c>.RequireCors(…)</c>,
    /// <c>.WithMetadata(…)</c>.
    /// </returns>
    /// <remarks>
    /// <para>Each endpoint also carries its handler class's attributes as metadata, so
    /// <c>[Authorize]</c>, <c>[AllowAnonymous]</c>, <c>[EnableRateLimiting]</c> and
    /// <c>[RequestSizeLimit]</c> on a handler apply to that endpoint alone. It carries an
    /// <see cref="XrpcMethodMetadata"/> naming its NSID too, which service auth
    /// (<c>AddAtProtoServiceAuth()</c>, <c>[RequireServiceAuth]</c>) binds each token's
    /// <c>lxm</c> to.</para>
    /// <para>Every failure is answered with the XRPC error envelope: an
    /// <see cref="ATProtoNet.Http.XrpcException"/> with its own status, error name and headers,
    /// and anything else with <c>500 InternalServerError</c>, logged under
    /// <c>ATProtoNet.Server.Xrpc</c> and without the exception's message.</para>
    /// <para>The group also holds a fallback for <c>/xrpc/{nsid}</c> that no endpoint matched:
    /// <c>501 MethodNotImplemented</c> for an NSID nothing serves, <c>405</c> (error
    /// <c>InvalidRequest</c>, with <c>Allow</c>) for a registered NSID called with the wrong HTTP
    /// method, and <c>400 InvalidRequest</c> for a path segment that is not an NSID. Group
    /// conventions apply to it as well, so an unauthenticated caller of a group that requires
    /// authorization gets the scheme's challenge rather than a list of what is served.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No XRPC endpoint was registered.</exception>
    public static RouteGroupBuilder MapXrpcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetService<XrpcEndpointRegistry>()
                       ?? throw new InvalidOperationException(
                           $"No XRPC endpoints are registered. Call {nameof(AddXrpcEndpoint)}<THandler>() " +
                           $"or {nameof(AddXrpcEndpointsFromAssembly)}() on the service collection first.");

        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(XrpcErrorResponse.LoggerCategory)
                     ?? NullLogger.Instance;

        var group = endpoints.MapGroup("/xrpc");

        foreach (var registration in registry.Registrations)
        {
            group.MapMethods($"/{registration.Nsid}", [registration.HttpMethod], XrpcErrorResponse.Handle(registration.Invoke, logger))
                .WithMetadata(registration.Metadata);
        }

        // Ordered after every other endpoint, so it answers only what nothing else matched —
        // including routes an application maps under /xrpc itself.
        group.MapFallback("/{nsid}", XrpcErrorResponse.Handle(XrpcErrorResponse.Unmatched(registry.Registrations), logger));

        return group;
    }

    private static XrpcEndpointRegistry GetOrCreateRegistry(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(XrpcEndpointRegistry)
                && descriptor.ImplementationInstance is XrpcEndpointRegistry existing)
            {
                return existing;
            }
        }

        var registry = new XrpcEndpointRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
