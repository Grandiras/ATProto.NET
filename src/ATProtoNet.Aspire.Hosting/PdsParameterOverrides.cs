using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

// Shared plumbing for the With* overrides on both PDS resources.
internal static class PdsParameterOverrides
{
    // Replaces one of the auto-created parameters with a caller-supplied value, dropping the original from
    // the application model.
    //
    // The parameters are created up front, before any With* override can run. Left in the model, a
    // superseded one still appears in a published manifest as an input, so a deployment would be prompted
    // for a value nothing reads.
    public static void Replace<TResource>(
        IResourceBuilder<TResource> builder,
        object? current,
        Action<TResource> assign)
        where TResource : IResource
    {
        if (current is ParameterResource superseded)
            builder.ApplicationBuilder.Resources.Remove(superseded);

        assign(builder.Resource);
    }
}
