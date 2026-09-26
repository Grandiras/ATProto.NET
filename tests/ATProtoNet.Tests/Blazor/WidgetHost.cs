using System.Security.Claims;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Blazor;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Services;
using ATProtoNet.Tests.Auth;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Blazor;

/// <summary>
/// A bUnit context wired as an application is: <c>AddAtProtoBlazor()</c> over the real client
/// factory and a session store, with the PDS and AppView answered by a <see cref="StubServer"/>.
/// </summary>
internal sealed class WidgetHost : IAsyncDisposable
{
    public const string Cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";

    public WidgetHost(bool signedIn = true)
    {
        var httpClients = Substitute.For<IHttpClientFactory>();
        httpClients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(Server, disposeHandler: false));

        Context.Services.AddLogging();
        Context.Services.AddSingleton<IAtProtoSessionStore>(Store);
        Context.Services.AddSingleton<IAtProtoClientFactory>(
            new AtProtoClientFactory(Store, httpClients, NullLoggerFactory.Instance));
        Context.Services.AddAtProtoBlazor();

        Authorization = Context.AddAuthorization();
        if (signedIn)
            SignIn();
    }

    public BunitContext Context { get; } = new();

    public StubServer Server { get; } = new();

    public InMemoryAtProtoSessionStore Store { get; } = new();

    public BunitAuthorizationContext Authorization { get; }

    /// <summary>Signs Alice in, with a stored session the factory restores.</summary>
    public void SignIn()
    {
        Store.SetAsync(PasswordSession(AccessJwt("a1"), "r1")).AsTask().GetAwaiter().GetResult();
        Authorization.SetAuthorized(AliceHandle.Value);
        Authorization.SetClaims(new Claim(AtProtoClaimTypes.Did, Alice.Value), new Claim(AtProtoClaimTypes.AuthMethod, "oauth"));
    }

    /// <summary>A post by Alice, as the AppView returns it.</summary>
    public static string PostJson(string rkey, string text, int likes = 0, string? like = null, int reposts = 0)
    {
        var viewer = like is null ? "" : $$""","viewer":{"like":"{{like}}"}""";
        return $$"""
            {"uri":"at://{{Alice.Value}}/app.bsky.feed.post/{{rkey}}","cid":"{{Cid}}",
             "author":{"did":"{{Alice.Value}}","handle":"alice.test","displayName":"Alice"},
             "record":{"$type":"app.bsky.feed.post","text":"{{text}}","createdAt":"2024-01-01T00:00:00Z"},
             "indexedAt":"2024-01-01T00:00:00Z","likeCount":{{likes}},"repostCount":{{reposts}}{{viewer}}}
            """;
    }

    public static PostView Post(string rkey, string text, int likes = 0, string? like = null) =>
        JsonSerializer.Deserialize<PostView>(PostJson(rkey, text, likes, like), AtProtoJsonDefaults.Options)!;

    public static HttpResponseMessage FeedResponse(string? cursor, params string[] posts) =>
        JsonResponse(
            $$"""{{{(cursor is null ? "" : $"\"cursor\":\"{cursor}\",")}}"feed":[{{string.Join(",", posts.Select(p => $"{{\"post\":{p}}}"))}}]}""");

    public static HttpResponseMessage RecordResponse(string collection, string rkey) =>
        JsonResponse($$"""{"uri":"at://{{Alice.Value}}/{{collection}}/{{rkey}}","cid":"{{Cid}}"}""");

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        Server.Dispose();
    }
}
