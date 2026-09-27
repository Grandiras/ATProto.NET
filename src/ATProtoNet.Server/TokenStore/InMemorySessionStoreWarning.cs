using ATProtoNet.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.TokenStore;

/// <summary>Says once, at startup, when the users' sessions are kept in the in-memory default store.</summary>
/// <remarks>
/// The default keeps an application that never chose a store from writing tokens anywhere on
/// disk, but it loses every user's session on a restart, and a second instance never sees them;
/// nothing else would say so until users find themselves signed out of their accounts.
/// <c>WithInMemorySessionStore()</c> keeps it without the warning.
/// </remarks>
internal sealed class InMemorySessionStoreWarning(
    IServiceProvider services, ILogger<InMemorySessionStoreWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IAtProtoSessionStore>() is InMemoryAtProtoSessionStore &&
            services.GetService<InMemorySessionStoreChoice>() is null)
        {
            logger.LogWarning(
                "Users' AT Protocol sessions are kept in memory ({Store}): a restart loses them, so everyone has " +
                "to sign in again, and another instance does not see them. Choose a store: WithFileSessionStore(), " +
                "WithEfCoreSessionStore<TContext>() from ATProtoNet.Server.EntityFrameworkCore, or " +
                "WithSessionStore<TStore>(); or call WithInMemorySessionStore() to keep this one.",
                nameof(InMemoryAtProtoSessionStore));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Registered by <c>WithInMemorySessionStore()</c>: the in-memory store is the application's choice.</summary>
internal sealed class InMemorySessionStoreChoice;
