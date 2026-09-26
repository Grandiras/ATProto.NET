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

    [Theory]
    [InlineData("access_denied", "Sign-in was cancelled.")]
    [InlineData("login_not_bound", "This sign-in was started in another browser. Please sign in again.")]
    [InlineData("invalid_handle", "We couldn&#x27;t find that account. Check the username and try again.")]
    [InlineData("state_expired", "This sign-in has expired. Please sign in again.")]
    [InlineData("login_failed", "Sign-in failed. Please try again.")]
    public async Task Render_AnErrorCode_ShowsItsMessage(string code, string message)
    {
        var html = await RenderLoginFormAsync(services => { }, uri: $"https://example.com/login?error={code}");

        Assert.Contains(message, html);
    }

    [Fact]
    public async Task Render_AnUnknownError_ShowsTheGenericMessageAndNotTheText()
    {
        // Anyone can link to the login page with any error text.
        var html = await RenderLoginFormAsync(
            services => { }, uri: "https://example.com/login?error=Your%20account%20is%20locked%2C%20call%20555-0100");

        Assert.Contains("Sign-in failed. Please try again.", html);
        Assert.DoesNotContain("locked", html);
    }

    [Fact]
    public async Task Render_AnErrorMessage_IsLocalizedByCode()
    {
        var html = await RenderLoginFormAsync(
            services => services.AddSingleton<IStringLocalizer<LoginForm>>(
                new StubLocalizer(new Dictionary<string, string> { ["Error_access_denied"] = "Abgebrochen." })),
            uri: "https://example.com/login?error=access_denied");

        Assert.Contains("Abgebrochen.", html);
    }

    private static async Task<string> RenderLoginFormAsync(
        Action<IServiceCollection> configureServices,
        IDictionary<string, object?>? parameters = null,
        string uri = "https://example.com/login")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNavigationManager(uri));
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
        public TestNavigationManager(string uri) => Initialize("https://example.com/", uri);
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
