using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server;

/// <summary>
/// Registers the SDK's options classes the one way: through <see cref="IOptions{TOptions}"/>, so
/// they bind from configuration like any other (<c>services.Configure&lt;T&gt;(section)</c>),
/// validated when the host starts.
/// </summary>
/// <remarks>
/// The callbacks passed to the registration methods are post-configurations: code runs after
/// every configuration bound to the options, whichever order the calls were made in.
/// </remarks>
internal static class AtProtoOptionsRegistration
{
    /// <summary>
    /// Registers <typeparamref name="TOptions"/> with <paramref name="configure"/>, validated by
    /// <paramref name="validate"/> when the host starts (and on first use without a host), and the
    /// validated instance itself for the services that take it directly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configures the options after anything bound from configuration, so code wins whatever the
    /// order of the calls; <see langword="null"/> adds nothing. Several calls apply in order.
    /// </param>
    /// <param name="validate">Throws an <see cref="ArgumentException"/> or
    /// <see cref="InvalidOperationException"/> naming what is wrong.</param>
    /// <returns>The options builder, for more configuration.</returns>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services, Action<TOptions>? configure, Action<TOptions> validate)
        where TOptions : class
    {
        var options = services.AddOptions<TOptions>();
        if (configure is not null)
            options.PostConfigure(configure);

        options.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TOptions>>(new ThrowingOptionsValidator<TOptions>(validate)));
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<TOptions>>().Value);

        return options;
    }

    /// <summary>A validator over a check that throws, reporting the exception's message.</summary>
    private sealed class ThrowingOptionsValidator<TOptions>(Action<TOptions> validate) : IValidateOptions<TOptions>
        where TOptions : class
    {
        public ValidateOptionsResult Validate(string? name, TOptions options)
        {
            try
            {
                validate(options);
                return ValidateOptionsResult.Success;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return ValidateOptionsResult.Fail($"{typeof(TOptions).Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Throws when <paramref name="value"/> is neither positive nor infinite.</summary>
    public static void RequirePositive(TimeSpan value, string name)
    {
        if (value != Timeout.InfiniteTimeSpan && value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Must be positive, or Timeout.InfiniteTimeSpan.");
    }

    /// <summary>Throws unless <paramref name="value"/> is an absolute <c>http</c> or <c>https</c> URL.</summary>
    public static void RequireHttpUrl(string? value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"Must be an absolute http(s) URL; got '{value}'.", name);
        }
    }
}
