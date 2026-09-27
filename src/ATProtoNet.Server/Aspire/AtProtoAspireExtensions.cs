using ATProtoNet.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Aspire;

/// <summary>Extension methods for integrating ATProto.NET into .NET Aspire service defaults.</summary>
public static class AtProtoAspireExtensions
{
    /// <summary>The configuration section <see cref="AddAtProtoClient"/> binds by default.</summary>
    public const string DefaultConfigurationSection = "AtProto";

    /// <summary>
    /// Registers <see cref="AtProtoClient"/> (<see cref="AtProtoServiceCollectionExtensions.AddAtProto"/>)
    /// with its options bound from configuration, and a health check for the PDS it talks to.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configurationSectionName">
    /// The section bound to <see cref="AtProtoClientOptions"/> (default: <c>AtProto</c>). Its
    /// <c>DisableHealthChecks</c> key, when <see langword="true"/>, leaves out the health check.
    /// </param>
    /// <param name="configure">
    /// Configures the options after the configuration section and the connection string, and
    /// after them even when <c>services.AddAtProto(configure)</c> was called first: code wins.
    /// </param>
    /// <param name="connectionName">
    /// The name of a connection string (<c>ConnectionStrings:{name}</c>) holding the PDS URL, as
    /// an AppHost's <c>WithReference(pds)</c> supplies it. When present, it is the
    /// <see cref="AtProtoClientOptions.InstanceUrl"/>, over the configuration section.
    /// </param>
    /// <returns>The AT Protocol builder, for the rest of the registration.</returns>
    /// <remarks>
    /// <para>The health check is <see cref="AtProtoBuilderExtensions.WithHealthCheck"/>'s:
    /// <c>atproto-pds</c>, reporting <c>Degraded</c> when the PDS is unreachable, tagged
    /// <c>atproto</c> and <c>ready</c>.</para>
    /// <para>No resilience handler is added. Aspire service defaults add the standard one to every
    /// client; retries below the SDK resend non-idempotent requests and DPoP proofs, so remove it
    /// from this client (see <see cref="IAtProtoBuilder.HttpClient"/>).</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddServiceDefaults();
    /// builder.AddAtProtoClient(connectionName: "pds")
    ///     .HttpClient.RemoveAllResilienceHandlers();   // EXTEXP0001: experimental
    /// </code>
    /// </example>
    public static IAtProtoBuilder AddAtProtoClient(
        this IHostApplicationBuilder builder,
        string configurationSectionName = DefaultConfigurationSection,
        Action<AtProtoClientOptions>? configure = null,
        string? connectionName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);

        var section = builder.Configuration.GetSection(configurationSectionName);

        // Configuration, not code: AddAtProto's callbacks post-configure, so they win whichever
        // order this and AddAtProto(…) run in.
        var options = builder.Services.AddOptions<AtProtoClientOptions>().Bind(section);
        if (connectionName is not null && builder.Configuration.GetConnectionString(connectionName) is { Length: > 0 } url)
            options.Configure(o => o.InstanceUrl = url);

        var atproto = builder.Services.AddAtProto(configure);
        if (!section.GetValue<bool>("DisableHealthChecks"))
            atproto.WithHealthCheck();

        return atproto;
    }
}
