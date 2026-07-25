using ATProtoNet.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    /// Keep the admin password out of source control — it grants full control over
    /// every account on the server.
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
    ///     var account = await pds.CreateAccountAsync(new CreatePdsAccountRequest
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

        var options = new PdsAdminOptions { Url = string.Empty, AdminPassword = string.Empty };
        builder.Configuration.GetSection(configurationSectionName).Bind(options);
        configureOptions?.Invoke(options);

        builder.Services.AddAtProtoPdsAdmin(options);

        return builder;
    }

    /// <summary>
    /// Registers a <see cref="PdsAdminClient"/> as a typed <see cref="HttpClient"/> with explicit options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The PDS URL and admin credentials.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// A missing URL or admin password fails here, while the host is being built. The
    /// client's own validation — notably the refusal to send the admin password over
    /// plaintext HTTP — runs when it is first resolved, so a URL that is present but
    /// unusable surfaces at that point rather than at startup.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the PDS URL or admin password is missing.
    /// </exception>
    public static IServiceCollection AddAtProtoPdsAdmin(
        this IServiceCollection services,
        PdsAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

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

        var baseAddress = new Uri(options.Url.TrimEnd('/') + "/");

        // Registered as a typed client rather than a singleton over one captured
        // HttpClient: the factory rotates the underlying handler, so a long-running
        // deployment picks up DNS changes behind the PDS URL.
        services
            .AddHttpClient(nameof(PdsAdminClient), httpClient => httpClient.BaseAddress = baseAddress)
            .AddTypedClient((httpClient, sp) => new PdsAdminClient(
                options,
                httpClient,
                sp.GetService<ILogger<PdsAdminClient>>()));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="PdsAdminClient"/> as a typed <see cref="HttpClient"/> for the given PDS.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pdsUrl">The PDS base URL.</param>
    /// <param name="adminPassword">The server's admin password.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtProtoPdsAdmin(
        this IServiceCollection services,
        string pdsUrl,
        string adminPassword) =>
        services.AddAtProtoPdsAdmin(new PdsAdminOptions
        {
            Url = pdsUrl,
            AdminPassword = adminPassword,
        });
}
