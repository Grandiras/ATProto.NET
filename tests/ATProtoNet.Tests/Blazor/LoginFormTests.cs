using ATProtoNet.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.Blazor;

public class LoginFormTests
{
    [Fact]
    public async Task Render_WithoutLocalizationRegistered_UsesEnglishDefaults()
    {
        var html = await RenderLoginFormAsync(services => { });

        Assert.Contains("Sign in with your Atmosphere account", html);
        Assert.Contains("alice.bsky.social", html);
    }

    [Fact]
    public async Task Render_WithLocalizerRegistered_UsesLocalizedCopy()
    {
        var html = await RenderLoginFormAsync(services =>
            services.AddSingleton<IStringLocalizer<LoginForm>>(
                new StubLocalizer(new Dictionary<string, string>
                {
                    ["ButtonText"] = "Anmelden",
                })));

        Assert.Contains("Anmelden", html);
        // Keys the localizer does not know still fall back to the English defaults.
        Assert.Contains("alice.bsky.social", html);
    }

    [Fact]
    public async Task Render_WithLocalizerRegistered_ExplicitParameterWins()
    {
        var html = await RenderLoginFormAsync(
            services => services.AddSingleton<IStringLocalizer<LoginForm>>(
                new StubLocalizer(new Dictionary<string, string>
                {
                    ["ButtonText"] = "Anmelden",
                })),
            new Dictionary<string, object?> { ["ButtonText"] = "Log in" });

        Assert.Contains("Log in", html);
        Assert.DoesNotContain("Anmelden", html);
    }

    private static async Task<string> RenderLoginFormAsync(
        Action<IServiceCollection> configureServices,
        IDictionary<string, object?>? parameters = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager, TestNavigationManager>();
        configureServices(services);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<LoginForm>(
                ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            return output.ToHtmlString();
        });
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://example.com/", "https://example.com/login");
    }

    private sealed class StubLocalizer(IReadOnlyDictionary<string, string> strings) : IStringLocalizer<LoginForm>
    {
        public LocalizedString this[string name] => strings.TryGetValue(name, out var value)
            ? new LocalizedString(name, value, resourceNotFound: false)
            : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            strings.Select(pair => new LocalizedString(pair.Key, pair.Value, resourceNotFound: false));
    }
}
