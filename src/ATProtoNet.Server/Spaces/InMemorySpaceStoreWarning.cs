using ATProtoNet.Server.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Says once, at startup, when single-use tokens are tracked by the in-process default replay
/// store.
/// </summary>
/// <remarks>
/// <para>The default is deliberate — a single-instance service and a test host want it — but it
/// is silently wrong in a deployment that grew a second replica or a restart policy, and nothing
/// else would ever say so: a captured delegation token is caught only by the instance that saw
/// the original, and the symptom is an accepted replay rather than an error.</para>
/// <para>Set <see cref="SpaceServerOptions.WarnOnInMemoryStores"/> to <see langword="false"/> to
/// silence it where the default is the intended choice.</para>
/// </remarks>
internal sealed class InMemorySpaceStoreWarning(
    IServiceProvider services, SpaceServerOptions options, ILogger<InMemorySpaceStoreWarning> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.WarnOnInMemoryStores && services.GetService<IJtiReplayStore>() is InMemoryJtiReplayStore)
        {
            logger.LogWarning(
                "Space single-use tokens are tracked by {Store}, which is per-process: a delegation token, " +
                "client attestation, DPoP proof or service auth token replayed against another instance is " +
                "accepted, and one replayed after a restart is accepted too. Register a shared store " +
                "(AddAtProtoEfCoreJtiReplayStore) if more than one instance answers for this DID.",
                nameof(InMemoryJtiReplayStore));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
