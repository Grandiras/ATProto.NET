using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Pds;

/// <summary>
/// Forces <see cref="PdsSessionService"/> to be constructed during host startup so a missing or
/// malformed <see cref="PdsOptions.SessionSigningKey"/> is reported (a warning, or an exception
/// for an unparsable key) before the first request rather than on the first login attempt.
/// </summary>
internal sealed class PdsSessionKeyStartupCheck : IHostedService
{
    // Injecting the singleton is the whole check — building it emits the signing-key
    // diagnostics, and the DI container caches the instance for the endpoints to use.
    public PdsSessionKeyStartupCheck(PdsSessionService sessions) => _ = sessions;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
