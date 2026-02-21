using System.Security.Claims;
using ATProtoNet.Blazor;
using ATProtoNet.Server;
using ATProtoNet.Server.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using ServerIntegrationSample.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 1. Configure standard cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

// 2. Register AT Proto OAuth (Blazor login)
builder.Services.AddAtProtoAuthentication(options =>
{
    options.ClientName = "ATProto.NET Server Integration Sample";
    options.LoginPath = "/login";
});

// 3. Register AT Proto Server (enables backend AT Proto access via IAtProtoClientFactory)
//    This also registers IAtProtoTokenStore, which the Blazor OAuth service
//    automatically uses to store tokens after login.
builder.Services.AddAtProtoServer();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 4. Map AT Proto OAuth endpoints
app.MapAtProtoOAuth();

// ─────────────────────────────────────────────
//  API Endpoints — AT Proto backend access
// ─────────────────────────────────────────────

// GET /api/profile — Fetch the authenticated user's Bluesky profile
app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
{
    await using var client = await factory.CreateClientForUserAsync(user);
    if (client is null)
        return Results.Unauthorized();

    try
    {
        var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
        return Results.Ok(new
        {
            profile.DisplayName,
            profile.Handle,
            profile.Did,
            profile.Description,
            profile.Avatar,
            profile.FollowersCount,
            profile.FollowsCount,
            profile.PostsCount,
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}).RequireAuthorization();

// GET /api/timeline — Fetch the authenticated user's Bluesky timeline
app.MapGet("/api/timeline", async (ClaimsPrincipal user, IAtProtoClientFactory factory, int? limit) =>
{
    await using var client = await factory.CreateClientForUserAsync(user);
    if (client is null)
        return Results.Unauthorized();

    try
    {
        var timeline = await client.Bsky.Feed.GetTimelineAsync(limit: limit ?? 10);
        var posts = timeline.Feed.Select(item =>
        {
            string? text = null;
            if (item.Post.Record.TryGetProperty("text", out var textProp))
                text = textProp.GetString();

            return new
            {
                Author = item.Post.Author.Handle,
                AuthorName = item.Post.Author.DisplayName,
                Text = text,
                item.Post.LikeCount,
                item.Post.RepostCount,
                item.Post.ReplyCount,
                IndexedAt = item.Post.IndexedAt,
            };
        });
        return Results.Ok(posts);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}).RequireAuthorization();

app.Run();
