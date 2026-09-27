using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Server;

// Registers the SDK's options classes the one way: through IOptions{TOptions}, so they bind from
// configuration like any other (services.Configure<T>(section)), validated when the host starts.
//
// The callbacks passed to the registration methods are post-configurations: code runs after every
// configuration bound to the options, whichever order the calls were made in.
internal static class AtProtoOptionsRegistration
{
    // Registers TOptions with configure, validated by validate when the host starts (and on first use
    // without a host), and the validated instance itself for the services that take it directly.
    //
    // services: The service collection.
    //
    // configure: Configures the options after anything bound from configuration, so code wins whatever the
    // order of the calls; null adds nothing. Several calls apply in order.
    //
    // validate: Throws an ArgumentException or InvalidOperationException naming what is wrong.
    //
    // Returns: The options builder, for more configuration.
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

    // A validator over a check that throws, reporting the exception's message.
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

    // Throws when value is neither positive nor infinite.
    public static void RequirePositive(TimeSpan value, string name)
    {
        if (value != Timeout.InfiniteTimeSpan && value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Must be positive, or Timeout.InfiniteTimeSpan.");
    }

    // Throws unless value is an absolute http or https URL.
    public static void RequireHttpUrl(string? value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"Must be an absolute http(s) URL; got '{value}'.", name);
        }
    }
}
