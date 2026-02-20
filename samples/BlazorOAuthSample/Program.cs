using ATProtoNet.Auth.OAuth;
using ATProtoNet.Blazor;
using BlazorOAuthSample.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register ATProto.NET with OAuth support
builder.Services.AddAtProtoBlazor(options =>
{
    options.InstanceUrl = "https://bsky.social";
    options.OAuth = new OAuthOptions
    {
        ClientMetadata = new OAuthClientMetadata
        {
            // For development, use the AT Protocol loopback client_id.
            // Custom redirect_uri path must be declared via query parameter.
            // Port numbers are ignored for matching; path must match exactly.
            // See: https://atproto.com/specs/oauth#localhost-client-development
            ClientId = "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%2Foauth%2Fcallback",
            ClientName = "ATProto.NET Blazor OAuth Sample",
            ClientUri = "http://127.0.0.1:5000",
            RedirectUris = ["http://127.0.0.1:5000/oauth/callback"],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            Scope = "atproto",
            TokenEndpointAuthMethod = "none",
            ApplicationType = "web",
            DpopBoundAccessTokens = true,
        },
        Scope = "atproto",
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
