using ATProtoNet.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server;

/// <summary>
/// Extension methods for registering a <see cref="PdsAdminClient"/> — programmatic
/// admin access to a PDS your application manages.
/// </summary>
public static class PdsAdminExtensions
{
    /// <summary>
    /// The default configuration section bound to <see cref="PdsAdminOptions"/>.
    /// The <c>ATProtoNet.Aspire.Hosting</c> package populates this section on
    /// projects wired up with <c>WithAtProtoPds()</c>.
    /// </summary>
    public const string DefaultConfigurationSection = "AtProto:Pds";

    /// <summary>
    /// Registers a <see cref="PdsAdminClient"/> as a typed <see cref="HttpClient"/>, configured from the
    /// <c>AtProto:Pds</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expects <c>AtProto:Pds:Url</c> and <c>AtProto:Pds:AdminPassword</c>. In an
    /// Aspire solution these arrive automatically when the AppHost wires the project
    /// to the PDS container with <c>WithAtProtoPds(pds)</c>; otherwise set them in
    /// configuration, user secrets, or environment variables.
    /// </para>
    /// <para>
    /// A PDS that authenticates administrators as accounts rather than with a shared
    /// password — Tranquil, wired up by <c>WithAtProtoTranquilPds(pds)</c> — also sets
    /// <c>AtProto:Pds:Authentication</c> to <c>AdminAccount</c> and
    /// <c>AtProto:Pds:AdminIdentifier</c> to the administrator's handle, and
    /// <c>AtProto:Pds:AdminPassword</c> then holds that account's password.
    /// </para>
    /// <para>
    /// Keep the admin password out of source control — it grants full control over
    /// every account on the server.
    /// </para>
    /// <para>
    /// The options go through <see cref="IOptions{TOptions}"/> and are validated when the
    /// host starts: a missing URL or password, an account without its identifier, or a
    /// plaintext URL without <see cref="PdsAdminOptions.AllowInsecureHttp"/> stops it with an
    /// <see cref="OptionsValidationException"/> naming the configuration key.
    /// </para>
    /// <para>
    /// The client is registered as a typed <see cref="HttpClient"/>, so it is
    /// <em>transient</em> and the factory rotates the underlying handler. Resolve it from
    /// a scope — endpoint or controller injection does this for you. Resolving it from the
    /// root provider (for example inside a singleton, or via
    /// <c>app.Services.GetRequiredService</c>) leaves each instance tracked until the
    /// application shuts down; the instances are small and do not own their
    /// <see cref="HttpClient"/>, but they are not released.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddAtProtoPdsAdmin();
    ///
    /// app.MapPost("/signup", async (SignupForm form, PdsAdminClient pds) =>
    /// {
    ///     var account = await pds.CreateAccountAsync(new CreateAccountRequest
    ///     {
    ///         Handle = $"{form.Username}.pds.example.com",
    ///         Email = form.Email,
    ///         Password = form.Password,
    ///     });
    ///
    ///     return Results.Ok(new { account.Did, account.Handle });
    /// });
    /// </code>
    /// </example>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configurationSectionName">
    /// The configuration section to bind (default: <c>"AtProto:Pds"</c>).
    /// </param>
    /// <param name="configureOptions">Optional callback to override bound settings.</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder AddAtProtoPdsAdmin(
        this IHostApplicationBuilder builder,
        string configurationSectionName = DefaultConfigurationSection,
        Action<PdsAdminOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);

        // Configuration, not code: the callbacks post-configure, so code wins whatever the order.
        builder.Services.AddOptions<PdsAdminOptions>().Bind(builder.Configuration.GetSection(configurationSectionName));
        builder.Services.AddPdsAdminClient(configureOptions);

        return builder;
    }

    /// <summary>Registers a <see cref="PdsAdminClient"/> as a typed <see cref="HttpClient"/> for the given PDS.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pdsUrl">The PDS base URL.</param>
    /// <param name="adminPassword">The server's admin password.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// The options go through <see cref="IOptions{TOptions}"/>, so the other settings are
    /// <c>services.Configure&lt;PdsAdminOptions&gt;(…)</c>. They are validated when the host starts,
    /// and without a host when the client is first resolved.
    /// </remarks>
    public static IServiceCollection AddAtProtoPdsAdmin(
        this IServiceCollection services,
        string pdsUrl,
        string adminPassword)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddPdsAdminClient(options =>
        {
            options.Url = pdsUrl;
            options.AdminPassword = adminPassword;
        });
    }

    private static IServiceCollection AddPdsAdminClient(
        this IServiceCollection services, Action<PdsAdminOptions>? configure)
    {
        services.AddValidatedOptions(configure, ValidateOptions);

        // Registered as a typed client rather than a singleton over one captured
        // HttpClient: the factory rotates the underlying handler, so a long-running
        // deployment picks up DNS changes behind the PDS URL.
        services
            .AddHttpClient(nameof(PdsAdminClient))
            .ConfigurePrimaryHttpMessageHandler(Http.AtProtoHttp.CreateHandler)
            .AddTypedClient((httpClient, sp) => new PdsAdminClient(
                sp.GetRequiredService<IOptions<PdsAdminOptions>>().Value,
                httpClient,
                sp.GetService<ILogger<PdsAdminClient>>()));

        return services;
    }

    // The configuration mistakes that would otherwise surface on the first admin call, long after startup,
    // with the configuration key to fix.
    internal static void ValidateOptions(PdsAdminOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new InvalidOperationException(
                $"No PDS URL configured. Set '{DefaultConfigurationSection}:Url', or pass it explicitly. " +
                "In an Aspire solution, call WithAtProtoPds(pds) on the project resource.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            throw new InvalidOperationException(
                $"No PDS admin password configured. Set '{DefaultConfigurationSection}:AdminPassword', " +
                "or pass it explicitly. In an Aspire solution, call WithAtProtoPds(pds) on the project resource.");
        }

        if (options.Authentication == PdsAdminAuthentication.AdminAccount
            && string.IsNullOrWhiteSpace(options.AdminIdentifier))
        {
            throw new InvalidOperationException(
                $"'{DefaultConfigurationSection}:Authentication' is 'AdminAccount', which names an " +
                $"account rather than a shared password, but '{DefaultConfigurationSection}:AdminIdentifier' " +
                "is not set. In an Aspire solution, call WithAtProtoTranquilPds(pds) on the project resource.");
        }

        if (!Http.AtProtoHttp.TryNormalizeBaseUrl(options.Url, out var url))
        {
            throw new InvalidOperationException(
                $"'{DefaultConfigurationSection}:Url' must be an absolute http(s) URL with no query or fragment; got '{options.Url}'.");
        }

        if (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback && !options.AllowInsecureHttp)
        {
            throw new InvalidOperationException(
                $"Refusing to send the PDS admin credentials in the clear to '{url}'. Use HTTPS, or set " +
                $"'{DefaultConfigurationSection}:AllowInsecureHttp' if the PDS is only reachable over a private " +
                "network you trust.");
        }
    }
}
