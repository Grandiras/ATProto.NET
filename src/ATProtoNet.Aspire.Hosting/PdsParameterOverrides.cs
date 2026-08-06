using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Shared plumbing for the <c>With*</c> overrides on both PDS resources.
/// </summary>
internal static class PdsParameterOverrides
{
    /// <summary>
    /// Replaces one of the auto-created parameters with a caller-supplied value, dropping
    /// the original from the application model.
    /// </summary>
    /// <remarks>
    /// The parameters are created up front, before any <c>With*</c> override can run. Left
    /// in the model, a superseded one still appears in a published manifest as an input,
    /// so a deployment would be prompted for a value nothing reads.
    /// </remarks>
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
