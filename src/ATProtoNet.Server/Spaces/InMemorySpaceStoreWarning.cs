using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Says once, at startup, which of the space server's stores are still the in-process defaults.
/// </summary>
/// <remarks>
/// <para>The defaults are deliberate — a single-instance service and a test host want them — but
/// two of them are silently wrong in a deployment that grew a second replica or a restart
/// policy, and nothing else would ever say so. A replay store that is per-process means a
/// captured delegation token is caught only by the instance that saw the original; a member list
/// held in memory is gone after a restart and is not republished by anything on the
/// network.</para>
/// <para>Set <see cref="SpaceServerOptions.WarnOnInMemoryStores"/> to <see langword="false"/> to
/// silence it where the defaults are the intended choice.</para>
/// </remarks>
internal sealed class InMemorySpaceStoreWarning : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly SpaceServerOptions _options;
    private readonly ILogger<InMemorySpaceStoreWarning> _logger;

    public InMemorySpaceStoreWarning(
        IServiceProvider services, SpaceServerOptions options, ILogger<InMemorySpaceStoreWarning> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.WarnOnInMemoryStores)
            return Task.CompletedTask;

        if (_services.GetService<ISpaceReplayStore>() is InMemorySpaceReplayStore)
        {
            _logger.LogWarning(
                "Space single-use tokens are tracked by {Store}, which is per-process: a delegation token, " +
                "client attestation, or DPoP proof replayed against another instance is accepted, and one " +
                "replayed after a restart is accepted too. Register a shared store " +
                "(AddAtProtoRedisSpaceReplayStore, AddAtProtoEfCoreSpaceReplayStore) if more than one instance " +
                "answers for this DID.",
                nameof(InMemorySpaceReplayStore));
        }

        if (_services.GetService<ISimpleSpaceStore>() is InMemorySimpleSpaceStore)
        {
            _logger.LogWarning(
                "simplespace member lists are held by {Store} and are lost on restart. A member list is never " +
                "published to the network, so nothing rebuilds it — the spaces survive without their access " +
                "control. Register a durable store (AddAtProtoEfCoreSimpleSpace) for anything but development.",
                nameof(InMemorySimpleSpaceStore));
        }

        // The writer set is only what the authority claims, and the next notifyWrite from any
        // repo host restores an entry, so this one is worth saying and not worth warning about.
        if (_services.GetService<ISpaceAuthorityStore>() is InMemorySpaceAuthorityStore)
        {
            _logger.LogInformation(
                "Space writer sets are held by {Store} and start empty after a restart, until each repo host's " +
                "next notifyWrite restores its entry. Register a durable store " +
                "(AddAtProtoEfCoreSpaceAuthority) to keep syncers from seeing an empty space in the meantime.",
                nameof(InMemorySpaceAuthorityStore));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
