using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ATProtoNet.Aspire.Hosting;

namespace ATProtoNet.Tests.Aspire;

/// <summary>
/// The PDS servers <c>ATProtoNet.Aspire.Hosting</c> can add, for theories over the
/// behaviour both share.
/// </summary>
public enum PdsFlavour
{
    /// <summary>The reference Bluesky PDS, added with <c>AddAtProtoPds</c>.</summary>
    Reference,

    /// <summary>Tranquil PDS, added with <c>AddAtProtoTranquilPds</c>.</summary>
    Tranquil,
}

/// <summary>
/// Builders and inspection helpers for tests over the Aspire application model.
/// </summary>
internal static class AspireTestHost
{
    public static IDistributedApplicationBuilder PublishModeBuilder()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--publisher", "manifest", "--output-path", "manifest.json"] });

        Assert.True(builder.ExecutionContext.IsPublishMode, "expected a publish-mode builder");
        return builder;
    }

    public static T SingleResource<T>(this IDistributedApplicationBuilder builder) where T : IResource =>
        builder.Resources.OfType<T>().Single();

    /// <summary>
    /// Runs every environment callback on <paramref name="resource"/>, in order, and returns
    /// the variables they leave behind.
    /// </summary>
    public static async Task<Dictionary<string, object>> GetEnvironmentAsync(IResource resource)
    {
        var env = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            resource,
            env,
            CancellationToken.None);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return env;
    }

    public static IResourceBuilder<AtProtoPdsContainerResourceBase> AddPds(
        this IDistributedApplicationBuilder builder,
        PdsFlavour flavour,
        string name = "pds",
        int? port = null,
        string? tag = null) => flavour switch
        {
            PdsFlavour.Reference => builder.AddAtProtoPds(name, port, tag),
            PdsFlavour.Tranquil => builder.AddAtProtoTranquilPds(name, port, tag),
            _ => throw new ArgumentOutOfRangeException(nameof(flavour)),
        };

    /// <summary>
    /// <c>WithAtProtoPds</c> or <c>WithAtProtoTranquilPds</c>, whichever fits the PDS.
    /// </summary>
    public static IResourceBuilder<T> WithPds<T>(
        this IResourceBuilder<T> consumer,
        IResourceBuilder<AtProtoPdsContainerResourceBase> pds,
        bool waitForHealthy = true)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport => pds switch
        {
            IResourceBuilder<AtProtoPdsContainerResource> reference => consumer.WithAtProtoPds(reference, waitForHealthy),
            IResourceBuilder<AtProtoTranquilPdsContainerResource> tranquil => consumer.WithAtProtoTranquilPds(tranquil, waitForHealthy),
            _ => throw new ArgumentOutOfRangeException(nameof(pds)),
        };

    /// <summary>
    /// <c>WithDataBindMount</c>/<c>WithDataVolume</c> or
    /// <c>WithBlobBindMount</c>/<c>WithBlobVolume</c>, whichever fits the PDS.
    /// </summary>
    public static void ReplaceStorage(
        this IResourceBuilder<AtProtoPdsContainerResourceBase> pds, bool bindMount, string? source)
    {
        switch (pds)
        {
            case IResourceBuilder<AtProtoPdsContainerResource> reference:
                _ = bindMount ? reference.WithDataBindMount(source!) : reference.WithDataVolume(source);
                break;
            case IResourceBuilder<AtProtoTranquilPdsContainerResource> tranquil:
                _ = bindMount ? tranquil.WithBlobBindMount(source!) : tranquil.WithBlobVolume(source);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pds));
        }
    }
}
