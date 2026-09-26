using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATProtoNet.Blazor;

/// <summary>
/// Registers what the AT Protocol Blazor components need.
/// </summary>
public static class AtProtoBlazorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AtProtoUserClientAccessor"/>, the scoped client through which
    /// <c>FeedView</c>, <c>PostCard</c>, <c>ProfileCard</c> and <c>ComposePost</c> act as the
    /// signed-in user.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// The accessor needs the client factory and the session store of <c>AddAtProtoServer()</c>,
    /// the hosted OAuth login of <c>AddAtProtoAuthentication()</c> (or another source of stored
    /// sessions and a user with a <c>did</c> claim), and Blazor's authentication state
    /// (<c>AddCascadingAuthenticationState()</c> in a Blazor Web App).
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAtProtoAuthentication();
    /// builder.Services.AddAtProtoServer();
    /// builder.Services.AddAtProtoBlazor();
    /// builder.Services.AddCascadingAuthenticationState();
    /// </code>
    /// </example>
    public static IServiceCollection AddAtProtoBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AtProtoUserClientAccessor>();
        return services;
    }
}
