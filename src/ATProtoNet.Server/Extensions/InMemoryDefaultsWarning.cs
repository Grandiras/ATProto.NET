using ATProtoNet.Auth;
using ATProtoNet.Server.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server;

// Says once, at startup, which stores are still the in-process defaults a registration fell back to.
// Each is right for a single instance and silently wrong for two or across a restart, and nothing else
// would say so. A store the application registered itself, the in-memory one included, is a choice and
// is not warned about.
//
// It reads the registrations rather than resolving the stores: a store may be scoped, and resolving
// one the application never uses has costs of its own.
internal sealed class InMemoryDefaultsWarning(InMemoryFallbacks fallbacks, ILogger<InMemoryDefaultsWarning> logger) : IHostedService
{
    // Registers TDefault as the fallback for TService, and the warning.
    public static void TryAdd<TService, TDefault>(IServiceCollection services)
        where TService : class
        where TDefault : class, TService
    {
        if (services.Any(d => d.ServiceType == typeof(TService) && !d.IsKeyedService))
            return;

        var fallback = ServiceDescriptor.Singleton<TService, TDefault>();
        services.Add(fallback);

        if (services.FirstOrDefault(d => d.ServiceType == typeof(InMemoryFallbacks))?.ImplementationInstance is not InMemoryFallbacks registry)
        {
            registry = new InMemoryFallbacks(services);
            services.AddSingleton(registry);
            services.AddSingleton<IHostedService, InMemoryDefaultsWarning>();
        }

        registry.Descriptors.Add(fallback);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (fallbacks.IsInEffect<IAtProtoSessionStore>())
            logger.LogWarning(
                "Users' AT Protocol sessions are kept in memory ({Store}): a restart loses them, so everyone has " +
                "to sign in again, and another instance does not see them. Choose a store: WithFileSessionStore(), " +
                "WithEfCoreSessionStore<TContext>() from ATProtoNet.Server.EntityFrameworkCore, or " +
                "WithSessionStore<TStore>(); or call WithInMemorySessionStore() to keep this one.",
                nameof(InMemoryAtProtoSessionStore));

        if (fallbacks.IsInEffect<IJtiReplayStore>())
            logger.LogWarning(
                "Single-use tokens are tracked by {Store}, which is per-process: a service auth token, or a space " +
                "delegation token, client attestation or DPoP proof, replayed against another instance or after a " +
                "restart is accepted. Register a shared store (AddAtProtoEfCoreJtiReplayStore, from " +
                "ATProtoNet.Server.EntityFrameworkCore) if more than one instance answers for this service, or register " +
                "{Store} yourself to keep it.",
                nameof(InMemoryJtiReplayStore), nameof(InMemoryJtiReplayStore));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// The fallback registrations made, and the collection they were made in.
internal sealed class InMemoryFallbacks(IServiceCollection services)
{
    public List<ServiceDescriptor> Descriptors { get; } = [];

    // Whether the fallback for T is the registration the container resolves: the last one for the type, as
    // nothing registered later replaced it.
    public bool IsInEffect<T>() =>
        services.LastOrDefault(d => d.ServiceType == typeof(T) && !d.IsKeyedService) is { } last &&
        Descriptors.Contains(last);
}
