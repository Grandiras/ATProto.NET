using ATProtoNet.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Builds the space authority's service auth generator when the host starts, so a key
/// configuration that cannot sign acceptable service auth fails there rather than at the first
/// outbound notification.
/// </summary>
internal sealed class SpaceAuthorityStartupCheck(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = services.GetRequiredService<ServiceAuthGenerator>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
